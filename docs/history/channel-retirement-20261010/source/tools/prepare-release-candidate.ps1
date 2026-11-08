[CmdletBinding()]
param(
    [switch]$NoStage,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$StageName = '1.3.1'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $projectRoot
$projectFile = Join-Path $projectRoot 'src\InventoryExpanded\InventoryExpanded.csproj'
$runId = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$workRoot = Join-Path $projectRoot ('.release-work\1.3.1\runs\' + $runId)
$buildRoot = Join-Path $projectRoot '.release-work\1.3.1\build'
$stageRoot = Join-Path $projectRoot ('release-candidates\' + $StageName)
$profiles = @(
    [pscustomobject]@{ Name = 'MAIN'; Config = 'IL2CPP'; RefName = 'MAIN_047f12'; GameVersion = '0.4.7f12'; AssemblyHash = 'F4F7CD358408CACE6267380B921DABF79946EBD688840A229F081751ED73518D'; Version = '1.3.1'; Dll = 'BackpackInventoryQoL.dll' },
    [pscustomobject]@{ Name = 'BETA'; Config = 'IL2CPP_BETA_047'; RefName = 'BETA_047'; GameVersion = '0.4.7f6 reference profile'; AssemblyHash = '43DF688224AAC7FA9C8AF99234AA5E646334366A3FE5B6DCEA1548DE6B099E31'; Version = '1.3.0-beta.47'; Dll = 'BackpackInventoryQoL.Beta047.dll' }
)

function Assert-ReferenceProfile($profile, [string]$root) {
    $manifestPath = Join-Path $root 'reference-hashes.csv'
    $assemblyPath = Join-Path $root 'MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll'
    if (-not (Test-Path -LiteralPath $manifestPath) -or -not (Test-Path -LiteralPath $assemblyPath)) {
        throw "$($profile.Name) reference profile is incomplete: $root"
    }
    $actualHash = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
    Write-Host "$($profile.Name) references: $root"
    Write-Host "  game profile: $($profile.GameVersion); Assembly-CSharp SHA-256: $actualHash"
    if ($actualHash -ne $profile.AssemblyHash) {
        throw "$($profile.Name) reference hash mismatch. Expected $($profile.AssemblyHash), got $actualHash."
    }
    $manifest = @(Import-Csv -LiteralPath $manifestPath)
    $files = @(Get-ChildItem -LiteralPath (Join-Path $root 'MelonLoader') -File -Recurse)
    if ($files.Count -ne $manifest.Count) {
        throw "$($profile.Name) reference count mismatch: $($files.Count) files, $($manifest.Count) manifest entries."
    }
    foreach ($entry in $manifest) {
        $path = Join-Path $root $entry.Path
        if (-not (Test-Path -LiteralPath $path)) {
            throw "$($profile.Name) reference missing: $path"
        }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.SHA256) {
            throw "$($profile.Name) reference manifest mismatch: $path"
        }
    }
    Write-Host "  manifest: $($manifest.Count) files verified"
}

function Invoke-TargetBuild($profile, [string]$root) {
    $outDir = Join-Path $buildRoot ('output\' + $profile.Name)
    $objDir = Join-Path $buildRoot ('obj\' + $profile.Name)
    New-Item -ItemType Directory -Force -Path $outDir, $objDir | Out-Null
    $properties = @(
        ('-p:S1Dir=' + $root),
        ('-p:BaseOutputPath=' + $outDir + '\'),
        ('-p:BaseIntermediateOutputPath=' + $objDir + '\'),
        '-p:DeployToGame=false'
    )
    $restoreLog = Join-Path $workRoot ($profile.Name + '-restore.log')
    $buildLog = Join-Path $workRoot ($profile.Name + '-build.log')
    & dotnet restore $projectFile ('-p:Configuration=' + $profile.Config) @properties --nologo --ignore-failed-sources *> $restoreLog
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $restoreLog -Tail 35
        throw "$($profile.Name) restore failed. Log: $restoreLog"
    }
    & dotnet build $projectFile -c $profile.Config --no-restore --nologo @properties *> $buildLog
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $buildLog -Tail 45
        throw "$($profile.Name) build failed. Log: $buildLog"
    }
    $artifact = Join-Path $outDir ($profile.Config + '\net6.0\' + $profile.Dll)
    if (-not (Test-Path -LiteralPath $artifact)) {
        throw "$($profile.Name) build succeeded without expected DLL: $artifact"
    }
    $compileAuditProject = Join-Path $projectRoot 'tools\ProductionCompileListAudit\ProductionCompileListAudit.csproj'
    $compileAuditLog = Join-Path $workRoot ($profile.Name + '-production-compile-audit.log')
    & dotnet run --project $compileAuditProject -- $projectRoot $profile.Name $artifact *> $compileAuditLog
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $compileAuditLog -Tail 35
        throw "$($profile.Name) production compile-list audit failed. Log: $compileAuditLog"
    }
    Get-Content -LiteralPath $compileAuditLog | ForEach-Object { Write-Host $_ }
    $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($artifact)
    $assembly = [System.Reflection.Assembly]::LoadFrom($artifact)
    $melon = @($assembly.GetCustomAttributesData() | Where-Object { $_.AttributeType.FullName -eq 'MelonLoader.MelonInfoAttribute' })
    if ($melon.Count -ne 1) {
        throw "$($profile.Name) artifact has $($melon.Count) MelonInfo attributes; expected one."
    }
    $melonVersion = [string]$melon[0].ConstructorArguments[2].Value
    $melonAuthor = [string]$melon[0].ConstructorArguments[3].Value
    if (
        $info.ProductVersion -ne $profile.Version -or
        $melonVersion -ne $profile.Version -or
        $info.ProductName -ne 'Inventory Expanded' -or
        $info.CompanyName -ne 'PigeonLabs' -or
        $melonAuthor -ne 'PigeonLabs'
    ) {
        throw "$($profile.Name) version/product/author mismatch: product=$($info.ProductVersion), Melon=$melonVersion, name=$($info.ProductName), company=$($info.CompanyName), author=$melonAuthor."
    }
    $hash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
    $warnings = @(Select-String -LiteralPath $restoreLog, $buildLog -Pattern ': warning ' | ForEach-Object { $_.Line } | Sort-Object -Unique)
    Write-Host "$($profile.Name) build: $artifact"
    Write-Host "  Melon/Product version: $melonVersion / $($info.ProductVersion); SHA-256: $hash"
    if ($warnings.Count -gt 0) {
        Write-Host "  warnings: $($warnings.Count) unique (see run logs)"
    }
    return [pscustomobject]@{ Profile = $profile; Artifact = $artifact; Hash = $hash; Version = $melonVersion; Warnings = $warnings }
}

function Invoke-ManagedTests([string]$root) {
    $testProjects = @(
        [pscustomobject]@{ Name = 'MigrationCompatibilityTests'; Path = 'tests\MigrationCompatibilityTests\MigrationCompatibilityTests.csproj' },
        [pscustomobject]@{ Name = 'PostReleaseBugfixTests'; Path = 'tests\PostReleaseBugfixTests\PostReleaseBugfixTests.csproj' },
        [pscustomobject]@{ Name = 'PreReleasePolishTests'; Path = 'tests\PreReleasePolishTests\PreReleasePolishTests.csproj' }
    )
    $summaries = @()
    foreach ($test in $testProjects) {
        $testProject = Join-Path $root $test.Path
        $testLog = Join-Path $workRoot ($test.Name + '.log')
        & dotnet run --project $testProject --no-restore *> $testLog
        if ($LASTEXITCODE -ne 0) {
            Get-Content -LiteralPath $testLog -Tail 60
            throw "$($test.Name) failed. Log: $testLog"
        }
        $output = Get-Content -LiteralPath $testLog -Raw
        $summaryLine = ($output -split "`r?`n" | Where-Object { $_ -match 'tests? passed|scenarios?\.' } | Select-Object -Last 1)
        if ([string]::IsNullOrWhiteSpace($summaryLine)) {
            $summaryLine = 'passed (process exit code 0)'
        }
        $summaries += "$($test.Name): $($summaryLine.Trim())"
        Write-Host "Managed test passed: $($summaries[-1])"
    }
    return $summaries
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet is required to build release candidates.'
}
foreach ($profile in $profiles) {
    $root = Join-Path $workspaceRoot ('dev-bootstrap\refs\' + $profile.RefName)
    Assert-ReferenceProfile $profile $root
}

New-Item -ItemType Directory -Force -Path $workRoot | Out-Null
$testSummaries = Invoke-ManagedTests $projectRoot
$results = @()
foreach ($profile in $profiles) {
    $root = Join-Path $workspaceRoot ('dev-bootstrap\refs\' + $profile.RefName)
    $results += Invoke-TargetBuild $profile $root
}

$lines = @("Inventory Expanded 1.3.1 release candidate '$StageName' — UNRELEASED", 'Author: PigeonLabs', 'MAIN 0.4.7f12 compatibility fix; live inventory and save/reload checks are still required.', 'Managed tests:')
$lines += $testSummaries
$lines += ''
foreach ($result in $results) {
    $profile = $result.Profile
    $lines += @($profile.Name, "Version: $($result.Version)", "DLL: $($profile.Name)\$($profile.Dll)", "SHA-256: $($result.Hash)", "Reference profile: $($profile.RefName) ($($profile.GameVersion))", "Assembly-CSharp SHA-256: $($profile.AssemblyHash)", '')
}
$manifestText = ($lines -join [Environment]::NewLine).TrimEnd() + [Environment]::NewLine

if (-not $NoStage) {
    $stageManifest = Join-Path $stageRoot 'manifest.txt'
    if (Test-Path -LiteralPath $stageManifest) {
        $old = Get-Content -LiteralPath $stageManifest -Raw
        if ($old -ne $manifestText) {
            throw "Staged manifest differs; refusing to overwrite existing release candidate: $stageManifest"
        }
    }
    foreach ($result in $results) {
        $destinationDir = Join-Path $stageRoot $result.Profile.Name
        $destination = Join-Path $destinationDir $result.Profile.Dll
        if (Test-Path -LiteralPath $destination) {
            if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $result.Hash) {
                throw "Staged DLL differs; refusing overwrite: $destination"
            }
        } else {
            New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null
            Copy-Item -LiteralPath $result.Artifact -Destination $destination
        }
    }
    if (-not (Test-Path -LiteralPath $stageManifest)) {
        Set-Content -LiteralPath $stageManifest -Value $manifestText -NoNewline
    }
    Write-Host "Staged candidates: $stageRoot"
}
Write-Host "Build logs: $workRoot"
Write-Host $manifestText
