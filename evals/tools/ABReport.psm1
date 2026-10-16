Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ABHarness.psm1')
function Get-ABMean([double[]] $Values) {
    if ($Values.Count -eq 0) { return $null }
    return [double](($Values | Measure-Object -Average).Average)
}

function Get-ABPairedInterval([object[]] $Pairs) {
    $clusterGroups = @($Pairs | Group-Object { $_.set + '|' + $_.language })
    if ($Pairs.Count -eq 0) {
        return [ordered]@{ mean = $null; confidenceInterval95 = $null; intervalIncludesZero = $null; unit = $null; n = 0 }
    }
    $unitValues = [Collections.Generic.List[double]]::new()
    $unitName = 'task means'
    if ($clusterGroups.Count -gt 1) {
        $unitName = 'set/language cluster means'
        foreach ($clusterGroup in $clusterGroups) {
            $unitValues.Add((Get-ABMean @($clusterGroup.Group | ForEach-Object { [double]$_.difference })))
        }
    } else {
        foreach ($taskGroup in @($Pairs | Group-Object { [string]$_.task })) {
            $unitValues.Add((Get-ABMean @($taskGroup.Group | ForEach-Object { [double]$_.difference })))
        }
    }
    $count = $unitValues.Count
    $mean = Get-ABMean @($unitValues)
    if ($count -lt 2) {
        return [ordered]@{ mean = $mean; confidenceInterval95 = $null; intervalIncludesZero = $null; unit = $unitName; n = $count }
    }
    $sumSquares = 0.0
    foreach ($unitValue in $unitValues) { $sumSquares += [Math]::Pow($unitValue - $mean, 2) }
    $standardError = [Math]::Sqrt(($sumSquares / ($count - 1)) / $count)
    $degreesOfFreedom = $count - 1
    $criticalValues = @{
        1 = 12.706; 2 = 4.303; 3 = 3.182; 4 = 2.776; 5 = 2.571; 6 = 2.447; 7 = 2.365; 8 = 2.306
        9 = 2.262; 10 = 2.228; 11 = 2.201; 12 = 2.179; 13 = 2.160; 14 = 2.145; 15 = 2.131
        16 = 2.120; 17 = 2.110; 18 = 2.101; 19 = 2.093; 20 = 2.086; 25 = 2.060; 30 = 2.042
        40 = 2.021; 60 = 2.000; 120 = 1.980
    }
    $tableDf = 1
    foreach ($key in $criticalValues.Keys | Sort-Object { [int]$_ }) {
        if ([int]$key -gt $degreesOfFreedom) { break }
        $tableDf = [int]$key
    }
    $halfWidth = [double]$criticalValues[$tableDf] * $standardError
    $interval = [ordered]@{
        lower = $mean - $halfWidth
        upper = $mean + $halfWidth
        method = "Student-t 95% interval over $unitName (n = $count)."
    }
    return [ordered]@{ mean = $mean; confidenceInterval95 = $interval;
        intervalIncludesZero = $interval.lower -le 0 -and $interval.upper -ge 0; unit = $unitName; n = $count }
}

function Test-ABScoredRow([object] $Row) {
    $failureClass = if ($Row.Contains('failureClass')) { [string]$Row.failureClass } else { '' }
    return $Row.integrity.state -eq 'clean' -and $null -ne $Row.primaryScore -and
        $failureClass -notin @('cloud_failure', 'harness_defect')
}

function Get-ABCommonEligibleTasks([object[]] $Completed, [object[]] $Arms, [int] $TrialCount) {
    # A task counts toward pass rates only when every Arm has every trial scored and decided, so Arms share one task set.
    $eligible = [Collections.Generic.List[string]]::new()
    foreach ($taskGroup in @($Completed | Group-Object { [string]$_.task })) {
        $complete = $true
        foreach ($arm in $Arms) {
            $rows = @($taskGroup.Group | Where-Object { $_.arm -eq $arm.id })
            if ($rows.Count -ne $TrialCount -or
                @($rows | Where-Object { -not (Test-ABScoredRow $_) -or $null -eq $_.success }).Count -gt 0) {
                $complete = $false
            }
        }
        if ($complete) { $eligible.Add([string]$taskGroup.Name) }
    }
    return @($eligible)
}

function Write-ABReport {
    param($completed, $selectedTasks, $arms, $trialCount, $primaryMetric, $questionData, $questionId, $runId, $runRoot,
        [string] $measurementFingerprint = '', [object] $hostLoad = $null)
    $paired = [Collections.Generic.List[object]]::new()
    foreach ($task in $selectedTasks) {
        for ($trial = 1; $trial -le $trialCount; $trial++) {
            $left = $completed | Where-Object { $_.task -eq $task.id -and $_.arm -eq $arms[0].id -and $_.trial -eq $trial } | Select-Object -First 1
            $right = $completed | Where-Object { $_.task -eq $task.id -and $_.arm -eq $arms[1].id -and $_.trial -eq $trial } | Select-Object -First 1
            if ($left -and $right -and (Test-ABScoredRow $left) -and (Test-ABScoredRow $right)) {
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
    $intervalResult = Get-ABPairedInterval @($paired)
    $meanDifference = $intervalResult.mean
    $interval = $intervalResult.confidenceInterval95

    $commonTasks = @(Get-ABCommonEligibleTasks @($completed) @($arms) $trialCount)
    $armSummary = [Collections.Generic.List[object]]::new()
    foreach ($arm in $arms) {
        $armRows = @($completed | Where-Object { $_.arm -eq $arm.id })
        $validRows = @($armRows | Where-Object { Test-ABScoredRow $_ })
        # Rows whose success is undecided (a capped parser) are inconclusive, not failures, in rates and prices.
        $decidedRows = @($validRows | Where-Object { $null -ne $_.success })
        $eligibleTasks = $commonTasks.Count
        $passedAtOne = 0
        $passedAll = 0
        foreach ($taskId in $commonTasks) {
            $taskRows = @($armRows | Where-Object { $_.task -eq $taskId } | Sort-Object trial)
            if ($taskRows[0].success -eq $true) { $passedAtOne++ }
            if (@($taskRows | Where-Object { $_.success -ne $true }).Count -eq 0) { $passedAll++ }
        }
        $successfulCount = @($decidedRows | Where-Object { $_.success -eq $true }).Count
        $costs = @($decidedRows | Where-Object { $null -ne $_.costUsd } | ForEach-Object { [double]$_.costUsd })
        $completeCost = $costs.Count -eq $decidedRows.Count
        $unpricedRetries = [int](@($decidedRows | ForEach-Object {
            if ($_.Contains('unpricedRetryAttempts')) { [int]$_.unpricedRetryAttempts } else { 0 } }) | Measure-Object -Sum).Sum
        $totalScoredCost = if ($completeCost -and $costs.Count -gt 0) { [double](($costs | Measure-Object -Sum).Sum) } else { $null }
        $priceToSolve = if ($null -ne $totalScoredCost -and $successfulCount -gt 0) {
            $totalScoredCost / $successfulCount
        } else { $null }
        $averageEpisodeCost = if ($null -ne $totalScoredCost -and $decidedRows.Count -gt 0) {
            $totalScoredCost / $decidedRows.Count
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
            priceToSolveUsd = $priceToSolve
            averageCostPerEpisodeUsd = $averageEpisodeCost
            costCoverage = @{ scoredEpisodes = $decidedRows.Count; pricedEpisodes = $costs.Count; complete = $completeCost
                unpricedRetryAttempts = $unpricedRetries }
            successfulTasksAtK = $passedAll; eligibleTasks = $eligibleTasks
            harnessDefects = @($armRows | Where-Object { $_.failureClass -eq 'harness_defect' }).Count
            cloudFailures = @($armRows | Where-Object { $_.failureClass -eq 'cloud_failure' }).Count
            quarantined = @($armRows | Where-Object { $_.integrity.state -ne 'clean' }).Count
            meanWallMs = Get-ABMean @($validRows | ForEach-Object { [double]$_.wallMs })
            meanTurns = Get-ABMean @($validRows | ForEach-Object { [double]$_.turns })
            meanToolCalls = Get-ABMean @($validRows | ForEach-Object { [double]$_.toolCalls })
            meanOperationCount = Get-ABMean @($validRows | ForEach-Object { [double]$_.operationCount })
            meanInputTokens = Get-ABMean @($validRows | Where-Object { $null -ne $_.inputTokens } | ForEach-Object { [double]$_.inputTokens })
            meanOutputTokens = Get-ABMean @($validRows | Where-Object { $null -ne $_.outputTokens } | ForEach-Object { [double]$_.outputTokens })
            topToolErrors = $errorCounts; topRefusals = $refusalCounts
        })
    }

    $failureExcerpts = [Collections.Generic.List[object]]::new()
    foreach ($row in $completed | Where-Object { $_.failureClass -or $_.success -ne $true -or
        ($_.Contains('cloudRetries') -and $_.cloudRetries -gt 0) }) {
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
            failureClass = $row.failureClass
            failureEvidence = if ($row.Contains('failureEvidence')) { @($row.failureEvidence) } else { @() }
            cloudRetries = if ($row.Contains('cloudRetries')) { [int]$row.cloudRetries } else { 0 }
            cloudRetryEvidence = if ($row.Contains('cloudRetryEvidence')) { @($row.cloudRetryEvidence) } else { @() }
            lastToolCalls = $activityTail
            finalMessage = if ($hostData -is [Collections.IDictionary] -and $hostData.Contains('finalMessage')) {
                $hostData.finalMessage
            } else { $null }
        })
    }

    $isAA = $questionData.Contains('mode') -and [string]$questionData.mode -ceq 'aa'
    $intervalCrossesZero = $null -eq $interval -or ($interval.lower -le 0 -and $interval.upper -ge 0)
    $leftLabel = [string]$arms[0].id
    $rightLabel = [string]$arms[1].id
    $aaArm = if ($questionData.Contains('aaArm') -and $questionData.aaArm) {
        [string]$questionData.aaArm
    } else { $leftLabel -replace '\.a1$', '' }
    $meanText = if ($null -eq $meanDifference) { 'unavailable' } else { '{0:P1}' -f $meanDifference }
    $intervalText = if ($interval) { '[{0:P1}, {1:P1}]' -f $interval.lower, $interval.upper } else { '[unavailable]' }
    $aaMetricDifferences = $null
    if ($isAA) {
        $aaMetricDifferences = [ordered]@{
            passAt1 = if ($null -ne $armSummary[0].passAt1 -and $null -ne $armSummary[1].passAt1) {
                [double]$armSummary[1].passAt1 - [double]$armSummary[0].passAt1
            } else { $null }
            meanWallMs = if ($null -ne $armSummary[0].meanWallMs -and $null -ne $armSummary[1].meanWallMs) {
                [double]$armSummary[1].meanWallMs - [double]$armSummary[0].meanWallMs
            } else { $null }
            meanToolCalls = if ($null -ne $armSummary[0].meanToolCalls -and $null -ne $armSummary[1].meanToolCalls) {
                [double]$armSummary[1].meanToolCalls - [double]$armSummary[0].meanToolCalls
            } else { $null }
        }
        $verdict = "A/A check of ${aaArm}: two identical copies. Paired difference $meanText, 95% interval $intervalText."
        if ($null -eq $interval) {
            $verdict += ' The interval is unavailable because fewer than two independent units were scored.'
        } elseif ($intervalResult.intervalIncludesZero) {
            $verdict += ' The interval includes zero: the harness shows no difference where none exists.'
        } else {
            $verdict += ' The interval excludes zero: the harness reports a difference between identical Arms; do not trust A/B results until this is explained.'
        }
    } elseif ($null -eq $interval) {
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
    if ($isAA) {
        $passAt1DifferenceText = if ($null -ne $aaMetricDifferences.passAt1) { '{0:N4}' -f $aaMetricDifferences.passAt1 } else { 'unavailable' }
        $wallDifferenceText = if ($null -ne $aaMetricDifferences.meanWallMs) { '{0:N1} ms' -f $aaMetricDifferences.meanWallMs } else { 'unavailable' }
        $toolDifferenceText = if ($null -ne $aaMetricDifferences.meanToolCalls) { '{0:N2}' -f $aaMetricDifferences.meanToolCalls } else { 'unavailable' }
        $report.Add("A/A metric differences (copy 2 minus copy 1): pass@1 $passAt1DifferenceText; mean wall time $wallDifferenceText; mean tool calls $toolDifferenceText.")
        $report.Add('')
    }
    $report.Add("Question: $($questionData.question)")
    $report.Add("Run: $runId | primary metric: $primaryMetric | trials per task: $trialCount | paired trials: $($paired.Count) | inconclusive or invalid trials: $incompleteCount")
    if ($hostLoad) { $report.Add("Host 5-minute load: $($hostLoad.start) at start, $($hostLoad.end) at end. Time is not an Agent budget, so load changes time, not scores.") }
    $report.Add('')
    $integritySummary = Get-ABIntegritySummary @($completed)
    $report.Add('## Integrity')
    $report.Add('')
    $report.Add("Attempted Episodes: $($integritySummary.attempted). Scorable: $($integritySummary.scorable). Integrity states — clean: $($integritySummary.counts.clean); review: $($integritySummary.counts.review); invalid: $($integritySummary.counts.invalid); isolation_failure: $($integritySummary.counts.isolation_failure).")
    $report.Add("Failure classes — Agent: $($integritySummary.failureClasses.agent_failure); Cloud: $($integritySummary.failureClasses.cloud_failure); Harness: $($integritySummary.failureClasses.harness_defect).")
    if ($integritySummary.confidenceInterval95) {
        $report.Add(('Scorable / attempted: {0:P1}, Wilson 95% interval [{1:P1}, {2:P1}].' -f $integritySummary.scorableRate, $integritySummary.confidenceInterval95.lower, $integritySummary.confidenceInterval95.upper))
    }
    $gradingFailures = @($completed | Where-Object { $_.failureClass -eq 'harness_defect' }).Count
    $judgeSplits = @($completed | Where-Object { $_.Contains('judgeDisagreements') -and $_.judgeDisagreements -gt 0 }).Count
    $report.Add("Control-side grading failures: $gradingFailures. Trials with a recorded judge split: $judgeSplits.")
    $report.Add('Agent failures are scored. Cloud failures are retried and excluded from agent scores. Harness defects are excluded until fixed. Evaluation awareness is recorded without penalty.')
    foreach ($row in $completed | Where-Object { $_.integrity.state -ne 'clean' }) {
        $reasons = ($row.integrity.reasons | ForEach-Object { $_.reason }) -join '; '
        $report.Add("- $($row.task) / $($row.arm) / $($row.trial): $($row.integrity.state) — $reasons")
    }
    $report.Add('')
    $report.Add('## Arms')
    $report.Add('')
    $report.Add('| Arm | Host / model | Auth mode | Mean primary score | pass@1 | pass^k | Price to solve | Average cost / Episode | Mean wall time | Mean turns | Mean tool calls | Operations / Episode | Quarantined trials |')
    $report.Add('|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
    foreach ($armResult in $armSummary) {
        $passAtOneText = if ($null -ne $armResult.passAt1) { '{0:P1}' -f $armResult.passAt1 } else { 'n/a' }
        $passKText = if ($null -ne $armResult.passPowerK) { '{0:P1}' -f $armResult.passPowerK } else { 'n/a' }
        $priceText = if ($null -ne $armResult.priceToSolveUsd) { '$' + ('{0:N4}' -f $armResult.priceToSolveUsd) }
            elseif (-not $armResult.costCoverage.complete) { 'incomplete costs' } else { 'no solves' }
        $averageCostText = if ($null -ne $armResult.averageCostPerEpisodeUsd) { '$' + ('{0:N4}' -f $armResult.averageCostPerEpisodeUsd) }
            elseif (-not $armResult.costCoverage.complete) { 'incomplete costs' } else { 'not reported' }
        $wallText = if ($null -ne $armResult.meanWallMs) { ('{0:N1}s' -f ($armResult.meanWallMs / 1000)) } else { 'n/a' }
        $report.Add("| $($armResult.id) | $($armResult.host) / $($armResult.model) | $($armResult.authMode) | $($armResult.meanPrimaryScore) | $passAtOneText | $passKText | $priceText | $averageCostText | $wallText | $($armResult.meanTurns) | $($armResult.meanToolCalls) | $($armResult.meanOperationCount) | $($armResult.quarantined) |")
    }
    $report.Add('')
    $report.Add("pass@1 is the share of tasks passed on the first trial. pass^k is the share passed on every requested trial. Both use the $($commonTasks.Count) tasks every Arm has scored and decided on every trial, so the Arms share one task set; paired estimates use every clean scored pair.")
    $unpricedTotal = [int](@($armSummary | ForEach-Object { [int]$_.costCoverage.unpricedRetryAttempts }) | Measure-Object -Sum).Sum
    if ($unpricedTotal -gt 0) {
        $report.Add("Prices include every recorded Cloud retry cost; $unpricedTotal retried attempts reported no cost, so those prices are lower bounds.")
    }
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
            if ($failure.failureClass) { $report.Add("Failure class: $($failure.failureClass).") }
            foreach ($evidence in $failure.failureEvidence) { $report.Add('- Evidence: ' + [string]$evidence) }
            if ($failure.cloudRetries -gt 0) {
                $report.Add("Cloud retries: $($failure.cloudRetries).")
                foreach ($retry in $failure.cloudRetryEvidence) {
                    $report.Add('- Retry evidence: ' + (@($retry.evidence) -join '; '))
                }
            }
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
        measurementFingerprint = $measurementFingerprint
        hostLoad = $hostLoad
        question = [string]$questionData.question
        questionId = $questionId
        mode = if ($isAA) { 'aa' } else { 'ab' }
        aa = if ($isAA) { [ordered]@{
            arm = $aaArm
            intervalIncludesZero = $intervalResult.intervalIncludesZero
            passAt1Difference = $aaMetricDifferences.passAt1
            meanWallMsDifference = $aaMetricDifferences.meanWallMs
            meanToolCallsDifference = $aaMetricDifferences.meanToolCalls
        } } else { $null }
        judgePolicy = if ($questionData.Contains('judge_policy')) { [string]$questionData.judge_policy } else { 'paired' }
        testGrammars = Get-ABTestGrammarsPin (Split-Path -Parent $PSScriptRoot)
        integrity = $integritySummary
        grading = @{ harnessDefects = $gradingFailures; trialsWithJudgeDisagreement = $judgeSplits;
            cloudFailures = @($completed | Where-Object { $_.failureClass -eq 'cloud_failure' }).Count }
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
                failureClass = if ($_.Contains('failureClass')) { $_.failureClass } else { $null }
                failureEvidence = if ($_.Contains('failureEvidence')) { $_.failureEvidence } else { @() }
                measurementFingerprint = if ($_.Contains('measurementFingerprint')) { $_.measurementFingerprint } else { $measurementFingerprint }
                armFingerprint = if ($_.Contains('armFingerprint')) { $_.armFingerprint } else { $null }
                judgeFamilies = @(if ($_.Contains('judgeFamilies')) { $_.judgeFamilies })
                judgeVerdict = if ($_.Contains('judgeVerdict')) { $_.judgeVerdict } else { $null }
                cloudRetries = if ($_.Contains('cloudRetries')) { $_.cloudRetries } else { 0 }
                cloudRetryEvidence = if ($_.Contains('cloudRetryEvidence')) { $_.cloudRetryEvidence } else { @() }
                grade = $_.grade; success = $_.success
                gradingState = if ($_.Contains('gradingState')) { $_.gradingState } else { 'unscored' }
                judgeDisagreements = if ($_.Contains('judgeDisagreements')) { $_.judgeDisagreements } else { 0 }
                primaryMetric = if ($_.Contains('primaryMetric')) { $_.primaryMetric } else { $primaryMetric }
                primaryScore = $_.primaryScore; operationCount = $_.operationCount; wallMs = $_.wallMs
                turns = $_.turns; toolCalls = $_.toolCalls; toolErrors = $_.toolErrors
                timeToFirstProposalMs = $_.timeToFirstProposalMs; inputTokens = $_.inputTokens
                outputTokens = $_.outputTokens; costUsd = $_.costUsd
                agentBudget = if ($_.Contains('agentBudget')) { $_.agentBudget } else { $null }
                agentToolCalls = if ($_.Contains('agentToolCalls')) { $_.agentToolCalls } else { $null }
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
Export-ModuleMember -Function Get-ABCommonEligibleTasks, Get-ABPairedInterval, Test-ABScoredRow, Write-ABReport
