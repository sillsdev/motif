[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $FeedDirectory,

    [Parameter(Mandatory = $true)]
    [string] $InstallDirectory,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string] $ProductVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not [OperatingSystem]::IsWindows()) {
    throw 'The package smoke script currently exercises the Windows installer.'
}

$feed = [System.IO.Path]::GetFullPath($FeedDirectory)
$install = [System.IO.Path]::GetFullPath($InstallDirectory)
$setups = @(Get-ChildItem -LiteralPath $feed -File -Filter '*Setup.exe')
if ($setups.Count -ne 1) {
    throw "Expected one Setup.exe in $feed; found $($setups.Count)."
}

$setupArguments = @('--silent', '--installto', $install)
$setupProcess = Start-Process -FilePath $setups[0].FullName -ArgumentList $setupArguments -Wait -PassThru
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

$appExecutable = Join-Path $installedRoot 'SIL.Motif.App.exe'
& $appExecutable --smoke
if ($LASTEXITCODE -ne 0) {
    throw "The installed App smoke exited with code $LASTEXITCODE."
}

$userDataDirectory = Join-Path $env:LOCALAPPDATA 'SIL/Motif'
New-Item -ItemType Directory -Path $userDataDirectory -Force | Out-Null
$userDataMarker = Join-Path $userDataDirectory ('package-smoke-' + [Guid]::NewGuid().ToString('N') + '.txt')
[System.IO.File]::WriteAllText($userDataMarker, 'keep')

$updateExecutables = @(Get-ChildItem -LiteralPath $install -Filter 'Update.exe' -File -Recurse)
if ($updateExecutables.Count -eq 0) {
    throw "Velopack's uninstaller is missing from $install."
}
$updateExecutable = $updateExecutables[0].FullName
& $updateExecutable uninstall --silent
if ($LASTEXITCODE -ne 0) {
    throw "Velopack uninstall failed with exit code $LASTEXITCODE."
}

if (Test-Path -LiteralPath 'HKCU:\Software\SIL\Motif') {
    throw 'The Motif discovery key remains after uninstall.'
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

Write-Host "Windows package smoke passed for $ProductVersion." -ForegroundColor Green
