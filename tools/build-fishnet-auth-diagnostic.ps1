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
$runRoot = Join-Path $projectRoot ('.release-work\fishnet-auth-diagnostic\runs\' + $runId)
$outputRoot = Join-Path $runRoot 'output'
$intermediateRoot = Join-Path $runRoot 'obj'
$tempRoot = Join-Path $runRoot 'temp'
$assemblyName = 'BackpackInventoryQoL-FishNetAuth-Diagnostic'
$artifact = Join-Path $outputRoot ('IL2CPP\net6.0\' + $assemblyName + '.dll')

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet is required to build the FishNet authorization diagnostic DLL.'
}
if (Test-Path -LiteralPath $runRoot) {
    throw "Refusing to reuse an existing diagnostic run directory: $runRoot"
}
if (-not (Test-Path -LiteralPath $referenceAssembly)) {
    throw "The pinned MAIN Schedule I 0.4.7f12 reference assembly is missing: $referenceAssembly"
}
$actualAssemblyHash = (Get-FileHash -LiteralPath $referenceAssembly -Algorithm SHA256).Hash
if ($actualAssemblyHash -ne $expectedAssemblyHash) {
    throw "Wrong MAIN reference profile. Expected $expectedAssemblyHash, got $actualAssemblyHash."
}

New-Item -ItemType Directory -Force -Path $runRoot, $outputRoot, $intermediateRoot, $tempRoot | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $runRoot 'dotnet-cli-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $env:USERPROFILE '.nuget\packages'
$env:TEMP = $tempRoot
$env:TMP = $tempRoot

$properties = @(
    '-p:FishNetAuthorizationDiagnostics=true',
    '-p:PersistenceDiagnostics=true',
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
    throw "Isolated diagnostic restore failed. Full log: $restoreLog"
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
    Get-Content -LiteralPath $buildLog -Tail 100
    throw "FishNet authorization diagnostic build failed. Full log: $buildLog"
}
if (-not (Test-Path -LiteralPath $artifact)) {
    throw "Build succeeded without the isolated diagnostic DLL: $artifact"
}

$assembly = [System.Reflection.Assembly]::LoadFrom($artifact)
$actualName = [System.Reflection.AssemblyName]::GetAssemblyName($artifact).Name
$assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($artifact).Version.ToString()
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($artifact)
if ($actualName -ne $assemblyName -or $assemblyVersion -ne '1.3.1.2') {
    throw "Unexpected diagnostic assembly identity: $actualName $assemblyVersion"
}

$melonAttributes = @($assembly.GetCustomAttributesData() |
    Where-Object { $_.AttributeType.FullName -eq 'MelonLoader.MelonInfoAttribute' })
if ($melonAttributes.Count -ne 1) {
    throw "Expected exactly one diagnostic MelonInfo attribute, found $($melonAttributes.Count)."
}
$melonName = [string]$melonAttributes[0].ConstructorArguments[1].Value
$melonVersion = [string]$melonAttributes[0].ConstructorArguments[2].Value
$melonAuthor = [string]$melonAttributes[0].ConstructorArguments[3].Value
if (
    $melonName -ne 'Inventory Expanded - FishNet Authorization Diagnostic' -or
    $melonVersion -ne '1.3.1-FISHNET-AUTH-DIAGNOSTIC' -or
    $melonAuthor -ne 'PigeonLabs' -or
    $versionInfo.ProductName -ne 'Inventory Expanded' -or
    $versionInfo.ProductVersion -ne '1.3.1.2'
) {
    throw "Diagnostic identity mismatch: Melon=$melonName/$melonVersion/$melonAuthor, product=$($versionInfo.ProductName)/$($versionInfo.ProductVersion)."
}

foreach ($typeName in @(
    'Backpack.Patches.FishNetSetStoredInstanceReaderDiagnosticsPatch',
    'Backpack.Patches.FishNetSetItemSlotQuantityReaderDiagnosticsPatch',
    'Backpack.Patches.FishNetStoredInstanceMutationDiagnosticsPatch',
    'Backpack.Patches.FishNetQuantityMutationDiagnosticsPatch',
    'Backpack.Patches.PlayerRequestSaveDiagnosticsPatch',
    'Backpack.Patches.PlayerManagerSaveDiagnosticsPatch',
    'Backpack.Patches.PlayerWriteDataDiagnosticsPatch'
)) {
    if ($assembly.GetType($typeName, $false) -eq $null) {
        throw "Expected FishNet/save diagnostic type is missing: $typeName"
    }
}
if ($assembly.GetType('Backpack.Patches.MultiplayerBackpackPersistence', $false) -ne $null) {
    throw 'The read-only diagnostic unexpectedly contains MPTEST1 persistence behavior.'
}

function Assert-HarmonyTarget([string]$typeName, [string]$methodName, [string[]]$argumentTypes) {
    $type = $assembly.GetType($typeName, $true)
    $attributes = @($type.GetCustomAttributesData() |
        Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' })
    if ($attributes.Count -ne 1 -or $attributes[0].ConstructorArguments.Count -lt 3) {
        throw "$typeName must have one explicit Harmony target."
    }
    $attribute = $attributes[0]
    $compiledMethod = [string]$attribute.ConstructorArguments[1].Value
    $compiledArguments = @($attribute.ConstructorArguments[2].Value | ForEach-Object { $_.Value.FullName })
    if ($compiledMethod -ne $methodName -or ($compiledArguments -join '|') -ne ($argumentTypes -join '|')) {
        throw "$typeName target mismatch: $compiledMethod($($compiledArguments -join ', '))."
    }
}

Assert-HarmonyTarget 'Backpack.Patches.FishNetSetStoredInstanceReaderDiagnosticsPatch' `
    'RpcReader___Server_SetStoredInstance_2652194801' `
    @('Il2CppFishNet.Serializing.PooledReader', 'Il2CppFishNet.Transporting.Channel', 'Il2CppFishNet.Connection.NetworkConnection')
Assert-HarmonyTarget 'Backpack.Patches.FishNetSetItemSlotQuantityReaderDiagnosticsPatch' `
    'RpcReader___Server_SetItemSlotQuantity_1692629761' `
    @('Il2CppFishNet.Serializing.PooledReader', 'Il2CppFishNet.Transporting.Channel', 'Il2CppFishNet.Connection.NetworkConnection')
Assert-HarmonyTarget 'Backpack.Patches.FishNetStoredInstanceMutationDiagnosticsPatch' `
    'SetStoredInstance_Internal' `
    @('Il2CppFishNet.Connection.NetworkConnection', 'System.Int32', 'Il2CppScheduleOne.ItemFramework.ItemInstance')
Assert-HarmonyTarget 'Backpack.Patches.FishNetQuantityMutationDiagnosticsPatch' `
    'SetItemSlotQuantity_Internal' `
    @('System.Int32', 'System.Int32')

$playerManagerPatch = $assembly.GetType('Backpack.Patches.PlayerManagerPatch', $true)
$playerManagerMethods = @($playerManagerPatch.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Static,DeclaredOnly') |
    Where-Object { $_.Name -eq 'TryGetPlayerData' })
if ($playerManagerMethods.Count -ne 1) {
    throw "Expected the f12 explicit PlayerManager Harmony patch, found $($playerManagerMethods.Count)."
}
$tryGetAttribute = @($playerManagerMethods[0].GetCustomAttributesData() |
    Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' })
if ($tryGetAttribute.Count -ne 1 -or $tryGetAttribute[0].ConstructorArguments.Count -lt 3) {
    throw 'The diagnostic DLL lost the explicit TryGetPlayerData overload target.'
}
$tryGetName = [string]$tryGetAttribute[0].ConstructorArguments[0].Value
$tryGetTypes = @($tryGetAttribute[0].ConstructorArguments[1].Value | ForEach-Object { $_.Value.FullName })
$tryGetVariations = @($tryGetAttribute[0].ConstructorArguments[2].Value | ForEach-Object { [int]$_.Value })
if (
    $tryGetName -ne 'TryGetPlayerData' -or
    ($tryGetTypes -join '|') -ne 'System.String|System.Boolean|Il2CppScheduleOne.PlayerScripts.FullPlayerData' -or
    $tryGetVariations.Count -ne 3 -or
    $tryGetVariations[2] -ne 2
) {
    throw "Unexpected TryGetPlayerData target: $tryGetName($($tryGetTypes -join ', ')), variations=$($tryGetVariations -join ', ')."
}

$sha256 = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
$manifest = @(
    'DEVELOPMENT-ONLY READ-ONLY DIAGNOSTIC — DO NOT PUBLISH',
    "Melon identity: $melonName $melonVersion by $melonAuthor",
    "Assembly identity: $actualName $assemblyVersion",
    'Target: Schedule I 0.4.7f12 IL2CPP MAIN reference profile',
    "Reference Assembly-CSharp SHA-256: $actualAssemblyHash",
    "DLL: $artifact",
    "DLL SHA-256: $sha256",
    'Build behavior: FishNet server-reader and storage-setter logging only; no RPC blocking, argument changes, quantity changes, custom networking, or MPTEST1 persistence path',
    'Privacy: short PlayerCode hashes and session-local FishNet ClientIds only; no Steam IDs or item IDs/payloads',
    'Deployment: disabled; no game Mods folder was written',
    'Runtime verification: pending isolated host/guest test; missing reader markers cannot alone prove FishNet rejected an RPC before dispatch'
)
Set-Content -LiteralPath (Join-Path $runRoot 'manifest.txt') -Value $manifest

Write-Host 'FishNet authorization diagnostic build succeeded.'
Write-Host "DLL: $artifact"
Write-Host "SHA-256: $sha256"
Write-Host "Melon identity: $melonName $melonVersion"
Write-Host 'Verified server RPC reader targets and both native storage mutation targets.'
Write-Host "Build logs and manifest: $runRoot"
Write-Host 'No deployment or live runtime test was performed.'
