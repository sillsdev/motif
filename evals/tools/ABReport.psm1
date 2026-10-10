Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ABHarness.psm1')
function Write-ABReport {
    param($completed, $selectedTasks, $arms, $trialCount, $primaryMetric, $questionData, $questionId, $runId, $runRoot)
    function Get-ABMean([double[]] $Values) {
        if ($Values.Count -eq 0) { return $null }
        return [double](($Values | Measure-Object -Average).Average)
    }

    $paired = [Collections.Generic.List[object]]::new()
    foreach ($task in $selectedTasks) {
        for ($trial = 1; $trial -le $trialCount; $trial++) {
            $left = $completed | Where-Object { $_.task -eq $task.id -and $_.arm -eq $arms[0].id -and $_.trial -eq $trial } | Select-Object -First 1
            $right = $completed | Where-Object { $_.task -eq $task.id -and $_.arm -eq $arms[1].id -and $_.trial -eq $trial } | Select-Object -First 1
            if ($left -and $right -and $left.integrity.state -eq 'clean' -and $right.integrity.state -eq 'clean' -and
                $null -ne $left.primaryScore -and $null -ne $right.primaryScore) {
                $setLanguagePath = Join-Path $task.setPath 'language/language.yaml'
                $languageId = if (Test-Path -LiteralPath $setLanguagePath) { (Read-ABJson $setLanguagePath).id } else { $task.set }
                $paired.Add([ordered]@{
                    task = [string]$task.id; set = [string]$task.set; language = [string]$languageId
                    trial = $trial; armA = [double]$left.primaryScore; armB = [double]$right.primaryScore
                    difference = [double]$right.primaryScore - [double]$left.primaryScore
                })
            }
        }
    }
    $diffs = @($paired | ForEach-Object { [double]$_.difference })
    $meanDifference = Get-ABMean $diffs
    $interval = $null
    if ($diffs.Count -gt 0) {
        $clusterGroups = @($paired | Group-Object { $_.set + '|' + $_.language })
        $rng = [Random]::new(531009)
        $bootstrap = [Collections.Generic.List[double]]::new()
        for ($iteration = 0; $iteration -lt 2000; $iteration++) {
            $sample = [Collections.Generic.List[double]]::new()
            for ($clusterIndex = 0; $clusterIndex -lt $clusterGroups.Count; $clusterIndex++) {
                $cluster = $clusterGroups[$rng.Next(0, $clusterGroups.Count)]
                $taskGroups = @($cluster.Group | Group-Object { [string]$_.task })
                for ($taskIndex = 0; $taskIndex -lt $taskGroups.Count; $taskIndex++) {
                    $taskGroup = $taskGroups[$rng.Next(0, $taskGroups.Count)]
                    $rows = @($taskGroup.Group)
                    $sample.Add([double]$rows[$rng.Next(0, $rows.Count)].difference)
                }
            }
            if ($sample.Count -gt 0) { $bootstrap.Add((Get-ABMean @($sample))) }
        }
        $sorted = @($bootstrap | Sort-Object)
        $lowerIndex = [Math]::Max(0, [int][Math]::Floor(($sorted.Count - 1) * 0.025))
        $upperIndex = [Math]::Min($sorted.Count - 1, [int][Math]::Ceiling(($sorted.Count - 1) * 0.975))
        $interval = [ordered]@{ lower = $sorted[$lowerIndex]; upper = $sorted[$upperIndex]; method = 'paired bootstrap, clustered by set and language, 2000 resamples' }
    }

    $armSummary = [Collections.Generic.List[object]]::new()
    foreach ($arm in $arms) {
        $armRows = @($completed | Where-Object { $_.arm -eq $arm.id })
        $validRows = @($armRows | Where-Object { $_.integrity.state -eq 'clean' -and $null -ne $_.primaryScore })
        $taskGroups = @($armRows | Group-Object { [string]$_.task })
        $eligibleTasks = 0
        $passedAtOne = 0
        $passedAll = 0
        foreach ($group in $taskGroups) {
            $taskRows = @($group.Group | Sort-Object trial)
            if (@($taskRows | Where-Object { $_.integrity.state -ne 'clean' -or $null -eq $_.primaryScore }).Count -gt 0) { continue }
            $eligibleTasks++
            if ($taskRows.Count -gt 0 -and $taskRows[0].success -eq $true) { $passedAtOne++ }
            if ($taskRows.Count -eq $trialCount -and @($taskRows | Where-Object { $_.success -ne $true }).Count -eq 0) { $passedAll++ }
        }
        $successfulCosts = @($validRows | Where-Object { $_.success -eq $true -and $null -ne $_.costUsd } | ForEach-Object { [double]$_.costUsd })
        $costPerSuccess = if ($successfulCosts.Count -gt 0) {
            [double](($successfulCosts | Measure-Object -Sum).Sum) / @($validRows | Where-Object { $_.success -eq $true }).Count
        } else { $null }
        $errorCounts = @($armRows | ForEach-Object { $_.activity } | Where-Object { $_.isError } |
            Group-Object { if ($_.Contains('name')) { [string]$_.name } elseif ($_.Contains('tool')) { [string]$_.tool } else { '' } } |
            Sort-Object Count -Descending | Select-Object -First 10 | ForEach-Object { [ordered]@{ tool = $_.Name; count = $_.Count } })
        $refusalCounts = @($armRows | ForEach-Object { $_.activity } | Where-Object { $_.isError } |
            ForEach-Object { if ($_.Contains('refusalCode') -and $_.refusalCode) { [string]$_.refusalCode } else { $null } } | Where-Object { $_ } |
            Group-Object | Sort-Object Count -Descending | Select-Object -First 10 | ForEach-Object { [ordered]@{ code = $_.Name; count = $_.Count } })
        $armSummary.Add([ordered]@{
            id = [string]$arm.id; host = [string]$arm.host; model = $arm.model; effort = $arm.effort
            authMode = $arm.authMode
            requestedTrials = $trialCount
            meanPrimaryScore = Get-ABMean @($validRows | ForEach-Object { [double]$_.primaryScore })
            passAt1 = if ($eligibleTasks -gt 0) { [double]$passedAtOne / $eligibleTasks } else { $null }
            passPowerK = if ($eligibleTasks -gt 0) { [double]$passedAll / $eligibleTasks } else { $null }
            costPerSuccessUsd = $costPerSuccess
            successfulTasksAtK = $passedAll; eligibleTasks = $eligibleTasks
            infrastructureInvalid = @($armRows | Where-Object { $_.infrastructureInvalid }).Count
            quarantined = @($armRows | Where-Object { $_.integrity.state -ne 'clean' }).Count
            meanWallMs = Get-ABMean @($validRows | ForEach-Object { [double]$_.wallMs })
            meanTurns = Get-ABMean @($validRows | ForEach-Object { [double]$_.turns })
            meanToolCalls = Get-ABMean @($validRows | ForEach-Object { [double]$_.toolCalls })
            meanInputTokens = Get-ABMean @($validRows | Where-Object { $null -ne $_.inputTokens } | ForEach-Object { [double]$_.inputTokens })
            meanOutputTokens = Get-ABMean @($validRows | Where-Object { $null -ne $_.outputTokens } | ForEach-Object { [double]$_.outputTokens })
            topToolErrors = $errorCounts; topRefusals = $refusalCounts
        })
    }

    $failureExcerpts = [Collections.Generic.List[object]]::new()
    foreach ($row in $completed | Where-Object { $_.infrastructureInvalid -or $_.success -ne $true }) {
        $activityTail = @($row.activity | Select-Object -Last 3 | ForEach-Object {
            [ordered]@{
                name = if ($_.Contains('name')) { $_.name } elseif ($_.Contains('tool')) { $_.tool } else { '' }
                arguments = if ($_.Contains('arguments')) { $_.arguments } elseif ($_.Contains('args')) { $_.args } else { $null }
                isError = $_.Contains('isError') -and [bool]$_.isError
                result = if ($_.Contains('result')) { $_.result } elseif ($_.Contains('code')) { $_.code } else { $null }
            }
        })
        $hostPath = if ($row.Contains('sourceTrialRoot')) { Join-Path $row.sourceTrialRoot 'frozen/host.json' }
            else { Join-Path $row.trialRoot 'host.json' }
        $hostData = $null
        if (Test-Path -LiteralPath $hostPath) {
            try { $hostData = Read-ABJson $hostPath } catch { $hostData = $null }
        }
        $failureExcerpts.Add([ordered]@{
            task = $row.task; arm = $row.arm; trial = $row.trial; status = $row.status
            lastToolCalls = $activityTail
            finalMessage = if ($hostData -is [Collections.IDictionary] -and $hostData.Contains('finalMessage')) {
                $hostData.finalMessage
            } else { $null }
        })
    }

    $intervalCrossesZero = $null -eq $interval -or ($interval.lower -le 0 -and $interval.upper -ge 0)
    $leftLabel = [string]$arms[0].id
    $rightLabel = [string]$arms[1].id
    $meanText = if ($null -eq $meanDifference) { 'unavailable' } else { '{0:P1}' -f $meanDifference }
    $intervalText = if ($interval) { '[{0:P1}, {1:P1}]' -f $interval.lower, $interval.upper } else { '[unavailable]' }
    if ($null -eq $interval) {
        $verdict = "No clean paired scores are available to compare $rightLabel with $leftLabel on $primaryMetric."
    }
    elseif ($intervalCrossesZero) {
        $verdict = "There is not enough evidence to say whether $rightLabel is better than $leftLabel on $primaryMetric; the paired mean difference (B minus A) is $meanText with a 95% interval $intervalText."
        $verdict += " The interval includes zero, so this run does not establish a difference."
    }
    elseif ($meanDifference -gt 0) {
        $verdict = "$rightLabel scored higher than $leftLabel on $primaryMetric by a paired mean of $meanText, with a 95% interval $intervalText."
        $verdict += " The interval stays above zero across the paired tasks and trials in this run."
    }
    else {
        $verdict = "$leftLabel scored higher than $rightLabel on $primaryMetric by a paired mean of {0:P1}, with a 95% interval $intervalText." -f [Math]::Abs($meanDifference)
        $verdict += " The interval stays below zero across the paired tasks and trials in this run."
    }
    $incompleteCount = @($completed | Where-Object { $_.status -eq 'inconclusive' -or $null -eq $_.primaryScore -or $_.integrity.state -ne 'clean' }).Count
    $report = [Collections.Generic.List[string]]::new()
    $report.Add($verdict)
    $report.Add('')
    $report.Add("Question: $($questionData.question)")
    $report.Add("Run: $runId | primary metric: $primaryMetric | trials per task: $trialCount | paired trials: $($paired.Count) | inconclusive or invalid trials: $incompleteCount")
    $report.Add('')
    $integritySummary = Get-ABIntegritySummary @($completed)
    $report.Add('## Integrity')
    $report.Add('')
    $report.Add("Attempted: $($integritySummary.attempted). Clean: $($integritySummary.counts.clean); review: $($integritySummary.counts.review); invalid: $($integritySummary.counts.invalid); isolation_failure: $($integritySummary.counts.isolation_failure); infrastructure_failure: $($integritySummary.counts.infrastructure_failure).")
    if ($integritySummary.confidenceInterval95) {
        $report.Add(('Clean / attempted: {0:P1}, Wilson 95% interval [{1:P1}, {2:P1}].' -f $integritySummary.cleanRate, $integritySummary.confidenceInterval95.lower, $integritySummary.confidenceInterval95.upper))
    }
    $gradingFailures = @($completed | Where-Object { $_.Contains('gradingState') -and $_.gradingState -eq 'infrastructure_failure' }).Count
    $judgeSplits = @($completed | Where-Object { $_.Contains('judgeDisagreements') -and $_.judgeDisagreements -gt 0 }).Count
    $report.Add("Control-side grading failures: $gradingFailures. Trials with a recorded judge split: $judgeSplits.")
    $report.Add('Only clean trials with complete grading are scored. Evaluation awareness is recorded without penalty. All other outcomes remain in the denominator.')
    foreach ($row in $completed | Where-Object { $_.integrity.state -ne 'clean' }) {
        $reasons = ($row.integrity.reasons | ForEach-Object { $_.reason }) -join '; '
        $report.Add("- $($row.task) / $($row.arm) / $($row.trial): $($row.integrity.state) — $reasons")
    }
    $report.Add('')
    $report.Add('## Arms')
    $report.Add('')
    $report.Add('| Arm | Host / model | Auth mode | Mean primary score | pass@1 | pass^k | Cost per success | Mean wall time | Mean turns | Mean tool calls | Quarantined trials |')
    $report.Add('|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|')
    foreach ($armResult in $armSummary) {
        $passAtOneText = if ($null -ne $armResult.passAt1) { '{0:P1}' -f $armResult.passAt1 } else { 'n/a' }
        $passKText = if ($null -ne $armResult.passPowerK) { '{0:P1}' -f $armResult.passPowerK } else { 'n/a' }
        $costText = if ($null -ne $armResult.costPerSuccessUsd) { '$' + ('{0:N4}' -f $armResult.costPerSuccessUsd) } else { 'not reported' }
        $wallText = if ($null -ne $armResult.meanWallMs) { ('{0:N1}s' -f ($armResult.meanWallMs / 1000)) } else { 'n/a' }
        $report.Add("| $($armResult.id) | $($armResult.host) / $($armResult.model) | $($armResult.authMode) | $($armResult.meanPrimaryScore) | $passAtOneText | $passKText | $costText | $wallText | $($armResult.meanTurns) | $($armResult.meanToolCalls) | $($armResult.quarantined) |")
    }
    $report.Add('')
    $report.Add('pass@1 is the share of tasks passed on the first trial. pass^k is the share passed on every requested trial. Only clean trials are included in those rates and paired estimates.')
    $report.Add('')
    $report.Add('## Paired results by task')
    $report.Add('')
    $report.Add('| Task | Set / language | Primary grader | A mean | B mean | B − A | Paired trials |')
    $report.Add('|---|---|---|---:|---:|---:|---:|')
    foreach ($task in $selectedTasks) {
        $rows = @($paired | Where-Object { $_.task -eq $task.id })
        $aMean = Get-ABMean @($rows | ForEach-Object { [double]$_.armA })
        $bMean = Get-ABMean @($rows | ForEach-Object { [double]$_.armB })
        $dMean = Get-ABMean @($rows | ForEach-Object { [double]$_.difference })
        $languagePath = Join-Path $task.setPath 'language/language.yaml'
        $lang = if (Test-Path -LiteralPath $languagePath) { (Read-ABJson $languagePath).id } else { $task.set }
        $taskMetrics = @($completed | Where-Object { $_.task -eq $task.id } | ForEach-Object {
            if ($_.Contains('primaryMetric')) { $_.primaryMetric } else { $primaryMetric }
        } | Sort-Object -Unique) -join ', '
        $report.Add("| $($task.id) | $($task.set) / $lang | $taskMetrics | $aMean | $bMean | $dMean | $($rows.Count) |")
    }
    $report.Add('')
    $report.Add("Mean paired difference (B − A): $meanText. 95% interval: $intervalText.")
    $report.Add('')
    $report.Add('## Tool errors and refusals')
    $report.Add('')
    foreach ($armResult in $armSummary) {
        $report.Add("### $($armResult.id)")
        $report.Add('')
        $report.Add('Tool errors: ' + $(if ($armResult.topToolErrors.Count) { ($armResult.topToolErrors | ForEach-Object { "$($_.tool) ($($_.count))" }) -join ', ' } else { 'none' }))
        $report.Add('Refusals: ' + $(if ($armResult.topRefusals.Count) { ($armResult.topRefusals | ForEach-Object { "$($_.code) ($($_.count))" }) -join ', ' } else { 'none' }))
        $report.Add('')
    }
    $report.Add('## Failures')
    $report.Add('')
    if ($failureExcerpts.Count -eq 0) { $report.Add('No task failures.') }
    else {
        foreach ($failure in $failureExcerpts) {
            $report.Add("### $($failure.task) — $($failure.arm), trial $($failure.trial) ($($failure.status))")
            $report.Add('')
            $report.Add('Last tool calls:')
            $report.Add('')
            if ($failure.lastToolCalls.Count -eq 0) { $report.Add('- No tool calls recorded.') }
            else {
                foreach ($call in $failure.lastToolCalls) {
                    $json = $call | ConvertTo-Json -Depth 20 -Compress
                    $report.Add('- `' + $json.Replace('`', '\`') + '`')
                }
            }
            $report.Add('')
            $report.Add('Final message:')
            $report.Add('')
            $report.Add('> ' + ([string]$failure.finalMessage -replace "`r?`n", "`n> "))
            $report.Add('')
        }
    }

    $reportPath = Join-Path $runRoot 'report.md'
    Set-Content -LiteralPath $reportPath -Value ($report -join "`n") -Encoding utf8
    $summary = [ordered]@{
        runId = $runId
        question = [string]$questionData.question
        questionId = $questionId
        integrity = $integritySummary
        grading = @{ infrastructureFailures = $gradingFailures; trialsWithJudgeDisagreement = $judgeSplits }
        arms = @($armSummary)
        tasks = @($selectedTasks | ForEach-Object { [ordered]@{ id = $_.id; set = $_.set; family = $_.family; tier = $_.tier } })
        primaryMetric = $primaryMetric
        meanPairedDifferenceBMinusA = $meanDifference
        confidenceInterval95 = $interval
        enoughEvidence = -not $intervalCrossesZero
        pairs = @($paired)
        trials = @($completed | ForEach-Object {
            [ordered]@{
                task = $_.task; set = $_.set; arm = $_.arm; trial = $_.trial; status = $_.status
                authMode = $_.authMode
                integrity = $_.integrity
                infrastructureInvalid = $_.infrastructureInvalid; grade = $_.grade; success = $_.success
                gradingState = if ($_.Contains('gradingState')) { $_.gradingState } else { 'unscored' }
                judgeDisagreements = if ($_.Contains('judgeDisagreements')) { $_.judgeDisagreements } else { 0 }
                primaryMetric = if ($_.Contains('primaryMetric')) { $_.primaryMetric } else { $primaryMetric }
                primaryScore = $_.primaryScore; operationCount = $_.operationCount; wallMs = $_.wallMs
                turns = $_.turns; toolCalls = $_.toolCalls; toolErrors = $_.toolErrors
                timeToFirstProposalMs = $_.timeToFirstProposalMs; inputTokens = $_.inputTokens
                outputTokens = $_.outputTokens; costUsd = $_.costUsd
                files = [ordered]@{
                    manifest = (Join-Path $_.trialRoot 'manifest.json')
                    transcript = (Join-Path $_.trialRoot 'transcript.jsonl')
                    activity = (Join-Path $_.trialRoot 'activity.jsonl')
                    proposals = (Join-Path $_.trialRoot 'proposals.json')
                    grade = (Join-Path $_.trialRoot 'grade.json')
                }
            }
        })
        failures = @($failureExcerpts)
        reportPath = $reportPath
    }
    Write-ABJson (Join-Path $runRoot 'summary.json') $summary
    # A re-grade directory holds grades only; its transcripts stay in the source run one level up.
    $transcripts = if (@(Get-ChildItem -Path (Join-Path $runRoot 'trials/*/*/trial-*/transcript.jsonl') -ErrorAction SilentlyContinue).Count) {
        $runRoot } else { Split-Path $runRoot }
    $measure = Invoke-ABProcess (Get-Command python3).Source @((Join-Path $PSScriptRoot 'MeasureRun.py'), $transcripts, '--grades', $runRoot) $runRoot @{} 600000
    $metricsPath = Join-Path $runRoot 'metrics.md'
    if ($measure.ExitCode -eq 0 -and (Test-Path -LiteralPath $metricsPath)) {
        Add-Content -LiteralPath $reportPath -Value ("`n" + (Get-Content -LiteralPath $metricsPath -Raw).Replace('# Cost and effort', '## Cost and effort'))
    } else { Write-Warning "Cost and effort measures were not written: $($measure.Stderr)" }
    Write-Host "Report: $reportPath"
    Write-Host "Summary: $(Join-Path $runRoot 'summary.json')"

}
Export-ModuleMember -Function Write-ABReport
