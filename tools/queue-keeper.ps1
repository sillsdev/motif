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
    $out
}
function Invoke-Graphql([string] $Query, [hashtable] $Variables = @{}) {
    $a = @('graphql', '-f', "query=$Query")
    foreach ($k in $Variables.Keys) { $a += @('-F', "$k=$($Variables[$k])") }
    (Invoke-Api $a | Out-String | ConvertFrom-Json).data
}

Log "## Queue keeper$(if ($DryRun) { ' (dry run)' })"
Log ''

# Queue state and open PRs with their queue timeline.
$q = Invoke-Graphql @'
query($o:String!,$n:String!){repository(owner:$o,name:$n){
  mergeQueue(branch:"main"){entries(first:100){nodes{position state pullRequest{number}}}}
  pullRequests(states:OPEN,first:100){nodes{id number headRefName
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

# 1. Cancel dead-group runs.
Log '### Cancelled runs'
$liveRefs = @((Invoke-Api @("repos/$Repo/git/matching-refs/heads/gh-readonly-queue", '--paginate', '--jq', '.[].ref')) -split "`r?`n" | Where-Object { $_ })
$active = foreach ($s in 'queued', 'in_progress', 'waiting', 'requested', 'pending') {
    (Invoke-Api @("repos/$Repo/actions/runs?status=$s&per_page=100") | Out-String | ConvertFrom-Json).workflow_runs
}
$deadRuns = @(Get-DeadQueueRuns -Runs @($active | Where-Object { $_ }) -LiveRefs $liveRefs)
if ($deadRuns.Count -eq 0) { Log '- none' }
foreach ($run in $deadRuns) {
    if ($DryRun) { Log "- would cancel run $($run.id) ($($run.name), $($run.head_branch))"; continue }
    try {
        Invoke-Api @('-X', 'POST', "repos/$Repo/actions/runs/$($run.id)/cancel") | Out-Null
        Log "- cancelled run $($run.id) ($($run.name), $($run.head_branch))"
    } catch { Log "- could not cancel run $($run.id): $($_.Exception.Message)" }
}
Log ''

# 2. Requeue flake evictions.
Log '### Requeue decisions'
$flakeTexts = $null
$mergeGroupRuns = $null
$today = (Get-Date).ToUniversalTime().Date
foreach ($pr in $prs) {
    $number = [int]$pr.number
    if ($inQueue.ContainsKey($number)) { continue }
    $events = @($pr.timelineItems.nodes)
    $reason = Get-LatestEvictionReason -Events $events
    if (-not $reason) { continue }
    if ($reason -ne 'failed_checks') { Log "- #$number left alone: evicted for $reason"; continue }

    if ($null -eq $mergeGroupRuns) {
        $mergeGroupRuns = @(foreach ($page in 1..5) {
            (Invoke-Api @("repos/$Repo/actions/runs?event=merge_group&status=failure&per_page=100&page=$page") | Out-String | ConvertFrom-Json).workflow_runs
        })
    }
    $runs = $mergeGroupRuns
    $failed = @($runs | Where-Object { $_.head_branch -like "gh-readonly-queue/main/pr-$number-*" -and $_.conclusion -eq 'failure' } |
        Sort-Object created_at -Descending)
    if ($failed.Count -eq 0) { Log "- #$number left alone: no failed queue-group run found"; continue }

    $failing = [System.Collections.Generic.List[string]]::new()
    $jobs = (Invoke-Api @("repos/$Repo/actions/runs/$($failed[0].id)/jobs?per_page=100") | Out-String | ConvertFrom-Json).jobs
    foreach ($job in @($jobs | Where-Object { $_.conclusion -eq 'failure' })) {
        $text = (& gh api --allow-escape-sequences "repos/$Repo/actions/jobs/$($job.id)/logs" 2>&1 | Out-String)
        foreach ($t in (Get-FailedTestNames -LogText $text)) { if (-not $failing.Contains($t)) { $failing.Add($t) } }
    }

    if ($null -eq $flakeTexts) {
        $flakeTexts = @((& gh issue list -R $Repo --label flake --state open --limit 100 --json 'number,title,body' | ConvertFrom-Json) |
            ForEach-Object { "$($_.title)`n$($_.body)" })
    }
    $prFiles = @((& gh api "repos/$Repo/pulls/$number/files" --paginate --jq '.[].filename') -split "`r?`n" | Where-Object { $_ })
    $testFiles = @{}
    foreach ($t in $failing) {
        $parts = $t -split '\.'
        if ($parts.Count -ge 2) {
            $hit = @(& git -C $repoRoot grep -l -E "class $([regex]::Escape($parts[-2]))\b" -- '*.cs' 2>$null) | Select-Object -First 1
            if ($hit) { $testFiles[$t] = $hit }
        }
    }
    $comments = @((& gh api "repos/$Repo/issues/$number/comments" --paginate --jq '.[] | select(.body | contains("queue-keeper:requeue")) | .created_at') -split "`r?`n" | Where-Object { $_ })
    $todayCount = @($comments | Where-Object { ([datetime]$_).ToUniversalTime().Date -eq $today }).Count

    # TODO: pass -QuarantineNames from [Trait("Quarantine", ...)] once cli/quarantine is merged.
    $decision = Get-RequeueDecision -FailingTests $failing.ToArray() -FlakeTexts $flakeTexts -PrFiles $prFiles -TestFiles $testFiles `
        -RequeuesToday $todayCount -MaxPerDay $MaxRequeuesPerDay
    $tests = if ($failing.Count) { $failing -join ', ' } else { 'none parsed' }
    if (-not $decision.Requeue) { Log "- #$number left alone: $($decision.Reason) (failing: $tests; run $($failed[0].id))"; continue }
    if ($DryRun) { Log "- #$number would be requeued: $($decision.Reason) (failing: $tests; run $($failed[0].id))"; continue }
    try {
        Invoke-Graphql 'mutation($id:ID!){enqueuePullRequest(input:{pullRequestId:$id}){mergeQueueEntry{position}}}' @{ id = $pr.id } | Out-Null
        & gh pr comment $number -R $Repo --body "$marker`nQueue keeper requeued this PR after a failed-checks eviction: $($decision.Reason) ($tests)." | Out-Null
        Log "- #$number requeued: $($decision.Reason) (failing: $tests)"
    } catch { Log "- #$number requeue failed: $($_.Exception.Message)" }
}
Log ''

if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value ($summary -join "`n") }
