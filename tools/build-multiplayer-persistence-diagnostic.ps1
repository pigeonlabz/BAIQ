[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $projectRoot
$projectFile = Join-Path $projectRoot 'src\InventoryExpanded\InventoryExpanded.csproj'
$referenceRoot = Join-Path $workspaceRoot 'dev-bootstrap\refs\MAIN_047f12'
$expectedAssemblyHash = 'F4F7CD358408CACE6267380B921DABF79946EBD688840A229F081751ED73518D'
$runId = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $projectRoot ('.release-work\multiplayer-persistence-diagnostic\runs\' + $runId)
$outputRoot = Join-Path $runRoot 'output'
$assetsFile = Join-Path $projectRoot 'src\InventoryExpanded\obj\project.assets.json'
$assemblyName = 'BackpackInventoryQoL-MultiplayerPersistence-Diagnostic'
$artifact = Join-Path $outputRoot ('IL2CPP\net6.0\' + $assemblyName + '.dll')

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet is required to build the diagnostic DLL.'
}
if (Test-Path -LiteralPath $runRoot) {
    throw "Refusing to reuse an existing diagnostic build directory: $runRoot"
}
if (-not (Test-Path -LiteralPath $assetsFile)) {
    throw "Offline build assets are missing: $assetsFile. Restore the project when NuGet is available, then retry."
}

$referenceAssembly = Join-Path $referenceRoot 'MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll'
$referenceManifest = Join-Path $referenceRoot 'reference-hashes.csv'
if (-not (Test-Path -LiteralPath $referenceAssembly) -or -not (Test-Path -LiteralPath $referenceManifest)) {
    throw "The preserved MAIN 0.4.7f12 reference profile is incomplete: $referenceRoot"
}
$actualAssemblyHash = (Get-FileHash -LiteralPath $referenceAssembly -Algorithm SHA256).Hash
if ($actualAssemblyHash -ne $expectedAssemblyHash) {
    throw "Wrong MAIN reference profile. Expected $expectedAssemblyHash, got $actualAssemblyHash."
}

New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $runRoot 'dotnet-cli-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:NUGET_PACKAGES = Join-Path $env:USERPROFILE '.nuget\packages'
$properties = @(
    '-p:PersistenceDiagnostics=true',
    ('-p:S1Dir=' + $referenceRoot),
    ('-p:BaseOutputPath=' + $outputRoot.TrimEnd('\') + '\'),
    '-p:DeployToGame=false'
)
$buildLog = Join-Path $runRoot 'build.log'

& dotnet build $projectFile -c IL2CPP --no-restore --nologo @properties *> $buildLog
if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath $buildLog -Tail 70
    throw "Diagnostic build failed. Log: $buildLog"
}
if (-not (Test-Path -LiteralPath $artifact)) {
    throw "Build succeeded without the expected isolated diagnostic DLL: $artifact"
}

$assembly = [System.Reflection.Assembly]::LoadFrom($artifact)
$actualName = [System.Reflection.AssemblyName]::GetAssemblyName($artifact).Name
if ($actualName -ne $assemblyName) {
    throw "Unexpected diagnostic assembly identity: $actualName"
}
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($artifact)
$melonAttributes = @($assembly.GetCustomAttributesData() |
    Where-Object { $_.AttributeType.FullName -eq 'MelonLoader.MelonInfoAttribute' })
if ($melonAttributes.Count -ne 1) {
    throw "Expected exactly one MAIN MelonInfo attribute, found $($melonAttributes.Count)."
}
$melonName = [string]$melonAttributes[0].ConstructorArguments[1].Value
$melonVersion = [string]$melonAttributes[0].ConstructorArguments[2].Value
$melonAuthor = [string]$melonAttributes[0].ConstructorArguments[3].Value
if (
    $melonName -ne 'Inventory Expanded - Backpack & Inventory QoL' -or
    $melonVersion -ne '1.3.1' -or
    $melonAuthor -ne 'PigeonLabs' -or
    $versionInfo.ProductName -ne 'Inventory Expanded' -or
    $versionInfo.ProductVersion -ne '1.3.1'
) {
    throw "MAIN product identity mismatch: Melon=$melonName/$melonVersion/$melonAuthor, product=$($versionInfo.ProductName)/$($versionInfo.ProductVersion)."
}
$diagnosticTypes = @(
    'Backpack.Patches.PlayerRequestSaveDiagnosticsPatch',
    'Backpack.Patches.PlayerManagerSaveDiagnosticsPatch',
    'Backpack.Patches.PlayerWriteDataDiagnosticsPatch'
)
foreach ($typeName in $diagnosticTypes) {
    if ($assembly.GetType($typeName, $false) -eq $null) {
        throw "Diagnostic instrumentation type is missing from the built DLL: $typeName"
    }
}

$patchType = $assembly.GetType('Backpack.Patches.PlayerManagerPatch', $true)
$patchMethods = @($patchType.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Static,DeclaredOnly') |
    Where-Object { $_.Name -eq 'TryGetPlayerData' })
if ($patchMethods.Count -ne 1) {
    throw "Expected one compiled BAIQ TryGetPlayerData patch, found $($patchMethods.Count)."
}
$patchAttributes = @($patchMethods[0].GetCustomAttributesData() |
    Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' })
if ($patchAttributes.Count -ne 1 -or $patchAttributes[0].ConstructorArguments.Count -lt 3) {
    throw 'The diagnostic DLL lost the explicit Harmony overload attribute.'
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
    throw "Compiled overload resolution is unexpected: $targetName($($targetTypes -join ', ')); variations=$($targetVariations -join ', ')."
}

$sha256 = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
$manifest = @(
    'Development-only multiplayer persistence diagnostic build — DO NOT PUBLISH',
    'Product: Inventory Expanded - Backpack & Inventory QoL (MAIN 1.3.1)',
    "Assembly identity: $actualName",
    "Product version: $($versionInfo.ProductVersion); Melon identity: $melonName $melonVersion by $melonAuthor",
    'Target: Schedule I 0.4.7f12 IL2CPP MAIN',
    "Reference Assembly-CSharp SHA-256: $actualAssemblyHash",
    "Diagnostic DLL: $artifact",
    "Diagnostic DLL SHA-256: $sha256",
    'Deployment: disabled; no game Mods folder was written',
    'Runtime verification: pending two-client in-game test'
)
$manifestPath = Join-Path $runRoot 'manifest.txt'
Set-Content -LiteralPath $manifestPath -Value $manifest

Write-Host 'Diagnostic build succeeded.'
Write-Host "DLL: $artifact"
Write-Host "SHA-256: $sha256"
Write-Host "Compiled Harmony target: $targetName(string, bool, out FullPlayerData)"
Write-Host "Restore/build logs and manifest: $runRoot"
Write-Host 'No regression test suite or live game session was run by this script.'
