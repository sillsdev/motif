[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $FeedDirectory,

    [Parameter(Mandatory = $true)]
    [string] $InstallDirectory,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string] $ProductVersion,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string] $NextProductVersion,

    [Parameter(Mandatory = $true)]
    [string] $InitialPackagePath,

    [Parameter(Mandatory = $true)]
    [string] $WorkDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not [OperatingSystem]::IsWindows()) {
    throw 'The package smoke script currently exercises the Windows installer.'
}

$feed = [System.IO.Path]::GetFullPath($FeedDirectory)
$install = [System.IO.Path]::GetFullPath($InstallDirectory)
$initialPackage = [System.IO.Path]::GetFullPath($InitialPackagePath)
$work = [System.IO.Path]::GetFullPath($WorkDirectory)
if (-not (Test-Path -LiteralPath $initialPackage -PathType Leaf)) {
    throw "The version N installer is missing: $initialPackage"
}

$setupArguments = @('--silent', '--installto', $install)
$setupProcess = Start-Process -FilePath $initialPackage -ArgumentList $setupArguments -Wait -PassThru
if ($setupProcess.ExitCode -ne 0) {
    throw "Velopack setup failed with exit code $($setupProcess.ExitCode)."
}

$record = Get-ItemProperty -LiteralPath 'HKCU:\Software\SIL\Motif' -ErrorAction Stop
$cliPath = [System.IO.Path]::GetFullPath([string] $record.CliPath)
$installedRoot = [System.IO.Path]::GetFullPath([string] $record.InstallDir)
if (-not (Test-Path -LiteralPath $cliPath -PathType Leaf)) {
    throw "The install record points to a missing CLI: $cliPath"
}
if (-not (Test-Path -LiteralPath (Join-Path $installedRoot 'SIL.Motif.App.exe') -PathType Leaf)) {
    throw "The installed App is missing from $installedRoot."
}

$environmentKey = Get-ItemProperty -LiteralPath 'HKCU:\Environment' -ErrorAction SilentlyContinue
$userPathProperty = if ($null -eq $environmentKey) { $null } else { $environmentKey.PSObject.Properties['Path'] }
$userPath = if ($null -eq $userPathProperty) { $null } else { [string] $userPathProperty.Value }
$pathEntries = @($userPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$hasInstallPath = $false
foreach ($pathEntry in $pathEntries) {
    $normalizedEntry = [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($pathEntry))
    if ([string]::Equals($normalizedEntry, $installedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        $hasInstallPath = $true
        break
    }
}
if (-not $hasInstallPath) {
    throw "The Motif install directory was not added to the per-user PATH: $installedRoot"
}
$env:Path = $installedRoot + [System.IO.Path]::PathSeparator + $env:Path
$pathCommand = Get-Command motif.exe -ErrorAction Stop
if (-not [string]::Equals([System.IO.Path]::GetFullPath($pathCommand.Source), $cliPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "PATH resolved motif.exe to $($pathCommand.Source), not the registered CLI $cliPath."
}
$versionOutput = & motif.exe --version
if ($LASTEXITCODE -ne 0 -or [string]::Join('', $versionOutput).Trim() -ne $ProductVersion) {
    throw "The installed CLI did not report version $ProductVersion."
}

$env:MOTIF_WORKER_ROOT = Join-Path $work 'worker'
$env:MOTIF_WRITING_SYSTEM_REPOSITORY_PATH = Join-Path $work 'writing-systems'
New-Item -ItemType Directory -Path $env:MOTIF_WORKER_ROOT -Force | Out-Null
New-Item -ItemType Directory -Path $env:MOTIF_WRITING_SYSTEM_REPOSITORY_PATH -Force | Out-Null

$repoRoot = Split-Path $PSScriptRoot -Parent
$sampleBuilder = Join-Path $repoRoot 'bin/Release/SIL.Motif.SampleProjects.exe'
$sampleSpec = Join-Path $repoRoot 'samples/synthetic-turkic/sample.json'
$sampleOutputRoot = Join-Path $work 'sample-projects'
$sampleBuildOutput = & $sampleBuilder build $sampleSpec $sampleOutputRoot 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Sample project builder failed with exit code ${LASTEXITCODE}: $($sampleBuildOutput -join [Environment]::NewLine)"
}
$sampleBuild = [string]::Join([Environment]::NewLine, [string[]] $sampleBuildOutput) | ConvertFrom-Json
$projectPath = [System.IO.Path]::GetFullPath([string] $sampleBuild.projectPath)
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Sample project builder reported a missing project: $projectPath"
}

$readOutput = & $cliPath analyses --project $projectPath
if ($LASTEXITCODE -ne 0) {
    throw "The installed CLI could not read the sample project: $($readOutput -join [Environment]::NewLine)"
}

$jobId = (& $cliPath baseline-refresh --project $projectPath | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($jobId)) {
    throw "The installed CLI did not queue a Worker job: $jobId"
}
$jobStatus = 'queued'
for ($attempt = 0; $attempt -lt 120; $attempt++) {
    $jobJson = & $cliPath jobs show $jobId --project $projectPath --json
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read the installed Worker job $jobId."
    }
    $job = [string]::Join([Environment]::NewLine, $jobJson) | ConvertFrom-Json
    $jobStatus = [string] $job.status
    if ($jobStatus -eq 'completed') { break }
    if ($jobStatus -in @('failed', 'cancelled')) {
        throw "Installed Worker job $jobId ended as $jobStatus."
    }
    Start-Sleep -Seconds 1
}
if ($jobStatus -ne 'completed') {
    throw "Installed Worker job $jobId did not complete; last status was $jobStatus."
}

$wordsPath = Join-Path $work 'words.txt'
[System.IO.File]::WriteAllText($wordsPath, "k$([Environment]::NewLine)")
$assessmentOutput = & $cliPath assess $projectPath --words $wordsPath
if ($LASTEXITCODE -ne 0) {
    throw "The installed CLI could not run PanGloss: $($assessmentOutput -join [Environment]::NewLine)"
}

$appExecutable = Join-Path $installedRoot 'SIL.Motif.App.exe'
& $appExecutable --smoke
if ($LASTEXITCODE -ne 0) {
    throw "The installed App smoke exited with code $LASTEXITCODE."
}

$userDataDirectory = Join-Path $env:LOCALAPPDATA 'SIL/Motif'
New-Item -ItemType Directory -Path $userDataDirectory -Force | Out-Null
$userDataMarker = Join-Path $userDataDirectory ('package-smoke-' + [Guid]::NewGuid().ToString('N') + '.txt')
[System.IO.File]::WriteAllText($userDataMarker, 'keep')

& $cliPath --update-smoke $feed 'win-x64' $NextProductVersion
if ($LASTEXITCODE -ne 0) {
    throw "The installed update smoke exited with code $LASTEXITCODE."
}
$updated = $false
for ($attempt = 0; $attempt -lt 120; $attempt++) {
    $updatedVersion = (& $cliPath --version | Out-String).Trim()
    if ($LASTEXITCODE -eq 0 -and $updatedVersion -eq $NextProductVersion) {
        $updated = $true
        break
    }
    Start-Sleep -Seconds 1
}
if (-not $updated) {
    throw "Motif did not update from $ProductVersion to $NextProductVersion."
}

$updateExecutables = @(Get-ChildItem -LiteralPath $install -Filter 'Update.exe' -File -Recurse)
if ($updateExecutables.Count -eq 0) {
    throw "Velopack's uninstaller is missing from $install."
}

function Get-MotifDiscoveryRegistryState {
    $motifKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\SIL\Motif')
    if ($null -eq $motifKey) { return 'absent' }
    try {
        $values = @($motifKey.GetValueNames() | ForEach-Object {
            "$_=$($motifKey.GetValue($_, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames))"
        })
        $subKeys = @($motifKey.GetSubKeyNames())
        return "present; values=[$($values -join '; ')]; subkeys=[$($subKeys -join '; ')]"
    }
    finally {
        $motifKey.Dispose()
    }
}

$updateExecutable = $updateExecutables[0].FullName
$uninstallTracePath = Join-Path $work 'uninstall-hook.log'
Remove-Item -LiteralPath $uninstallTracePath -Force -ErrorAction SilentlyContinue
$env:MOTIF_PACKAGE_UNINSTALL_TRACE = $uninstallTracePath
$uninstallOutput = & $updateExecutable uninstall --silent 2>&1
$uninstallExitCode = $LASTEXITCODE
$uninstallOutputText = [string]::Join([Environment]::NewLine, [string[]] $uninstallOutput)
Remove-Item Env:MOTIF_PACKAGE_UNINSTALL_TRACE -ErrorAction SilentlyContinue
$uninstallTrace = if (Test-Path -LiteralPath $uninstallTracePath -PathType Leaf) {
    [System.IO.File]::ReadAllText($uninstallTracePath).Trim()
} else {
    '<no callback trace>'
}
$registryState = Get-MotifDiscoveryRegistryState
Write-Host "Velopack uninstaller output: $uninstallOutputText"
Write-Host "Velopack uninstall hook trace: $uninstallTrace"
Write-Host "Motif discovery registry state after uninstall: $registryState"
if ($uninstallExitCode -ne 0) {
    throw "Velopack uninstall failed with exit code $uninstallExitCode; output: $uninstallOutputText; hook trace: $uninstallTrace"
}
if (-not $uninstallTrace.Contains('callback completed:', [System.StringComparison]::Ordinal)) {
    throw "Velopack did not complete Motif's uninstall callback; hook trace: $uninstallTrace; registry: $registryState"
}
if ($registryState -ne 'absent') {
    throw "The Motif discovery key remains after uninstall; hook trace: $uninstallTrace; registry: $registryState"
}
$environmentKey = Get-ItemProperty -LiteralPath 'HKCU:\Environment' -ErrorAction SilentlyContinue
$remainingPathProperty = if ($null -eq $environmentKey) { $null } else { $environmentKey.PSObject.Properties['Path'] }
$remainingPath = if ($null -eq $remainingPathProperty) { $null } else { [string] $remainingPathProperty.Value }
$stillRegistered = $false
foreach ($pathEntry in @($remainingPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
    $normalizedEntry = [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($pathEntry))
    if ([string]::Equals($normalizedEntry, $installedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        $stillRegistered = $true
        break
    }
}
if ($stillRegistered) {
    throw 'The Motif install directory remains in the per-user PATH after uninstall.'
}
if (Test-Path -LiteralPath $install) {
    throw "The installed application directory remains after uninstall: $install"
}
if (-not (Test-Path -LiteralPath $userDataMarker -PathType Leaf)) {
    throw 'Motif user data did not survive uninstall.'
}
Remove-Item -LiteralPath $userDataMarker -Force

Write-Host "Windows package smoke passed for $ProductVersion to $NextProductVersion." -ForegroundColor Green
