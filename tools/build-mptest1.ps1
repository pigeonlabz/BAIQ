[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $projectRoot
$projectFile = Join-Path $projectRoot 'src\InventoryExpanded\InventoryExpanded.csproj'
$referenceRoot = Join-Path $workspaceRoot 'dev-bootstrap\refs\MAIN_047f12'
$referenceAssembly = Join-Path $referenceRoot 'MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll'
$expectedAssemblyHash = 'F4F7CD358408CACE6267380B921DABF79946EBD688840A229F081751ED73518D'
$runId = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $projectRoot ('.release-work\MPTEST1\runs\' + $runId)
$outputRoot = Join-Path $runRoot 'output'
$intermediateRoot = Join-Path $runRoot 'obj'
$assemblyName = 'BackpackInventoryQoL-MPTEST1'
$artifact = Join-Path $outputRoot ('IL2CPP\net6.0\' + $assemblyName + '.dll')
$testSuites = @(
    [pscustomobject]@{ Name = 'MigrationCompatibilityTests'; Path = 'tests\MigrationCompatibilityTests\MigrationCompatibilityTests.csproj' },
    [pscustomobject]@{ Name = 'PostReleaseBugfixTests'; Path = 'tests\PostReleaseBugfixTests\PostReleaseBugfixTests.csproj' },
    [pscustomobject]@{ Name = 'PreReleasePolishTests'; Path = 'tests\PreReleasePolishTests\PreReleasePolishTests.csproj' }
)

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet is required to build MPTEST1.'
}
if (Test-Path -LiteralPath $runRoot) {
    throw "Refusing to reuse an existing MPTEST1 run directory: $runRoot"
}
if (-not (Test-Path -LiteralPath $referenceAssembly)) {
    throw "The pinned MAIN Schedule I 0.4.7f12 reference assembly is missing: $referenceAssembly"
}
$actualAssemblyHash = (Get-FileHash -LiteralPath $referenceAssembly -Algorithm SHA256).Hash
if ($actualAssemblyHash -ne $expectedAssemblyHash) {
    throw "Wrong Schedule I reference profile. Expected $expectedAssemblyHash, got $actualAssemblyHash."
}

New-Item -ItemType Directory -Force -Path $runRoot, $outputRoot, $intermediateRoot | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $runRoot 'dotnet-cli-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $env:USERPROFILE '.nuget\packages'
$testTempRoot = Join-Path $runRoot 'test-temp'
New-Item -ItemType Directory -Force -Path $testTempRoot | Out-Null
$env:TEMP = $testTempRoot
$env:TMP = $testTempRoot
$sdkLines = @(& dotnet --list-sdks)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not enumerate installed .NET SDKs.'
}
$sdkVersions = @(
    foreach ($line in $sdkLines) {
        if ($line -match '^\s*(\d+\.\d+\.\d+)\s+') {
            [version]$matches[1]
        }
    }
)
$testSdk = @($sdkVersions | Where-Object { $_.Major -eq 9 } | Sort-Object -Descending | Select-Object -First 1)
if ($testSdk.Count -gt 0) {
    $sdkSelection = @{ sdk = @{ version = $testSdk[0].ToString(); rollForward = 'disable' } } | ConvertTo-Json -Depth 3
    Set-Content -LiteralPath (Join-Path $runRoot 'global.json') -Value $sdkSelection
}

foreach ($suite in $testSuites) {
    $testProject = Join-Path $projectRoot $suite.Path
    $testLog = Join-Path $runRoot ($suite.Name + '.log')
    Push-Location $runRoot
    try {
        & dotnet run --project $testProject --no-restore *> $testLog
        $testExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    if ($testExitCode -ne 0) {
        Get-Content -LiteralPath $testLog -Tail 60
        throw "$($suite.Name) failed. Full log: $testLog"
    }
    Write-Host "$($suite.Name): passed"
}

$properties = @(
    '-p:MultiplayerPersistenceExperimental=true',
    '-p:PersistenceDiagnostics=true',
    '-p:Version=1.3.1.1',
    ('-p:S1Dir=' + $referenceRoot),
    ('-p:BaseOutputPath=' + $outputRoot.TrimEnd('\') + '\'),
    ('-p:BaseIntermediateOutputPath=' + $intermediateRoot.TrimEnd('\') + '\'),
    '-p:DeployToGame=false'
)
$restoreLog = Join-Path $runRoot 'restore.log'
$buildLog = Join-Path $runRoot 'build.log'
Push-Location $runRoot
try {
    & dotnet restore $projectFile '-p:Configuration=IL2CPP' @properties --nologo --ignore-failed-sources -p:NuGetAudit=false *> $restoreLog
    $restoreExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}
if ($restoreExitCode -ne 0) {
    Get-Content -LiteralPath $restoreLog -Tail 70
    throw "Isolated MPTEST1 restore failed. Full log: $restoreLog"
}
Push-Location $runRoot
try {
    & dotnet build $projectFile -c IL2CPP --no-restore --nologo @properties *> $buildLog
    $buildExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}
if ($buildExitCode -ne 0) {
    Get-Content -LiteralPath $buildLog -Tail 90
    throw "Isolated MPTEST1 build failed. Full log: $buildLog"
}
if (-not (Test-Path -LiteralPath $artifact)) {
    throw "Build succeeded without the isolated MPTEST1 DLL: $artifact"
}

$assembly = [System.Reflection.Assembly]::LoadFrom($artifact)
$actualName = [System.Reflection.AssemblyName]::GetAssemblyName($artifact).Name
$assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($artifact).Version.ToString()
$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($artifact)
$melonAttributes = @($assembly.GetCustomAttributesData() |
    Where-Object { $_.AttributeType.FullName -eq 'MelonLoader.MelonInfoAttribute' })
if ($actualName -ne $assemblyName -or $assemblyVersion -ne '1.3.1.1') {
    throw "MPTEST1 assembly identity mismatch: $actualName $assemblyVersion"
}
if ($melonAttributes.Count -ne 1) {
    throw "Expected exactly one MelonInfo attribute, found $($melonAttributes.Count)."
}
$melonName = [string]$melonAttributes[0].ConstructorArguments[1].Value
$melonVersion = [string]$melonAttributes[0].ConstructorArguments[2].Value
$melonAuthor = [string]$melonAttributes[0].ConstructorArguments[3].Value
if (
    $melonName -ne 'Inventory Expanded - Backpack & Inventory QoL' -or
    $melonVersion -ne '1.3.1-MPTEST1' -or
    $melonAuthor -ne 'PigeonLabs' -or
    $fileVersion.ProductName -ne 'Inventory Expanded' -or
    $fileVersion.ProductVersion -ne '1.3.1.1'
) {
    throw "MPTEST1 product identity mismatch: Melon=$melonName/$melonVersion/$melonAuthor, product=$($fileVersion.ProductName)/$($fileVersion.ProductVersion)."
}

foreach ($typeName in @(
    'Backpack.Patches.MultiplayerBackpackPersistence',
    'Backpack.Patches.MultiplayerBackpackStoredInstanceMutationPatch',
    'Backpack.Patches.MultiplayerBackpackQuantityMutationPatch',
    'Backpack.Patches.MultiplayerBackpackStorageDestroyPatch',
    'Backpack.Patches.PlayerRequestSaveDiagnosticsPatch',
    'Backpack.Patches.PlayerManagerSaveDiagnosticsPatch',
    'Backpack.Patches.PlayerWriteDataDiagnosticsPatch'
)) {
    if ($assembly.GetType($typeName, $false) -eq $null) {
        throw "Expected experimental/diagnostic type is missing: $typeName"
    }
}

$patchType = $assembly.GetType('Backpack.Patches.PlayerManagerPatch', $true)
$patchMethods = @($patchType.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Static,DeclaredOnly') |
    Where-Object { $_.Name -eq 'TryGetPlayerData' })
if ($patchMethods.Count -ne 1) {
    throw "Expected one compiled TryGetPlayerData patch, found $($patchMethods.Count)."
}
$patchAttributes = @($patchMethods[0].GetCustomAttributesData() |
    Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' })
if ($patchAttributes.Count -ne 1 -or $patchAttributes[0].ConstructorArguments.Count -lt 3) {
    throw 'Compiled MAIN lost the explicit Harmony overload attribute.'
}
$targetAttribute = $patchAttributes[0]
$targetName = [string]$targetAttribute.ConstructorArguments[0].Value
$targetTypes = @($targetAttribute.ConstructorArguments[1].Value | ForEach-Object { $_.Value.FullName })
$targetVariations = @($targetAttribute.ConstructorArguments[2].Value | ForEach-Object { [int]$_.Value })
if (
    $targetName -ne 'TryGetPlayerData' -or
    ($targetTypes -join '|') -ne 'System.String|System.Boolean|Il2CppScheduleOne.PlayerScripts.FullPlayerData' -or
    $targetVariations.Count -ne 3 -or
    $targetVariations[2] -ne 2
) {
    throw "Compiled Harmony target is unexpected: $targetName($($targetTypes -join ', '))."
}

$expectedMutationTargets = @{
    'Backpack.Patches.MultiplayerBackpackStoredInstanceMutationPatch' = @{
        Method = 'SetStoredInstance_Internal'
        Arguments = @('Il2CppFishNet.Connection.NetworkConnection', 'System.Int32', 'Il2CppScheduleOne.ItemFramework.ItemInstance')
    }
    'Backpack.Patches.MultiplayerBackpackQuantityMutationPatch' = @{
        Method = 'SetItemSlotQuantity_Internal'
        Arguments = @('System.Int32', 'System.Int32')
    }
}
foreach ($entry in $expectedMutationTargets.GetEnumerator()) {
    $type = $assembly.GetType($entry.Key, $true)
    $attributes = @($type.GetCustomAttributesData() |
        Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' })
    if ($attributes.Count -ne 1) {
        throw "$($entry.Key) must have one compiled Harmony target."
    }
    $attribute = $attributes[0]
    $compiledMethodName = [string]$attribute.ConstructorArguments[1].Value
    if ($attribute.ConstructorArguments.Count -lt 3) {
        throw "$($entry.Key) does not have explicit compiled parameter types."
    }
    $compiledArgumentTypes = @($attribute.ConstructorArguments[2].Value | ForEach-Object { $_.Value.FullName })
    if (
        $compiledMethodName -ne $entry.Value.Method -or
        ($compiledArgumentTypes -join '|') -ne ($entry.Value.Arguments -join '|')
    ) {
        throw "$($entry.Key) target mismatch: $compiledMethodName($($compiledArgumentTypes -join ', '))."
    }
}

$sha256 = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
$manifest = @(
    'EXPERIMENTAL DEVELOPMENT BUILD — MPTEST1 — DO NOT PUBLISH OR DEPLOY',
    'Product: Inventory Expanded - Backpack & Inventory QoL',
    "Assembly identity: $actualName $assemblyVersion",
    "Melon identity: $melonName $melonVersion by $melonAuthor",
    'Target: Schedule I 0.4.7f12 IL2CPP MAIN reference profile',
    "Reference Assembly-CSharp SHA-256: $actualAssemblyHash",
    "DLL: $artifact",
    "DLL SHA-256: $sha256",
    'Deployment: disabled; this script does not write to a game Mods directory',
    'Compiled Harmony: explicit TryGetPlayerData(string, bool, out FullPlayerData)',
    'Compiled native mutation hooks: explicit StorageEntity stored instance and quantity setter signatures',
    'Automated checks: MigrationCompatibilityTests, PostReleaseBugfixTests, PreReleasePolishTests passed before build',
    'Runtime verification: pending two-client in-game test; do not publish before acceptance'
)
$manifestPath = Join-Path $runRoot 'manifest.txt'
Set-Content -LiteralPath $manifestPath -Value $manifest

Write-Host 'MPTEST1 isolated build succeeded; no deployment was performed.'
Write-Host "DLL: $artifact"
Write-Host "SHA-256: $sha256"
Write-Host "Compiled Harmony target: $targetName(string, bool, out FullPlayerData)"
Write-Host "Build logs and manifest: $runRoot"
