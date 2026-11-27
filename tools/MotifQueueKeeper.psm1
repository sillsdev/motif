Set-StrictMode -Version Latest

# Pure decision logic for tools/queue-keeper.ps1: no network, no clock, so tools/MotifQueueKeeper.Tests.ps1 can pin it.

# A workflow run is dead when it belongs to a merge-queue group branch that GitHub no longer lists.
function Get-DeadQueueRuns {
    [CmdletBinding()]
    param([object[]] $Runs = @(), [string[]] $LiveRefs = @())

    $live = @{}
    foreach ($ref in $LiveRefs) { $live[($ref -replace '^refs/heads/', '')] = $true }
    foreach ($run in $Runs) {
        if ($run.status -eq 'completed') { continue }
        if (-not ([string]$run.head_branch).StartsWith('gh-readonly-queue/')) { continue }
        if ($live.ContainsKey([string]$run.head_branch)) { continue }
        $run
    }
}

# xUnit prints "[xUnit.net 00:00:01.2]     Ns.Class.Method [FAIL]" (and a theory's arguments after the name).
# Returns the distinct fully qualified test names, without arguments, in order of first appearance.
function Get-FailedTestNames {
    [CmdletBinding()]
    param([string] $LogText)

    $seen = [System.Collections.Generic.List[string]]::new()
    foreach ($line in ($LogText -split "`r?`n")) {
        $clean = $line -replace '^\d{4}-\d\d-\d\dT[\d:.]+Z\s*', '' -replace '\x1b\[[\d;]*m', ''
        if ($clean -match '^(?:\[xUnit\.net [\d:.]+\]\s+)?(\S+?)(?:\(.*\))? \[FAIL\]\s*$') {
            $name = $Matches[1]
            if ($name -match '^[A-Za-z_][\w.]*\.[A-Za-z_]\w*$' -and -not $seen.Contains($name)) { $seen.Add($name) }
        }
    }
    , $seen.ToArray()
}

# A test counts as named in an issue when its full name, or Class.Method, appears as a whole token: the characters
# either side may not continue an identifier, so Ns.C.Method never matches inside Ns.C.MethodTwo, and a bare method
# name never matches at all.
function Test-NamedInText {
    [CmdletBinding()]
    param([string] $TestName, [string] $Text)

    if (-not $Text) { return $false }
    $parts = $TestName -split '\.'
    $forms = @($TestName)
    if ($parts.Count -ge 3) { $forms += ($parts[-2] + '.' + $parts[-1]) }
    foreach ($form in $forms) {
        if ($Text -cmatch ('(?<![\w.])' + [regex]::Escape($form) + '(?![\w]|\.\w)')) { return $true }
    }
    return $false
}

# Decides whether a PR evicted for failed checks may be requeued. Returns Requeue (bool) and Reason.
#   FailingTests    fully qualified names from the failed queue run
#   FlakeTexts      title plus body of each open issue labelled flake
#   QuarantineNames TODO: tests tagged [Trait("Quarantine", ...)] once cli/quarantine lands (always empty today)
#   PrFiles         paths changed by the PR
#   TestFiles       hashtable of test name to the repo path that defines its class
#   RequeuesToday   requeues the keeper already made for this PR today
function Get-RequeueDecision {
    [CmdletBinding()]
    param(
        [string[]] $FailingTests = @(),
        [string[]] $FlakeTexts = @(),
        [string[]] $QuarantineNames = @(),
        [string[]] $PrFiles = @(),
        [hashtable] $TestFiles = @{},
        [int] $RequeuesToday = 0,
        [int] $MaxPerDay = 2,
        # One entry per non-success job of the failed run: Name, Conclusion, Tests (names parsed from its log).
        [object[]] $Jobs = $null
    )

    if ($null -ne $Jobs) {
        foreach ($job in $Jobs) {
            if ($job.Conclusion -ne 'failure') { return [pscustomobject]@{ Requeue = $false; Reason = "job '$($job.Name)' ended $($job.Conclusion), not a test failure" } }
            if (@($job.Tests).Count -eq 0) { return [pscustomobject]@{ Requeue = $false; Reason = "job '$($job.Name)' failed with no [FAIL] test names in its log" } }
        }
    }

    if ($FailingTests.Count -eq 0) { return [pscustomobject]@{ Requeue = $false; Reason = 'no [FAIL] test names found in the failed job logs' } }
    if ($RequeuesToday -ge $MaxPerDay) { return [pscustomobject]@{ Requeue = $false; Reason = "already requeued $RequeuesToday time(s) today" } }

    foreach ($test in $FailingTests) {
        if (-not ($TestFiles.ContainsKey($test) -and $TestFiles[$test])) {
            return [pscustomobject]@{ Requeue = $false; Reason = "no source file found for $test at the PR head, so the diff guard cannot clear it" }
        }
        if ($PrFiles -contains $TestFiles[$test]) {
            return [pscustomobject]@{ Requeue = $false; Reason = "$test is defined in $($TestFiles[$test]), which this PR changes" }
        }
    }
    foreach ($test in $FailingTests) {
        $quarantined = $QuarantineNames -contains $test
        $inFlake = $false
        foreach ($text in $FlakeTexts) { if (Test-NamedInText -TestName $test -Text $text) { $inFlake = $true; break } }
        if (-not ($quarantined -or $inFlake)) {
            return [pscustomobject]@{ Requeue = $false; Reason = "$test is not named in an open flake issue and is not quarantined" }
        }
    }
    [pscustomobject]@{ Requeue = $true; Reason = "all $($FailingTests.Count) failing test(s) are known flakes" }
}

# True when the PR's latest queue event is a failed_checks removal: it was evicted and nobody has requeued it since.
function Get-LatestEvictionReason {
    [CmdletBinding()]
    param([object[]] $Events = @())

    $last = $Events | Select-Object -Last 1
    if ($last -and $last.__typename -eq 'RemovedFromMergeQueueEvent') { return [string]$last.reason }
    return $null
}

# The latest queue event when it is a removal (the PR was evicted and nobody has requeued it since); else $null.
function Get-LatestEviction {
    [CmdletBinding()]
    param([object[]] $Events = @())

    $last = $Events | Select-Object -Last 1
    if ($last -and $last.__typename -eq 'RemovedFromMergeQueueEvent') { return $last }
    return $null
}

# Decides whether a PR evicted for merge_conflict or failed_checks may be requeued because it was pushed to since.
# Returns Action (Requeue, Wait or Skip) and Reason. Requires: open, not a draft, base main, head committed after the
# eviction, mergeable MERGEABLE, and every required check on the head green (Buckets from `gh pr checks --required`).
function Get-PushedSinceEvictionDecision {
    [CmdletBinding()]
    param(
        [string] $State = 'OPEN',
        [bool] $IsDraft = $false,
        [string] $BaseRef = 'main',
        $EvictedAt,
        $HeadCommittedAt,
        [string] $Mergeable = 'UNKNOWN',
        [string[]] $CheckBuckets = @()
    )

    function Skip([string] $Why) { [pscustomobject]@{ Action = 'Skip'; Reason = $Why } }
    if ($State -ne 'OPEN') { return Skip "state is $State" }
    if ($IsDraft) { return Skip 'it is a draft' }
    if ($BaseRef -ne 'main') { return Skip "base is $BaseRef, not main" }
    if ($null -eq $HeadCommittedAt -or $null -eq $EvictedAt -or ([datetime]$HeadCommittedAt) -le ([datetime]$EvictedAt)) { return Skip 'head is not newer than the eviction' }
    if ($Mergeable -ne 'MERGEABLE') { return Skip "mergeable is $Mergeable" }
    $buckets = @($CheckBuckets)
    $bad = @($buckets | Where-Object { $_ -notin 'pass', 'skipping', 'pending' })
    if ($bad.Count -gt 0) { return Skip "required checks not green ($($bad -join ', '))" }
    if (@($buckets | Where-Object { $_ -eq 'pending' }).Count -gt 0) { return [pscustomobject]@{ Action = 'Wait'; Reason = 'waiting for checks' } }
    if ($buckets.Count -eq 0) { return [pscustomobject]@{ Action = 'Wait'; Reason = 'waiting for checks (none reported yet)' } }
    [pscustomobject]@{ Action = 'Requeue'; Reason = 'pushed since the eviction, mergeable, required checks green' }
}
