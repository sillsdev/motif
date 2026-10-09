Set-StrictMode -Version Latest
$script:WindowsProcessSnapshot = $null

function Set-MotifBuildProcessPolicy {
    foreach ($setting in @{
            MSBUILDDISABLENODEREUSE = '1'
            UseSharedCompilation = 'false'
            DOTNET_CLI_USE_MSBUILD_SERVER = '0'
        }.GetEnumerator()) {
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($setting.Key))) {
            [Environment]::SetEnvironmentVariable($setting.Key, $setting.Value)
        }
    }
}

function Get-MotifNormalizedRoot {
    param([Parameter(Mandatory)][string] $RepoRoot)
    if ([string]::IsNullOrWhiteSpace($RepoRoot)) { throw 'RepoRoot cannot be empty.' }
    [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RepoRoot))
}

function Get-MotifStartTimeUtc {
    param([Parameter(Mandatory)][int] $ProcessId)
    $process = [Diagnostics.Process]::GetProcessById($ProcessId)
    try { return $process.StartTime.ToUniversalTime() }
    finally { $process.Dispose() }
}

function Get-MotifWindowsProcessEntry {
    param([Parameter(Mandatory)][int] $ProcessId)
    if ($null -ne $script:WindowsProcessSnapshot) { return $script:WindowsProcessSnapshot[$ProcessId] }
    return Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction Stop
}

function Get-MotifProcessStartToken {
    param([Parameter(Mandatory)][int] $ProcessId)
    if ($IsWindows) {
        try {
            $entry = Get-MotifWindowsProcessEntry -ProcessId $ProcessId
            if ($entry.CreationDate) {
                return "windows:$(([DateTime] $entry.CreationDate).ToUniversalTime().Ticks)"
            }
        }
        catch { }
    }
    if ($IsLinux) {
        try {
            $stat = [IO.File]::ReadAllText("/proc/$ProcessId/stat")
            $close = $stat.LastIndexOf(')')
            if ($close -ge 0) {
                $fields = $stat.Substring($close + 1).Trim().Split([char[]]@(' ', "`t"), [StringSplitOptions]::RemoveEmptyEntries)
                if ($fields.Count -gt 19) { return "linux:$($fields[19])" }
            }
        }
        catch { }
    }
    if ($IsMacOS) {
        try {
            $output = & ps -p $ProcessId -o lstart= 2>$null
            $started = (@($output) -join ' ').Trim()
            if ($started) { return "macos:$started" }
        }
        catch { }
    }
    return "time:$((Get-MotifStartTimeUtc -ProcessId $ProcessId).Ticks)"
}

function Test-MotifProcessStartMatches {
    param(
        [Parameter(Mandatory)][int] $ProcessId,
        [Parameter(Mandatory)][string] $RecordedToken
    )
    try {
        $currentToken = Get-MotifProcessStartToken -ProcessId $ProcessId
        return [string]::Equals($currentToken, $RecordedToken, [StringComparison]::Ordinal)
    }
    catch { return $false }
}

function Get-MotifParentProcessId {
    param([Parameter(Mandatory)][int] $ProcessId)

    if ($IsWindows) {
        try {
            $entry = Get-MotifWindowsProcessEntry -ProcessId $ProcessId
            if ($entry) { return [int] $entry.ParentProcessId }
        }
        catch { return $null }
        return $null
    }

    if ($IsLinux) {
        try {
            $stat = [IO.File]::ReadAllText("/proc/$ProcessId/stat")
            $close = $stat.LastIndexOf(')')
            if ($close -lt 0) { return $null }
            $fields = $stat.Substring($close + 1).Trim().Split([char[]]@(' ', "`t"), [StringSplitOptions]::RemoveEmptyEntries)
            if ($fields.Count -lt 2) { return $null }
            return [int] $fields[1]
        }
        catch { return $null }
    }

    try {
        $output = & ps -p $ProcessId -o ppid= 2>$null
        $parent = 0
        if ([int]::TryParse((@($output) -join '').Trim(), [ref] $parent)) { return $parent }
    }
    catch { }
    return $null
}

function Test-MotifLockOwnerIdentity {
    param(
        [Parameter(Mandatory)] $Owner,
        [Parameter(Mandatory)][string] $RepoRoot
    )

    if (-not $Owner.pid -or -not $Owner.startTimeUtc -or -not $Owner.repoRoot) { return $false }
    $ownerRoot = Get-MotifNormalizedRoot ([string] $Owner.repoRoot)
    $expectedRoot = Get-MotifNormalizedRoot $RepoRoot
    $comparison = if ($IsWindows -or $IsMacOS) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not [string]::Equals($ownerRoot, $expectedRoot, $comparison)) { return $false }

    try {
        return Test-MotifProcessStartMatches -ProcessId ([int] $Owner.pid) `
            -RecordedToken ([string] $Owner.processStartToken)
    }
    catch { return $false }
}

function Enter-MotifWorktreeLock {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $RepoRoot,
        [Parameter(Mandatory)][string] $Context
    )

    $normalizedRoot = Get-MotifNormalizedRoot $RepoRoot
    $cacheRoot = Join-Path $normalizedRoot 'bin/.cache'
    [IO.Directory]::CreateDirectory($cacheRoot) | Out-Null
    $lockPath = Join-Path $cacheRoot 'run.lock'
    $ownerPath = Join-Path $cacheRoot 'run.lock.json'

    $handoffPid = 0
    if ([int]::TryParse([string] $env:MOTIF_WORKTREE_LOCK_HELD, [ref] $handoffPid) -and $handoffPid -gt 0) {
        $owner = $null
        if (Test-Path -LiteralPath $ownerPath -PathType Leaf) {
            try { $owner = Get-Content -LiteralPath $ownerPath -Raw | ConvertFrom-Json }
            catch { $owner = $null }
        }
        if ($owner -and [int] $owner.pid -eq $handoffPid -and
            (Test-MotifLockOwnerIdentity -Owner $owner -RepoRoot $normalizedRoot)) {
            $isInOwnerTree = $handoffPid -eq $PID -or
                (Get-MotifAncestorProcessIds -ProcessId $PID).Contains($handoffPid)
            if ($isInOwnerTree) {
                return [pscustomobject]@{
                    Stream = $null
                    LockPath = $lockPath
                    OwnerPath = $ownerPath
                    RepoRoot = $normalizedRoot
                    OwnerPid = $handoffPid
                    OwnerStartTimeUtcTicks = [long] $owner.startTimeUtcTicks
                    IsOwner = $false
                    IsDelegated = $true
                    IsHeld = $true
                }
            }
        }
    }

    $stream = $null
    try {
        $stream = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    }
    catch [IO.IOException] {
        $owner = $null
        if (Test-Path -LiteralPath $ownerPath -PathType Leaf) {
            try { $owner = Get-Content -LiteralPath $ownerPath -Raw | ConvertFrom-Json }
            catch { $owner = $null }
        }
        if ($owner) {
            throw "$Context is already running in this worktree (owner PID $($owner.pid), context '$($owner.context)'). Run one build or test workflow at a time per worktree."
        }
        throw "$Context is already running in this worktree. Run one build or test workflow at a time per worktree."
    }

    try {
        $startTime = Get-MotifStartTimeUtc -ProcessId $PID
        $startToken = Get-MotifProcessStartToken -ProcessId $PID
        $metadata = [pscustomobject]@{
            pid = $PID
            startTimeUtc = $startTime.ToString('o')
            startTimeUtcTicks = $startTime.Ticks
            processStartToken = $startToken
            context = $Context
            repoRoot = $normalizedRoot
        }
        [IO.File]::WriteAllText($ownerPath, ($metadata | ConvertTo-Json -Compress))
        $previousHandoff = [Environment]::GetEnvironmentVariable('MOTIF_WORKTREE_LOCK_HELD')
        $env:MOTIF_WORKTREE_LOCK_HELD = [string] $PID
        return [pscustomobject]@{
            Stream = $stream
            LockPath = $lockPath
            OwnerPath = $ownerPath
            RepoRoot = $normalizedRoot
            OwnerPid = $PID
            OwnerStartTimeUtcTicks = $startTime.Ticks
            OwnerProcessStartToken = $startToken
            PreviousHandoff = $previousHandoff
            IsOwner = $true
            IsDelegated = $false
            IsHeld = $true
        }
    }
    catch {
        if ($stream) { $stream.Dispose() }
        throw
    }
}

function Exit-MotifWorktreeLock {
    [CmdletBinding()]
    param([Parameter(Mandatory)] $LockHandle)

    if (-not $LockHandle.IsOwner) { return }
    try {
        $owner = $null
        if (Test-Path -LiteralPath $LockHandle.OwnerPath -PathType Leaf) {
            try { $owner = Get-Content -LiteralPath $LockHandle.OwnerPath -Raw | ConvertFrom-Json }
            catch { $owner = $null }
        }
        if ($owner -and [int] $owner.pid -eq $PID -and
            [string] $owner.processStartToken -eq [string] $LockHandle.OwnerProcessStartToken) {
            Remove-Item -LiteralPath $LockHandle.OwnerPath -Force -ErrorAction SilentlyContinue
        }
    }
    finally {
        if ($LockHandle.Stream) { $LockHandle.Stream.Dispose() }
        if ([string] $env:MOTIF_WORKTREE_LOCK_HELD -eq [string] $PID) {
            [Environment]::SetEnvironmentVariable('MOTIF_WORKTREE_LOCK_HELD', $LockHandle.PreviousHandoff)
        }
        $LockHandle.IsHeld = $false
    }
}

function Test-MotifLockHeld {
    param([Parameter(Mandatory)] $LockHandle, [Parameter(Mandatory)][string] $RepoRoot)
    $comparison = if ($IsWindows -or $IsMacOS) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not [string]::Equals((Get-MotifNormalizedRoot $LockHandle.RepoRoot),
            (Get-MotifNormalizedRoot $RepoRoot), $comparison)) { return $false }
    if (-not $LockHandle.IsHeld) { return $false }
    if ($LockHandle.IsOwner) { return $LockHandle.Stream -and $LockHandle.Stream.CanRead }
    if (-not $LockHandle.IsDelegated) { return $false }

    $owner = $null
    if (Test-Path -LiteralPath $LockHandle.OwnerPath -PathType Leaf) {
        try { $owner = Get-Content -LiteralPath $LockHandle.OwnerPath -Raw | ConvertFrom-Json }
        catch { $owner = $null }
    }
    if (-not $owner -or [int] $owner.pid -ne [int] $LockHandle.OwnerPid -or
        -not (Test-MotifLockOwnerIdentity -Owner $owner -RepoRoot $LockHandle.RepoRoot)) { return $false }
    if ($PID -eq [int] $LockHandle.OwnerPid) { return $true }
    return [int] $LockHandle.OwnerPid -in (Get-MotifAncestorProcessIds -ProcessId $PID)
}

function Get-MotifProcessInformation {
    param([Parameter(Mandatory)][int] $ProcessId)

    $process = $null
    try { $process = [Diagnostics.Process]::GetProcessById($ProcessId) }
    catch { return $null }
    try {
        $name = $process.ProcessName
        $started = $process.StartTime.ToUniversalTime()
        $startToken = Get-MotifProcessStartToken -ProcessId $ProcessId
        $parent = Get-MotifParentProcessId -ProcessId $ProcessId
        $commandLine = ''
        $paths = [Collections.Generic.List[string]]::new()

        if ($IsWindows) {
            try {
                $entry = Get-MotifWindowsProcessEntry -ProcessId $ProcessId
                if ($entry.CommandLine) { $commandLine = [string] $entry.CommandLine }
                if ($entry.ExecutablePath) { $paths.Add([string] $entry.ExecutablePath) }
            }
            catch { }
        }
        elseif ($IsLinux) {
            try {
                $commandLine = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes("/proc/$ProcessId/cmdline")).Replace([char] 0, ' ')
            }
            catch { }
            foreach ($link in @("/proc/$ProcessId/cwd", "/proc/$ProcessId/exe")) {
                try {
                    $item = Get-Item -LiteralPath $link -Force -ErrorAction Stop
                    $target = $item.ResolveLinkTarget($true)
                    if ($target) { $paths.Add($target.FullName) }
                }
                catch { }
            }
        }
        else {
            try {
                $output = & ps -p $ProcessId -o command= 2>$null
                $commandLine = @($output) -join ' '
            }
            catch { }
            try { if ($process.MainModule.FileName) { $paths.Add([string] $process.MainModule.FileName) } }
            catch { }
            try {
                $output = & lsof -a -p $ProcessId -d cwd -Fn 2>$null
                foreach ($line in @($output)) {
                    if ([string] $line -match '^n(.+)$') { $paths.Add($Matches[1]) }
                }
            }
            catch { }
        }

        return [pscustomobject]@{
            Id = $ProcessId
            Name = $name
            StartTimeUtc = $started
            StartToken = $startToken
            ParentProcessId = $parent
            CommandLine = $commandLine
            Paths = @($paths | Select-Object -Unique)
        }
    }
    catch { return $null }
    finally { $process.Dispose() }
}

function Get-MotifAncestorProcessIds {
    param([Parameter(Mandatory)][int] $ProcessId)
    $ids = [Collections.Generic.HashSet[int]]::new()
    $visited = [Collections.Generic.HashSet[int]]::new()
    [void] $visited.Add($ProcessId)
    $cursor = $ProcessId
    for ($depth = 0; $depth -lt 128; $depth++) {
        $parent = Get-MotifParentProcessId -ProcessId $cursor
        if (-not $parent -or $parent -le 0 -or -not $visited.Add([int] $parent)) { break }
        [void] $ids.Add([int] $parent)
        $cursor = [int] $parent
    }
    return ,$ids
}

function Test-MotifIdeProcess {
    param([Parameter(Mandatory)][string] $Name)
    return $Name -match '^(devenv|rider(?:64)?|code(?:-insiders)?|codium|cursor|idea(?:64)?|webstorm64|clion64|pycharm64|omnisharp)(?:$|[._-])|^(ServiceHub|JetBrains)'
}

function Test-MotifProcessHasIdeAncestor {
    param([Parameter(Mandatory)] $Information)
    $cursor = [int] $Information.ParentProcessId
    $childStarted = $Information.StartTimeUtc
    $visited = [Collections.Generic.HashSet[int]]::new()
    for ($depth = 0; $depth -lt 128 -and $cursor -gt 0; $depth++) {
        if (-not $visited.Add($cursor)) { return $true }
        $parent = Get-MotifProcessInformation -ProcessId $cursor
        # Windows keeps an exited parent's PID; a missing one, or one reused by a later process, ends the chain.
        if (-not $parent -or ($childStarted -and $parent.StartTimeUtc -gt $childStarted)) { return $false }
        if (Test-MotifIdeProcess -Name $parent.Name) { return $true }
        $childStarted = $parent.StartTimeUtc
        $cursor = [int] $parent.ParentProcessId
    }
    return $false
}

function Test-MotifPathUnderRoot {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $RepoRoot)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    try { $candidate = [IO.Path]::GetFullPath($Path.Trim()) }
    catch { return $false }
    $comparison = if ($IsWindows -or $IsMacOS) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $root = Get-MotifNormalizedRoot $RepoRoot
    if ([string]::Equals($candidate, $root, $comparison)) { return $true }
    $separator = [IO.Path]::DirectorySeparatorChar
    $prefix = if ($root.EndsWith($separator)) { $root } else { $root + $separator }
    return $candidate.StartsWith($prefix, $comparison)
}

function Test-MotifCommandReferencesRoot {
    param([Parameter(Mandatory)][string] $CommandLine, [Parameter(Mandatory)][string] $RepoRoot)
    $root = Get-MotifNormalizedRoot $RepoRoot
    $comparison = if ($IsWindows -or $IsMacOS) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    # Windows command lines mix separators (bin/Release beside H:\repos), so compare with one.
    if ($IsWindows) { $CommandLine = $CommandLine.Replace('/', '\'); $root = $root.Replace('/', '\') }
    $offset = 0
    while ($offset -lt $CommandLine.Length) {
        $index = $CommandLine.IndexOf($root, $offset, $comparison)
        if ($index -lt 0) { return $false }
        $end = $index + $root.Length
        $beforeOkay = $index -eq 0 -or $CommandLine[$index - 1] -match '[\s"''=:(]'
        $afterOkay = $end -eq $CommandLine.Length -or $CommandLine[$end] -match '[\\/\s"'',;)]'
        if ($beforeOkay -and $afterOkay) { return $true }
        $offset = $index + 1
    }
    return $false
}

function Test-MotifProcessOwnedByRoot {
    param([Parameter(Mandatory)] $Information, [Parameter(Mandatory)][string] $RepoRoot)
    foreach ($path in $Information.Paths) {
        if (Test-MotifPathUnderRoot -Path ([string] $path) -RepoRoot $RepoRoot) { return $true }
    }
    return Test-MotifCommandReferencesRoot -CommandLine ([string] $Information.CommandLine) -RepoRoot $RepoRoot
}

function Stop-MotifStaleProcesses {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $RepoRoot,
        [Parameter(Mandatory)] $LockHandle,
        [string[]] $AdditionalProcessNames = @()
    )

    $root = Get-MotifNormalizedRoot $RepoRoot
    if (-not (Test-MotifLockHeld -LockHandle $LockHandle -RepoRoot $root)) {
        throw 'Stale process cleanup requires the active worktree lock.'
    }

    $names = @('dotnet', 'msbuild', 'VBCSCompiler', 'testhost*', 'motif', 'SIL.Motif.App', 'SIL.Motif.Worker', 'pangloss') +
        @($AdditionalProcessNames)
    $comparison = if ($IsWindows -or $IsMacOS) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $protectedIds = Get-MotifAncestorProcessIds -ProcessId $PID
    [void] $protectedIds.Add($PID)
    $candidates = [Collections.Generic.List[object]]::new()
    # One query for every process: per-process CIM lookups cost most of a second each on a busy machine.
    if ($IsWindows) {
        $script:WindowsProcessSnapshot = @{}
        foreach ($entry in Get-CimInstance Win32_Process) { $script:WindowsProcessSnapshot[[int] $entry.ProcessId] = $entry }
    }
    try {
        foreach ($process in [Diagnostics.Process]::GetProcesses()) {
            try {
                $id = $process.Id
                $name = $process.ProcessName
                $matchesName = $false
                foreach ($pattern in $names) {
                    if ($name -like $pattern) { $matchesName = $true; break }
                }
                if (-not $matchesName -or $protectedIds.Contains($id) -or (Test-MotifIdeProcess -Name $name)) { continue }

                $information = Get-MotifProcessInformation -ProcessId $id
                if (-not $information -or -not $information.StartTimeUtc -or
                    -not (Test-MotifProcessOwnedByRoot -Information $information -RepoRoot $root) -or
                    (Test-MotifProcessHasIdeAncestor -Information $information)) { continue }
                $candidates.Add($information)
            }
            catch { }
            finally { $process.Dispose() }
        }
    }
    finally { $script:WindowsProcessSnapshot = $null }

    $stopped = 0
    foreach ($candidate in $candidates) {
        if (-not (Test-MotifLockHeld -LockHandle $LockHandle -RepoRoot $root)) {
            throw 'The worktree lock was lost during stale process cleanup.'
        }
        if ($protectedIds.Contains([int] $candidate.Id)) { continue }
        $current = Get-MotifProcessInformation -ProcessId ([int] $candidate.Id)
        if (-not $current -or $current.Name -notin @($candidate.Name) -or
            -not (Test-MotifProcessStartMatches -ProcessId ([int] $candidate.Id) `
                -RecordedToken ([string] $candidate.StartToken)) -or
            (Test-MotifProcessHasIdeAncestor -Information $current) -or
            -not (Test-MotifProcessOwnedByRoot -Information $current -RepoRoot $root)) { continue }

        $target = $null
        try {
            $target = [Diagnostics.Process]::GetProcessById([int] $candidate.Id)
            if (-not $target.HasExited) {
                $target.Kill()
                if (-not $target.WaitForExit(10000) -and -not $target.HasExited) {
                    throw "Could not stop process $($candidate.Name) (PID $($candidate.Id)) from this worktree."
                }
                $stopped++
            }
        }
        catch [ArgumentException] { }
        finally { if ($target) { $target.Dispose() } }
    }

    if ($stopped -gt 0) { Write-Host "Stopped $stopped stale process(es) owned by $root." -ForegroundColor Yellow }
}

Export-ModuleMember -Function Set-MotifBuildProcessPolicy, Enter-MotifWorktreeLock, Exit-MotifWorktreeLock, Stop-MotifStaleProcesses
