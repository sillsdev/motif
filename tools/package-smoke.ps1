[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $FeedDirectory,

    [Parameter(Mandatory = $true)]
    [string] $InstallDirectory,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string] $ProductVersion,

    [ValidatePattern('^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string] $NextProductVersion,

    [Parameter(Mandatory = $true)]
    [string] $InitialPackagePath,

    [Parameter(Mandatory = $true)]
    [string] $WorkDirectory,

    [switch] $SkipUpdate
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

function Read-SharedFileText {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $sharing = [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
    $stream = [System.IO.FileStream]::new($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, $sharing)
    $reader = [System.IO.StreamReader]::new($stream)
    try {
        return $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}

function Get-InstallProcesses {
    $installPrefix = $install.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    foreach ($process in Get-CimInstance -ClassName Win32_Process) {
        $executablePath = [string] $process.ExecutablePath
        if ([string]::IsNullOrWhiteSpace($executablePath)) { continue }
        $fullExecutablePath = [System.IO.Path]::GetFullPath($executablePath)
        if ([string]::Equals($fullExecutablePath, $install, [System.StringComparison]::OrdinalIgnoreCase) -or
            $fullExecutablePath.StartsWith($installPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            [pscustomobject]@{
                ProcessId = [int] $process.ProcessId
                Name = [string] $process.Name
                ExecutablePath = $fullExecutablePath
            }
        }
    }
}

function Wait-ForInstallProcesses {
    $maximumProcessLifetime = [TimeSpan]::FromMinutes(5)
    $maximumWait = [TimeSpan]::FromMinutes(10)
    $waitStarted = [DateTime]::UtcNow
    $firstObserved = @{}
    $quietPolls = 0
    while ([DateTime]::UtcNow - $waitStarted -lt $maximumWait) {
        $processes = @(Get-InstallProcesses)
        if ($processes.Count -eq 0) {
            $quietPolls++
            if ($quietPolls -ge 2) { return @() }
        }
        else {
            $quietPolls = 0
            $now = [DateTime]::UtcNow
            foreach ($process in $processes) {
                if (-not $firstObserved.ContainsKey($process.ProcessId)) {
                    $firstObserved[$process.ProcessId] = $now
                }
            }
            $expiredProcesses = @($processes | Where-Object {
                $now - $firstObserved[$_.ProcessId] -ge $maximumProcessLifetime
            })
            if ($expiredProcesses.Count -gt 0) { return $expiredProcesses }
        }
        Start-Sleep -Milliseconds 250
    }
    return @(Get-InstallProcesses)
}

$uninstallTracePath = Join-Path $work 'uninstall-hook.log'
$velopackUninstallLogPath = Join-Path $work 'velopack-uninstall.log'
Remove-Item -LiteralPath $uninstallTracePath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $velopackUninstallLogPath -Force -ErrorAction SilentlyContinue

function Get-VelopackLogCandidates {
    $paths = [System.Collections.Generic.List[string]]::new()
    [void] $paths.Add($velopackUninstallLogPath)
    $directories = @(
        @{ Path = Join-Path $env:LOCALAPPDATA 'velopack'; Recurse = $false },
        @{ Path = $env:TEMP; Recurse = $false },
        @{ Path = $install; Recurse = $true }
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Path) }
    foreach ($directory in $directories) {
        $search = @{ LiteralPath = $directory.Path; Filter = 'velopack*.log'; File = $true }
        if ($directory.Recurse) { $search.Recurse = $true }
        if (Test-Path -LiteralPath $directory.Path -PathType Container) {
            foreach ($candidate in Get-ChildItem @search) {
                [void] $paths.Add($candidate.FullName)
            }
        }
    }
    return @($paths | Select-Object -Unique)
}

function Write-VelopackLogs {
    foreach ($candidatePath in Get-VelopackLogCandidates) {
        Write-Host "Velopack log candidate: $candidatePath"
        if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
            Write-Host (Read-SharedFileText $candidatePath).Trim()
        }
        else {
            Write-Host '<not created>'
        }
    }
}

function Format-InstallProcesses {
    param([object[]] $Processes)

    if ($Processes.Count -eq 0) { return 'none' }
    return [string]::Join('; ', [string[]] @($Processes | ForEach-Object {
        "PID $($_.ProcessId) $($_.Name) at $($_.ExecutablePath)"
    }))
}

if (-not $SkipUpdate) {
    if ([string]::IsNullOrWhiteSpace($NextProductVersion)) {
        throw 'NextProductVersion is required unless SkipUpdate is selected.'
    }

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
        Write-VelopackLogs
        $installProcesses = Format-InstallProcesses @(Get-InstallProcesses)
        throw ("Motif did not update from $ProductVersion to $NextProductVersion; the CLI last reported " +
            "'$updatedVersion'. Velopack logs are above; processes running from the install: $installProcesses")
    }
}

$updateExecutable = Join-Path $install 'Update.exe'
if (-not (Test-Path -LiteralPath $updateExecutable -PathType Leaf)) {
    throw "Velopack's uninstaller is missing from $install."
}

$velopackLogsBeforeUninstall = @{}
foreach ($candidatePath in Get-VelopackLogCandidates) {
    if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
        $velopackLogsBeforeUninstall[$candidatePath] = Read-SharedFileText $candidatePath
    }
}
$env:MOTIF_PACKAGE_UNINSTALL_TRACE = $uninstallTracePath
$uninstallOutput = & $updateExecutable --verbose --log $velopackUninstallLogPath --rootDir $install uninstall --silent 2>&1
$uninstallExitCode = $LASTEXITCODE
$uninstallOutputText = @($uninstallOutput) -join [Environment]::NewLine
Remove-Item Env:MOTIF_PACKAGE_UNINSTALL_TRACE -ErrorAction SilentlyContinue
$remainingInstallProcesses = @(Wait-ForInstallProcesses)
$uninstallTrace = if (Test-Path -LiteralPath $uninstallTracePath -PathType Leaf) {
    (Read-SharedFileText $uninstallTracePath).Trim()
} else {
    '<no callback trace>'
}
$registryState = Get-MotifDiscoveryRegistryState
Write-Host "Velopack uninstall executable: $updateExecutable"
Write-Host "Velopack uninstaller output: $uninstallOutputText"
foreach ($candidatePath in (Get-VelopackLogCandidates)) {
    if (-not $velopackLogsBeforeUninstall.ContainsKey($candidatePath)) {
        $velopackLogsBeforeUninstall[$candidatePath] = $null
    }
}
foreach ($candidatePath in $velopackLogsBeforeUninstall.Keys | Sort-Object) {
    Write-Host "Velopack log candidate: $candidatePath"
    if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
        $logContents = (Read-SharedFileText $candidatePath).Trim()
    }
    elseif ($null -ne $velopackLogsBeforeUninstall[$candidatePath]) {
        $logContents = '[captured before uninstall; the source file was removed by uninstall]' +
            [Environment]::NewLine + $velopackLogsBeforeUninstall[$candidatePath].Trim()
    }
    else {
        $logContents = '<not created>'
    }
    Write-Host $logContents
}
Write-Host "Velopack uninstall hook trace: $uninstallTrace"
Write-Host "Motif discovery registry state after uninstall: $registryState"
if ($remainingInstallProcesses.Count -gt 0) {
    $processSummary = Format-InstallProcesses $remainingInstallProcesses
    throw "Velopack processes remained after the uninstall wait; verbose logs and hook trace are above: $processSummary"
}
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
# Update.exe cannot delete its own folder, so it leaves a delayed cmd rmdir behind as it exits.
$installRemovalDeadline = [DateTime]::UtcNow.AddMinutes(2)
while ((Test-Path -LiteralPath $install) -and [DateTime]::UtcNow -lt $installRemovalDeadline) {
    Start-Sleep -Milliseconds 500
}
if (Test-Path -LiteralPath $install) {
    throw "The installed application directory remains after uninstall: $install"
}
if (-not (Test-Path -LiteralPath $userDataMarker -PathType Leaf)) {
    throw 'Motif user data did not survive uninstall.'
}
Remove-Item -LiteralPath $userDataMarker -Force

$versionSummary = if ($SkipUpdate) { "at $ProductVersion" } else { "from $ProductVersion to $NextProductVersion" }
Write-Host "Windows package smoke passed $versionSummary." -ForegroundColor Green
