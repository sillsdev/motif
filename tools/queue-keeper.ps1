<#
Keeps the merge queue moving without a person: cancels workflow runs of dead queue groups, requeues pull requests the
queue evicted only because known-flaky tests failed, and writes a summary. Every step is idempotent. Runs from
.github/workflows/queue-keeper.yml with GH_TOKEN set; run it by hand with `gh` logged in. -DryRun (or DRY_RUN=true)
changes nothing. See MotifQueueKeeper.psm1 for the decisions.
#>
[CmdletBinding()]
param(
    [string] $Repo = $(if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { 'sillsdev/motif' }),
    [switch] $DryRun,
    [int] $MaxRequeuesPerDay = 2
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'MotifQueueKeeper.psm1') -Force

if ($env:DRY_RUN -eq 'true') { $DryRun = $true }
$owner, $name = $Repo -split '/'
$marker = '<!-- queue-keeper:requeue -->'
$repoRoot = Split-Path $PSScriptRoot -Parent
$summary = [System.Collections.Generic.List[string]]::new()
function Log([string] $Line) { Write-Host $Line; $summary.Add($Line) }

function Invoke-Api([string[]] $ApiArgs) {
    $out = & gh api @ApiArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "gh api $($ApiArgs -join ' ') failed: $out" }
    ($out | Out-String)
}
function Invoke-Gh([string[]] $GhArgs) {
    $out = & gh @GhArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "gh $($GhArgs -join ' ') failed: $out" }
    ($out | Out-String)
}
function Get-Lines([string] $Text) { ,@($Text -split "`r?`n" | Where-Object { $_ }) }
function Invoke-Graphql([string] $Query, [hashtable] $Variables = @{}) {
    $a = @('graphql', '-f', "query=$Query")
    foreach ($k in $Variables.Keys) { $a += @('-F', "$k=$($Variables[$k])") }
    (Invoke-Api $a | ConvertFrom-Json).data
}
function Get-RunPages([string] $Query) {
    # --paginate with --jq prints one object per line.
    @(Get-Lines (Invoke-Api @("repos/$Repo/actions/runs?$Query&per_page=100", '--paginate', '--jq', '.workflow_runs[] | tojson')) | ForEach-Object { $_ | ConvertFrom-Json })
}

Log "## Queue keeper$(if ($DryRun) { ' (dry run)' })"
Log ''

try {
    # Queue state and open PRs with their queue timeline.
    $q = Invoke-Graphql @'
query($o:String!,$n:String!){repository(owner:$o,name:$n){
  mergeQueue(branch:"main"){entries(first:100){nodes{position state pullRequest{number}}}}
  pullRequests(states:OPEN,first:100){nodes{id number headRefName baseRefName isDraft state mergeable
    commits(last:1){nodes{commit{committedDate}}}
    timelineItems(last:40,itemTypes:[REMOVED_FROM_MERGE_QUEUE_EVENT,ADDED_TO_MERGE_QUEUE_EVENT]){nodes{__typename
      ... on RemovedFromMergeQueueEvent{createdAt reason} ... on AddedToMergeQueueEvent{createdAt}}}}}}}
'@ @{ o = $owner; n = $name }
    $entries = @($q.repository.mergeQueue.entries.nodes)
    $prs = @($q.repository.pullRequests.nodes)
    $inQueue = @{}
    foreach ($e in $entries) { $inQueue[[int]$e.pullRequest.number] = $e }

    Log '### Queue'
    if ($entries.Count -eq 0) { Log '- empty' }
    foreach ($e in $entries) { Log "- #$($e.pullRequest.number) position $($e.position): $($e.state)" }
    Log ''

    # 1. Cancel dead-group runs: runs are listed before refs and each ref is rechecked before its cancel.
    Log '### Cancelled runs'
    $active = foreach ($s in 'queued', 'in_progress', 'waiting', 'requested', 'pending') { Get-RunPages "status=$s" }
    $liveRefs = Get-Lines (Invoke-Api @("repos/$Repo/git/matching-refs/heads/gh-readonly-queue", '--paginate', '--jq', '.[].ref'))
    $deadRuns = @(Get-DeadQueueRuns -Runs @($active | Where-Object { $_ }) -LiveRefs $liveRefs)
    if ($deadRuns.Count -eq 0) { Log '- none' }
    foreach ($run in $deadRuns) {
        try {
            $now = @(Get-Lines (Invoke-Api @("repos/$Repo/git/matching-refs/heads/$($run.head_branch)", '--jq', '.[].ref')))
            if ($now.Count -gt 0) { Log "- run $($run.id) kept: $($run.head_branch) is live again"; continue }
            if ($DryRun) { Log "- would cancel run $($run.id) ($($run.name), $($run.head_branch))"; continue }
            Invoke-Api @('-X', 'POST', "repos/$Repo/actions/runs/$($run.id)/cancel") | Out-Null
            Log "- cancelled run $($run.id) ($($run.name), $($run.head_branch))"
        } catch { Log "- run $($run.id) left alone: $($_.Exception.Message)" }
    }
    Log ''

    # 2. Requeue flake evictions. Any error while gathering evidence for a PR means no requeue for it.
    Log '### Requeue decisions'
    $flakeTexts = $null
    $mergeGroupRuns = $null
    $today = (Get-Date).ToUniversalTime().Date
    foreach ($pr in $prs) {
        $number = [int]$pr.number
        if ($inQueue.ContainsKey($number)) { continue }
        $eviction = Get-LatestEviction -Events @($pr.timelineItems.nodes)
        if (-not $eviction) { continue }
        $reason = [string]$eviction.reason
        if (-not (Test-RequeueableEvictionReason -Reason $reason)) { Log "- #$number left alone: evicted for $reason"; continue }
        try {
            # A push after the eviction supersedes the old failure: requeue once the required checks on the new head are green.
            # Push time is approximated by the head commit's committedDate (a rebase or amend rewrites it); a plain push of an
            # older commit would not look newer.
            $headDate = $null
            $lastCommit = @($pr.commits.nodes | Select-Object -Last 1)
            if ($lastCommit.Count -gt 0) { $headDate = $lastCommit[0].commit.committedDate }
            $pushArgs = @{ State = [string]$pr.state; IsDraft = [bool]$pr.isDraft; BaseRef = [string]$pr.baseRefName
                EvictedAt = $eviction.createdAt; HeadCommittedAt = $headDate; Mergeable = [string]$pr.mergeable }
            $pushed = Get-PushedSinceEvictionDecision @pushArgs
            if ($pushed.Action -eq 'NeedChecks') {
                $errFile = [System.IO.Path]::GetTempFileName()
                try {
                    $c = & gh pr checks $number -R $Repo --required --json name,bucket 2>$errFile
                    $code = $LASTEXITCODE
                } finally { Remove-Item -LiteralPath $errFile -ErrorAction SilentlyContinue }
                $parsed = ConvertFrom-RequiredChecksResult -ExitCode $code -Stdout ($c | Out-String)
                if (-not $parsed.Ok) { throw $parsed.Error }
                $pushed = Get-PushedSinceEvictionDecision @pushArgs -CheckBuckets $parsed.Buckets
            }
            if ($pushed.Action -eq 'Wait') { Log "- #$number $($pushed.Reason) (evicted for $reason, pushed since)"; continue }
            if ($pushed.Action -eq 'Requeue') {
                $markerTimes = Get-Lines (Invoke-Api @("repos/$Repo/issues/$number/comments", '--paginate', '--jq', '.[] | select(.body | contains("queue-keeper:requeue")) | .created_at'))
                $todayCount = @($markerTimes | Where-Object { ([datetime]$_).ToUniversalTime().Date -eq $today }).Count
                if ($todayCount -ge $MaxRequeuesPerDay) { Log "- #$number left alone: already requeued $todayCount time(s) today"; continue }
                if ($DryRun) { Log "- #$number would be requeued: $($pushed.Reason) (evicted for $reason)"; continue }
                # The marker comes first so the cap can be counted. Enqueue directly: auto-merge is lost on eviction and cannot be re-enabled here.
                Invoke-Api @('-X', 'POST', "repos/$Repo/issues/$number/comments", '-f', "body=$marker`nQueue keeper is requeueing this PR after a $reason eviction: $($pushed.Reason).") | Out-Null
                Invoke-Graphql 'mutation($id:ID!){enqueuePullRequest(input:{pullRequestId:$id}){mergeQueueEntry{position}}}' @{ id = $pr.id } | Out-Null
                Log "- #$number requeued: $($pushed.Reason) (evicted for $reason)"
                continue
            }
            if ($reason -eq 'merge_conflict') { Log "- #$number left alone: evicted for merge_conflict ($($pushed.Reason))"; continue }
            if ($null -eq $mergeGroupRuns) { $mergeGroupRuns = @(Get-RunPages 'event=merge_group&status=failure') }
            $failed = @($mergeGroupRuns | Where-Object { $_.head_branch -like "gh-readonly-queue/main/pr-$number-*" } | Sort-Object created_at -Descending)
            if ($failed.Count -eq 0) { Log "- #$number left alone: no failed queue-group run found"; continue }

            $jobs = @(Get-Lines (Invoke-Api @("repos/$Repo/actions/runs/$($failed[0].id)/jobs?per_page=100", '--paginate', '--jq', '.jobs[] | tojson')) | ForEach-Object { $_ | ConvertFrom-Json })
            $jobResults = [System.Collections.Generic.List[object]]::new()
            $failing = [System.Collections.Generic.List[string]]::new()
            foreach ($job in @($jobs | Where-Object { $_.conclusion -notin 'success', 'skipped' })) {
                $tests = @()
                if ($job.conclusion -eq 'failure') {
                    $tests = @(Get-FailedTestNames -LogText (Invoke-Api @('--allow-escape-sequences', "repos/$Repo/actions/jobs/$($job.id)/logs")))
                }
                foreach ($t in $tests) { if (-not $failing.Contains($t)) { $failing.Add($t) } }
                $jobResults.Add([pscustomobject]@{ Name = $job.name; Conclusion = [string]$job.conclusion; Tests = $tests })
            }

            if ($null -eq $flakeTexts) {
                $flakeTexts = @((Invoke-Gh @('issue', 'list', '-R', $Repo, '--label', 'flake', '--state', 'open', '--limit', '100', '--json', 'number,title,body') | ConvertFrom-Json) |
                    ForEach-Object { "$($_.title)`n$($_.body)" })
            }
            $prFiles = Get-Lines (Invoke-Api @("repos/$Repo/pulls/$number/files", '--paginate', '--jq', '.[].filename'))

            # Source files are looked up at the PR head, not the checkout of main.
            $testFiles = @{}
            if ($failing.Count -gt 0) {
                $f = & git -C $repoRoot fetch --quiet --depth 1 origin "pull/$number/head" 2>&1
                if ($LASTEXITCODE -ne 0) { throw "git fetch pull/$number/head failed: $f" }
                foreach ($t in $failing) {
                    $parts = $t -split '\.'
                    if ($parts.Count -lt 2) { continue }
                    $hits = @(& git -C $repoRoot grep -l -E "(class|record|struct) $([regex]::Escape($parts[-2]))\b" FETCH_HEAD -- '*.cs' 2>$null)
                    if ($LASTEXITCODE -gt 1) { throw "git grep for $($parts[-2]) failed" }
                    if ($hits.Count -eq 1) { $testFiles[$t] = ($hits[0] -replace '^FETCH_HEAD:', '') }
                }
            }

            $markerTimes = Get-Lines (Invoke-Api @("repos/$Repo/issues/$number/comments", '--paginate', '--jq', '.[] | select(.body | contains("queue-keeper:requeue")) | .created_at'))
            $todayCount = @($markerTimes | Where-Object { ([datetime]$_).ToUniversalTime().Date -eq $today }).Count

            # TODO: pass -QuarantineNames from [Trait("Quarantine", ...)] once cli/quarantine is merged.
            $decision = Get-RequeueDecision -FailingTests $failing.ToArray() -FlakeTexts $flakeTexts -PrFiles $prFiles -TestFiles $testFiles `
                -RequeuesToday $todayCount -MaxPerDay $MaxRequeuesPerDay -Jobs $jobResults.ToArray()
            $tests = if ($failing.Count) { $failing -join ', ' } else { 'none parsed' }
            if (-not $decision.Requeue) { Log "- #$number left alone: $($decision.Reason) (failing: $tests; run $($failed[0].id))"; continue }
            if ($DryRun) { Log "- #$number would be requeued: $($decision.Reason) (failing: $tests; run $($failed[0].id))"; continue }

            # The marker comes first: if it cannot be written the cap could not be counted, so nothing is enqueued.
            Invoke-Api @('-X', 'POST', "repos/$Repo/issues/$number/comments", '-f', "body=$marker`nQueue keeper is requeueing this PR after a failed-checks eviction: $($decision.Reason) ($tests).") | Out-Null
            Invoke-Graphql 'mutation($id:ID!){enqueuePullRequest(input:{pullRequestId:$id}){mergeQueueEntry{position}}}' @{ id = $pr.id } | Out-Null
            Log "- #$number requeued: $($decision.Reason) (failing: $tests)"
        } catch { Log "- #$number left alone: $($_.Exception.Message)" }
    }
    Log ''
} catch {
    # A refused token or an API outage must not turn every 10-minute run red.
    Log "- keeper stopped: $($_.Exception.Message)"
} finally {
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value ($summary -join "`n") }
}
exit 0
