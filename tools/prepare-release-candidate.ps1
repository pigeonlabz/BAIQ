[CmdletBinding()]
param(
    [switch]$NoStage,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$StageName = '1.3.3-main-047f12-shift-click-priority-rc1-20261028',
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [Alias('SourceCheckpoint')]
    [string]$CheckpointName = '20261011-host-settings-sync-rc3-final'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $projectRoot
$projectFile = Join-Path $projectRoot 'src\InventoryExpanded\InventoryExpanded.csproj'
$runId = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$workRoot = Join-Path $projectRoot ('.release-work\1.3.2-main\runs\' + $runId)
$buildRoot = Join-Path $workRoot 'build'
$stageRoot = Join-Path $projectRoot ('release-candidates\' + $StageName)
$sourceCheckpoint = '.release-work\1.3.2-integration\checkpoints\' + $CheckpointName
$profiles = @(
    [pscustomobject]@{ Name = 'MAIN'; Config = 'IL2CPP'; RefName = 'MAIN_047f12'; GameVersion = '0.4.7f12'; AssemblyHash = 'F4F7CD358408CACE6267380B921DABF79946EBD688840A229F081751ED73518D'; Version = '1.3.3'; Dll = 'BackpackInventoryQoL.dll' }
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
    $patchType = $assembly.GetType('Backpack.Patches.PlayerManagerPatch', $true)
    $patchMethods = @($patchType.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Static,DeclaredOnly') |
        Where-Object { $_.Name -eq 'TryGetPlayerData' })
    if ($patchMethods.Count -ne 1) {
        throw "$($profile.Name) compiled assembly has $($patchMethods.Count) PlayerManagerPatch.TryGetPlayerData methods; expected one."
    }
    $patchAttributes = @($patchMethods[0].GetCustomAttributesData() |
        Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' })
    if ($patchAttributes.Count -ne 1 -or $patchAttributes[0].ConstructorArguments.Count -lt 3) {
        throw "$($profile.Name) compiled TryGetPlayerData patch does not carry one explicit Harmony target attribute."
    }
    $patchAttribute = $patchAttributes[0]
    $targetName = [string]$patchAttribute.ConstructorArguments[0].Value
    $targetTypes = @($patchAttribute.ConstructorArguments[1].Value | ForEach-Object { $_.Value.FullName })
    $targetVariations = @($patchAttribute.ConstructorArguments[2].Value | ForEach-Object { [int]$_.Value })
    if (
        $targetName -ne 'TryGetPlayerData' -or
        ($targetTypes -join '|') -ne 'System.String|System.Boolean|Il2CppScheduleOne.PlayerScripts.FullPlayerData' -or
        $targetVariations.Count -ne 3 -or
        $targetVariations[2] -ne 2
    ) {
        throw "$($profile.Name) compiled Harmony target is not TryGetPlayerData(string, bool, out FullPlayerData): name=$targetName, types=$($targetTypes -join ', '), variations=$($targetVariations -join ', ')."
    }
    Write-Host "$($profile.Name) compiled Harmony metadata: $targetName(string, bool, out FullPlayerData) verified."
    $assemblyIdentity = [System.Reflection.AssemblyName]::GetAssemblyName($artifact)
    if ($assemblyIdentity.Name -ne 'BackpackInventoryQoL' -or $assemblyIdentity.Version.ToString() -ne '1.3.3.0') {
        throw "$($profile.Name) assembly identity mismatch: $($assemblyIdentity.Name) $($assemblyIdentity.Version)."
    }
    foreach ($typeName in @(
        'Backpack.Patches.MultiplayerBackpackPersistence',
        'Backpack.Patches.MultiplayerBackpackStoredInstanceMutationPatch',
        'Backpack.Patches.MultiplayerBackpackQuantityMutationPatch',
        'Backpack.Patches.MultiplayerBackpackStorageDestroyPatch',
        'Backpack.AtomicTextFileWriter',
        'Backpack.Config.GameplaySettingsPolicy',
        'Backpack.Config.GameplaySettingsSyncManager',
        'Backpack.Patches.PackagingStationsProductionPatch'
    )) {
        if ($null -eq $assembly.GetType($typeName, $false)) {
            throw "$($profile.Name) candidate does not contain required production persistence type $typeName."
        }
    }
    foreach ($typeName in @(
        'Backpack.Patches.BackpackPersistenceDiagnostics',
        'Backpack.Patches.PlayerRequestSaveDiagnosticsPatch',
        'Backpack.Patches.FishNetStorageAuthorizationTrace',
        'Backpack.Patches.FishNetStoredInstanceMutationDiagnosticsPatch'
    )) {
        if ($null -ne $assembly.GetType($typeName, $false)) {
            throw "$($profile.Name) production candidate contains development-only type $typeName."
        }
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
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot $sourceCheckpoint))) {
    throw "Source integration checkpoint is missing: $(Join-Path $projectRoot $sourceCheckpoint)"
}
$sourceCheckpointRoot = Join-Path $projectRoot $sourceCheckpoint
if (-not (Test-Path -LiteralPath (Join-Path $sourceCheckpointRoot 'source-manifest.csv'))) {
    throw "Source integration checkpoint has no source manifest: $sourceCheckpointRoot"
}
$checkpointManifest = @(Import-Csv -LiteralPath (Join-Path $sourceCheckpointRoot 'source-manifest.csv'))
if ($checkpointManifest.Count -eq 0) {
    throw "Source integration checkpoint manifest is empty: $sourceCheckpointRoot"
}
foreach ($entry in $checkpointManifest) {
    $currentPath = Join-Path $projectRoot $entry.Path
    $snapshotPath = Join-Path (Join-Path $sourceCheckpointRoot 'source') $entry.Path
    if (-not (Test-Path -LiteralPath $currentPath) -or -not (Test-Path -LiteralPath $snapshotPath)) {
        throw "Checkpoint source is missing from the working tree or snapshot: $($entry.Path)"
    }
    $snapshotHash = (Get-FileHash -LiteralPath $snapshotPath -Algorithm SHA256).Hash
    $currentHash = (Get-FileHash -LiteralPath $currentPath -Algorithm SHA256).Hash
    if ($snapshotHash -ne $entry.SHA256 -or $currentHash -ne $entry.SHA256) {
        throw "Working source differs from checkpoint manifest: $($entry.Path)"
    }
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

$lines = @("Inventory Expanded 1.3.3 unified MAIN release candidate '$StageName' — UNRELEASED", 'Author: PigeonLabs', "Source checkpoint: $sourceCheckpoint (workspace has no .git metadata)", 'One MAIN artifact based on the BETA-tested 0.4.7 lifecycle and the validated MPTEST1 guest-persistence implementation. Host gameplay settings use Steam owner-authenticated lobby metadata; the host requires exact write/readback and guests fail closed until a matching policy arrives. Startup identity recovery and Main-scene revalidation are enabled. Phone presentation retries transient geometry while keeping the panel read-only until policy readiness.', 'Exact-candidate single-player and two-client runtime acceptance, including startup policy recovery, phone app/layout transitions, production authority and quantity conservation, is still required.', 'Mk1 Packaging scaling is enabled through verified native transactions; Mk2 remains native. Guest slot-lock replication remains unverified.', 'FishNet cross-owner native RPC authorization remains unproven; this persistence change adds no RPC endpoint.', 'Managed tests:')
$lines += $testSummaries
$lines += ''
foreach ($result in $results) {
    $profile = $result.Profile
    $assemblyIdentity = [System.Reflection.AssemblyName]::GetAssemblyName($result.Artifact)
    $lines += @($profile.Name, 'Build profile: IL2CPP', "Assembly identity: $($assemblyIdentity.Name) $($assemblyIdentity.Version)", "Melon version: $($result.Version)", "DLL: $($profile.Name)\$($profile.Dll)", "SHA-256: $($result.Hash)", "Reference profile: $($profile.RefName) ($($profile.GameVersion))", "Assembly-CSharp SHA-256: $($profile.AssemblyHash)", '')
    if ($result.Warnings.Count -gt 0) {
        $warningCodes = @($result.Warnings | ForEach-Object {
            if ($_ -match 'warning ([A-Z]+\d+):') { $Matches[1] }
        } | Sort-Object -Unique)
        $lines += "Build/restore warnings: $($result.Warnings.Count) unique ($($warningCodes -join ', ')); see run logs."
    } else {
        $lines += 'Build/restore warnings: none.'
    }
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
