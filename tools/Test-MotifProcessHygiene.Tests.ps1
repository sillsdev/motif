param(
    [switch] $LockOwner,
    [switch] $AncestorProbe,
    [string] $RepoRoot,
    [string] $ReadyFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'MotifProcessHygiene.psm1') -Force

if ($LockOwner) {
    $lock = Enter-MotifWorktreeLock -RepoRoot $RepoRoot -Context 'lock owner test'
    [IO.File]::WriteAllText($ReadyFile, 'ready')
    [System.Threading.Thread]::Sleep([System.Threading.Timeout]::Infinite)
    exit 0
}

if ($AncestorProbe) {
    $lock = Enter-MotifWorktreeLock -RepoRoot $RepoRoot -Context 'ancestor probe'
    Stop-MotifStaleProcesses -RepoRoot $RepoRoot -LockHandle $lock -AdditionalProcessNames @('pwsh')
    exit 0
}

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Test-ProcessAlive {
    param([int] $ProcessId)
    try {
        $process = [Diagnostics.Process]::GetProcessById($ProcessId)
        try { return -not $process.HasExited } finally { $process.Dispose() }
    }
    catch [ArgumentException] { return $false }
}

function Start-TestProcess {
    param([string] $WorkingDirectory, [string] $ReadyPath)
    $startInfo = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.ArgumentList.Add('-NoProfile')
    $startInfo.ArgumentList.Add('-Command')
    $startInfo.ArgumentList.Add("[IO.File]::WriteAllText('$ReadyPath','ready'); Start-Sleep -Seconds 600")
    [void] $startInfo.Environment.Remove('MOTIF_WORKTREE_LOCK_HELD')
    return [Diagnostics.Process]::Start($startInfo)
}

function Wait-ForFile {
    param([string] $Path, [Diagnostics.Process] $Process)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        if ($Process.HasExited) { throw "Test process exited with code $($Process.ExitCode) before it was ready." }
        if ($timer.Elapsed.TotalSeconds -ge 60) { throw "Test process did not become ready: $Path" }
        Start-Sleep -Milliseconds 50
    }
}

$rootLock = Enter-MotifWorktreeLock -RepoRoot $scriptRepoRoot -Context 'process hygiene tests'
$savedPolicy = @{}
foreach ($name in @('MSBUILDDISABLENODEREUSE', 'UseSharedCompilation', 'DOTNET_CLI_USE_MSBUILD_SERVER')) {
    $savedPolicy[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, $null)
}

$scenarioRoot = Join-Path $scriptRepoRoot "bin/.cache/process-hygiene-tests/$([guid]::NewGuid().ToString('N'))"
$lockRoot = Join-Path $scenarioRoot 'lock-worktree'
$sweepRoot = Join-Path $scenarioRoot 'motif'
$insideRoot = Join-Path $sweepRoot 'bin/.cache/inside'
$worktreeSibling = Join-Path $scenarioRoot 'motif.worktrees/x'
$hyphenSibling = Join-Path $scenarioRoot 'motif-2'
$ownerReady = Join-Path $scenarioRoot 'owner.ready'
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$ownerProcess = $null
$ancestorProcess = $null

try {
    New-Item -ItemType Directory -Force -Path $lockRoot, $insideRoot, $worktreeSibling, $hyphenSibling | Out-Null

    Set-MotifBuildProcessPolicy
    Assert-True ($env:MSBUILDDISABLENODEREUSE -eq '1') 'The MSBuild node-reuse default was not set.'
    Assert-True ($env:UseSharedCompilation -eq 'false') 'The shared-compilation default was not set.'
    Assert-True ($env:DOTNET_CLI_USE_MSBUILD_SERVER -eq '0') 'The MSBuild server default was not set.'
    $env:MSBUILDDISABLENODEREUSE = 'caller-value'
    Set-MotifBuildProcessPolicy
    Assert-True ($env:MSBUILDDISABLENODEREUSE -eq 'caller-value') 'A caller-selected process policy was replaced.'

    $ownerStart = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
    $ownerStart.UseShellExecute = $false
    $ownerStart.CreateNoWindow = $true
    $ownerStart.WorkingDirectory = $scriptRepoRoot
    foreach ($argument in @('-NoProfile', '-File', $PSCommandPath, '-LockOwner', '-RepoRoot', $lockRoot,
            '-ReadyFile', $ownerReady)) { $ownerStart.ArgumentList.Add($argument) }
    [void] $ownerStart.Environment.Remove('MOTIF_WORKTREE_LOCK_HELD')
    $ownerProcess = [Diagnostics.Process]::Start($ownerStart)
    Wait-ForFile -Path $ownerReady -Process $ownerProcess

    $ownerData = Get-Content -LiteralPath (Join-Path $lockRoot 'bin/.cache/run.lock.json') -Raw | ConvertFrom-Json
    Assert-True ($ownerData.pid -eq $ownerProcess.Id) 'The lock owner PID was not recorded.'
    Assert-True ($null -ne $ownerData.startTimeUtc) 'The lock owner start time was not recorded.'
    Assert-True ($ownerData.context -eq 'lock owner test') 'The lock owner context was not recorded.'

    $refusal = $null
    try { $null = Enter-MotifWorktreeLock -RepoRoot $lockRoot -Context 'second lock test' }
    catch { $refusal = $_.Exception.Message }
    Assert-True ($refusal -and $refusal.Contains([string] $ownerProcess.Id) -and $refusal.Contains('lock owner test')) `
        'A second run was not refused with the current lock owner.'

    $ownerProcess.Kill()
    [void] $ownerProcess.WaitForExit(10000)
    Assert-True ($ownerProcess.HasExited) 'The test lock owner did not exit.'
    $afterOwnerExit = Enter-MotifWorktreeLock -RepoRoot $lockRoot -Context 'lock recovery test'
    Exit-MotifWorktreeLock -LockHandle $afterOwnerExit

    $insideReady = Join-Path $insideRoot 'ready'
    $worktreeReady = Join-Path $worktreeSibling 'ready'
    $hyphenReady = Join-Path $hyphenSibling 'ready'
    $processes.Add((Start-TestProcess -WorkingDirectory $insideRoot -ReadyPath $insideReady))
    $processes.Add((Start-TestProcess -WorkingDirectory $worktreeSibling -ReadyPath $worktreeReady))
    $processes.Add((Start-TestProcess -WorkingDirectory $hyphenSibling -ReadyPath $hyphenReady))
    for ($index = 0; $index -lt $processes.Count; $index++) {
        Wait-ForFile -Path @($insideReady, $worktreeReady, $hyphenReady)[$index] -Process $processes[$index]
    }

    $sweepLock = Enter-MotifWorktreeLock -RepoRoot $sweepRoot -Context 'process sweep test'
    try {
        Stop-MotifStaleProcesses -RepoRoot $sweepRoot -LockHandle $sweepLock -AdditionalProcessNames @('pwsh')
    }
    finally { Exit-MotifWorktreeLock -LockHandle $sweepLock }

    Assert-True (-not (Test-ProcessAlive $processes[0].Id)) 'A process inside the worktree survived the sweep.'
    Assert-True (Test-ProcessAlive $processes[1].Id) 'A process in the .worktrees sibling was swept.'
    Assert-True (Test-ProcessAlive $processes[2].Id) 'A process in the hyphen sibling was swept.'

    $ancestorMarker = Join-Path $scenarioRoot 'ancestor-survived'
    $ancestorStart = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
    $ancestorStart.UseShellExecute = $false
    $ancestorStart.CreateNoWindow = $true
    $ancestorStart.WorkingDirectory = $scriptRepoRoot
    foreach ($argument in @('-NoProfile', '-File', $PSCommandPath, '-AncestorProbe', '-RepoRoot', $scriptRepoRoot)) {
        $ancestorStart.ArgumentList.Add($argument)
    }
    $ancestorStart.RedirectStandardOutput = $true
    $ancestorStart.RedirectStandardError = $true
    $ancestorProcess = [Diagnostics.Process]::Start($ancestorStart)
    if (-not $ancestorProcess.WaitForExit(30000)) {
        $ancestorProcess.Kill()
        throw 'The ancestor process probe did not finish.'
    }
    $probeOutput = $ancestorProcess.StandardOutput.ReadToEnd() + $ancestorProcess.StandardError.ReadToEnd()
    Assert-True ($ancestorProcess.ExitCode -eq 0) "The ancestor process probe failed: $probeOutput"
    [IO.File]::WriteAllText($ancestorMarker, 'parent alive')
    Assert-True (Test-ProcessAlive $PID) 'The current process did not survive the process sweep.'
    Assert-True (Test-Path -LiteralPath $ancestorMarker) 'The ancestor process did not survive the process sweep.'

    Write-Host 'Process hygiene checks passed.' -ForegroundColor Green
}
finally {
    foreach ($process in $processes) {
        try {
            if (-not $process.HasExited) { $process.Kill(); [void] $process.WaitForExit(10000) }
        }
        catch { }
        $process.Dispose()
    }
    if ($ownerProcess) {
        try {
            if (-not $ownerProcess.HasExited) { $ownerProcess.Kill(); [void] $ownerProcess.WaitForExit(10000) }
        }
        catch { }
        $ownerProcess.Dispose()
    }
    if ($ancestorProcess) { $ancestorProcess.Dispose() }
    foreach ($name in $savedPolicy.Keys) { [Environment]::SetEnvironmentVariable($name, $savedPolicy[$name]) }
    try { Remove-Item -LiteralPath $scenarioRoot -Recurse -Force -ErrorAction SilentlyContinue }
    catch { }
    if ($rootLock.IsOwner) { Exit-MotifWorktreeLock -LockHandle $rootLock }
}
