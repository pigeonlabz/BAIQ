$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectFile = Join-Path $projectRoot 'src\InventoryExpanded\InventoryExpanded.csproj'
$projectDirectory = Split-Path -Parent $projectFile
$assetsFile = Join-Path $projectDirectory 'obj\project.assets.json'
$referenceRoot = (Resolve-Path (Join-Path $projectRoot '..\dev-bootstrap\refs\MAIN_047f12')).Path
$assemblyReference = Join-Path $referenceRoot 'MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll'
$expectedAssemblyHash = 'F4F7CD358408CACE6267380B921DABF79946EBD688840A229F081751ED73518D'

if (-not (Test-Path -LiteralPath $projectFile)) {
    throw "Project file was not found: $projectFile"
}
if (-not (Test-Path -LiteralPath $assemblyReference)) {
    throw "Pinned Schedule I reference assembly was not found: $assemblyReference"
}

$actualAssemblyHash = (Get-FileHash -LiteralPath $assemblyReference -Algorithm SHA256).Hash
if ($actualAssemblyHash -ne $expectedAssemblyHash) {
    throw "Pinned Assembly-CSharp.dll hash mismatch. Expected $expectedAssemblyHash, found $actualAssemblyHash."
}

$runId = '{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
$runRoot = Join-Path $projectRoot (Join-Path 'test-output\ui-workspace' $runId)
$baseOutputPath = Join-Path $runRoot 'output'
$dotnetCliHome = Join-Path $projectRoot '.dotnet-cli-home'
New-Item -ItemType Directory -Path $baseOutputPath -Force | Out-Null
New-Item -ItemType Directory -Path $dotnetCliHome -Force | Out-Null

$dotnetCommand = Get-Command 'dotnet' -ErrorAction Stop
$previousDotnetCliHome = $env:DOTNET_CLI_HOME
$previousPath = $env:PATH
$previousNugetPackages = $env:NUGET_PACKAGES
$previousDotnetFirstRun = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
$previousTelemetryOptOut = $env:DOTNET_CLI_TELEMETRY_OPTOUT
try {
    $env:DOTNET_CLI_HOME = $dotnetCliHome
    $env:PATH = (Join-Path $dotnetCliHome '.dotnet\tools') + ';' + $env:PATH
    if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
        $env:NUGET_PACKAGES = Join-Path $env:USERPROFILE '.nuget\packages'
    }
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

    $buildArguments = @(
        'build',
        $projectFile,
        '-c', 'IL2CPP',
        "-p:ExperimentalUIWorkspace=true",
        "-p:S1Dir=$referenceRoot",
        "-p:BaseOutputPath=$baseOutputPath\",
        '-p:DeployToGame=false'
    )
    if (Test-Path -LiteralPath $assetsFile) {
        Write-Output 'Using the existing NuGet restore assets; no package restore will be attempted.'
    }
    else {
        Write-Output 'No existing NuGet restore assets found; restoring packages from configured sources and cache.'
        & $dotnetCommand.Source restore $projectFile `
            '-p:Configuration=IL2CPP' `
            '--ignore-failed-sources' `
            '-p:ExperimentalUIWorkspace=true' `
            "-p:S1Dir=$referenceRoot" `
            '-p:NuGetAudit=false'
        if ($LASTEXITCODE -ne 0) {
            throw "Experimental UI Workspace package restore failed with exit code $LASTEXITCODE."
        }
    }

    $buildArguments += '--no-restore'
    & $dotnetCommand.Source @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Experimental UI Workspace build failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:DOTNET_CLI_HOME = $previousDotnetCliHome
    $env:PATH = $previousPath
    $env:NUGET_PACKAGES = $previousNugetPackages
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $previousDotnetFirstRun
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $previousTelemetryOptOut
}

$expectedDllName = 'BackpackInventoryQoL-Experimental-UIWorkspace.dll'
$dllMatches = @(Get-ChildItem -LiteralPath $baseOutputPath -Recurse -File -Filter $expectedDllName)
if ($dllMatches.Count -ne 1) {
    throw "Expected exactly one $expectedDllName under $baseOutputPath, found $($dllMatches.Count)."
}

$dllPath = $dllMatches[0].FullName
$dllHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
Write-Output "Build result: success"
Write-Output "Build directory: $runRoot"
Write-Output "DLL: $dllPath"
Write-Output "SHA-256: $dllHash"
