Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ABRouteHealthResult([object] $Summary, [object[]] $Routes) {
    $routeResults = [Collections.Generic.List[object]]::new()
    foreach ($route in $Routes) {
        $rows = @($Summary.trials | Where-Object { $_.arm -eq $route.id })
        $tasks = @($rows | ForEach-Object { [string]$_.task } | Sort-Object -Unique)
        $reasons = [Collections.Generic.List[string]]::new()
        if ([string]$Summary.measurementFingerprint -notmatch '^[0-9a-f]{64}$') {
            $reasons.Add('the run is missing its measurement fingerprint')
        }
        if ($rows.Count -ne [int]$route.episodes) { $reasons.Add("expected $($route.episodes) Episodes, found $($rows.Count)") }
        if ($tasks.Count -ne [int]$route.episodes) { $reasons.Add("expected $($route.episodes) distinct tasks, found $($tasks.Count)") }
        $observedTimes = [Collections.Generic.List[double]]::new()
        foreach ($row in $rows) {
            if ($row.authMode -cne [string]$route.authMode) { $reasons.Add("$($row.task) did not record billing mode $($route.authMode)") }
            if ($row.failureClass -eq 'cloud_failure' -or [int]$row.cloudRetries -gt 0 -or $row.status -eq 'cloud_failure') {
                $reasons.Add("$($row.task) had a Cloud failure or retry")
            }
            $network = if ($row.integrity.Contains('network')) { $row.integrity.network } else { @{} }
            if (-not $network.allowedHostsOnly -or @($network.observedHosts).Count -eq 0 -or
                [string]$route.allowedHost -notin @($network.allowedHosts)) {
                $reasons.Add("$($row.task) has incomplete network allow-list evidence")
            }
            if (@($network.observedHosts | Where-Object { $_ -notin @($network.allowedHosts) }).Count -gt 0 -or
                [string]$route.allowedHost -notin @($network.observedHosts)) {
                $reasons.Add("$($row.task) did not show only the expected model-service host")
            }
            $registration = if ($row.integrity.Contains('mcpRegistration')) { $row.integrity.mcpRegistration } else { @{} }
            if (-not $registration.fullToolListSeen) { $reasons.Add("$($row.task) did not record the full MCP tool list") }
            if ($null -eq $row.wallMs -or [double]$row.wallMs -le 0) { $reasons.Add("$($row.task) has no positive Episode time") }
            else { $observedTimes.Add([double]$row.wallMs) }
        }
        $times = @($observedTimes | Sort-Object)
        $median = if ($times.Count -eq 0) { $null }
            elseif ($times.Count % 2) { $times[[int][Math]::Floor($times.Count / 2)] }
            else { ($times[$times.Count / 2 - 1] + $times[$times.Count / 2]) / 2.0 }
        $routeResults.Add([ordered]@{
            arm = [string]$route.id; authMode = [string]$route.authMode; expectedHost = [string]$route.allowedHost
            episodes = $rows.Count; tasks = $tasks
            cloudFailures = @($rows | Where-Object { $_.failureClass -eq 'cloud_failure' -or $_.status -eq 'cloud_failure' }).Count
            cloudRetries = [int](($rows | ForEach-Object { [int]$_.cloudRetries } | Measure-Object -Sum).Sum)
            medianEpisodeMs = $median; passed = $reasons.Count -eq 0; reasons = @($reasons)
        })
    }
    $preferred = $routeResults | Where-Object { $_.authMode -eq 'claude-plan' } | Select-Object -First 1
    $fallback = $routeResults | Where-Object { $_.authMode -eq 'chatgpt-plan' } | Select-Object -First 1
    $selected = $null
    $fallbackReason = $null
    if ($preferred -and $preferred.passed) { $selected = $preferred.arm }
    elseif ($fallback -and $fallback.passed) {
        $selected = $fallback.arm
        $fallbackReason = if ($preferred) { @($preferred.reasons) -join '; ' } else { 'The preferred Claude route was not included.' }
    }
    return [ordered]@{
        schema = 'motif-agent-route-health/v1'; measurementFingerprint = [string]$Summary.measurementFingerprint
        passed = $null -ne $selected; selectedRoute = $selected; fallbackReason = $fallbackReason
        routes = @($routeResults); checkedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }
}

function Get-ABAcceptanceResult([object] $Screen, [object] $Full, [object] $Confirmation,
    [object] $Baseline, [object] $Evidence, [string] $CandidateArm, [string] $BaselineArm) {
    $reasons = [Collections.Generic.List[string]]::new()
    $splits = 0
    $disputedEpisodes = [Collections.Generic.List[string]]::new()
    foreach ($summary in @($Screen, $Full, $Confirmation, $Baseline)) {
        if (-not $summary -or [string]$summary.measurementFingerprint -notmatch '^[0-9a-f]{64}$') {
            $reasons.Add('A required summary is missing its measurement fingerprint.')
        }
        if ($summary -and $summary.Contains('trials')) {
            foreach ($trial in $summary.trials) {
                $summaryId = if ($summary.Contains('runId')) { [string]$summary.runId } else { 'unknown run' }
                if (-not $trial.Contains('measurementFingerprint') -or
                    [string]$trial.measurementFingerprint -cne [string]$summary.measurementFingerprint) {
                    $reasons.Add("$summaryId / $($trial.task) / $($trial.arm) has a mismatched measurement fingerprint.")
                }
                if (-not $trial.Contains('armFingerprint') -or [string]$trial.armFingerprint -notmatch '^[0-9a-f]{64}$') {
                    $reasons.Add("$summaryId / $($trial.task) / $($trial.arm) is missing its Arm fingerprint.")
                }
            }
        }
        if ($summary -and $summary.Contains('grading') -and $summary.grading.Contains('trialsWithJudgeDisagreement')) {
            $splits += [int]$summary.grading.trialsWithJudgeDisagreement
        }
        if ($summary -and $summary.Contains('trials')) {
            foreach ($trial in $summary.trials | Where-Object { $_.Contains('judgeDisagreements') -and [int]$_.judgeDisagreements -gt 0 }) {
                if (-not $summary.Contains('runId') -or -not $summary.runId) { continue }
                $disputedEpisodes.Add("$($summary.runId)/$($trial.task)/$($trial.arm)/$($trial.trial)")
            }
        }
    }
    $rulings = @(if ($Evidence.Contains('judgeRulings')) { $Evidence.judgeRulings })
    $rulingEpisodes = @($rulings | ForEach-Object { [string]$_.episodeId } | Where-Object { $_ } | Sort-Object -Unique)
    $expectedRulings = @($disputedEpisodes | Sort-Object -Unique)
    $validRulings = @($rulings | Where-Object {
        $_.Contains('episodeId') -and [string]$_.episodeId -and $_.Contains('person') -and [string]$_.person -and
        $_.Contains('ruling') -and [string]$_.ruling -in @('pass', 'fail') -and
        $_.Contains('rationale') -and [string]$_.rationale
    })
    $disputesResolved = $splits -eq 0 -or ($disputedEpisodes.Count -eq $splits -and $rulings.Count -eq $splits -and
        $validRulings.Count -eq $splits -and $rulingEpisodes.Count -eq $splits -and
        @($rulingEpisodes | Where-Object { $_ -notin $expectedRulings }).Count -eq 0)
    $valid = $reasons.Count -eq 0 -and $Evidence.Contains('frozenKeysVerified') -and $Evidence.frozenKeysVerified -eq $true -and
        $Evidence.Contains('matchedConditions') -and $Evidence.matchedConditions -eq $true -and $disputesResolved
    $violations = @(if ($Evidence.Contains('criticalContractViolations')) { $Evidence.criticalContractViolations } else { 'evidence missing' })
    $contractClean = $violations.Count -eq 0

    $affected = @(if ($Evidence.Contains('affectedTasks')) { $Evidence.affectedTasks | ForEach-Object { [string]$_ } | Sort-Object -Unique })
    $candidateConfirmation = @($Confirmation.trials | Where-Object { $_.arm -eq $CandidateArm })
    $baselineConfirmation = @($Confirmation.trials | Where-Object { $_.arm -eq $BaselineArm })
    $confirmationScorable = @($candidateConfirmation | Where-Object { $_.integrity.state -eq 'clean' -and
        $null -ne $_.primaryScore -and $_.gradingState -ne 'judge_disagreement' })
    $taskCounts = @($Confirmation.trials | Group-Object { [string]$_.arm + '|' + [string]$_.task })
    $withinConfirmationCap = @($taskCounts | Where-Object { $_.Count -gt 10 }).Count -eq 0
    $freshTasks = @($confirmationScorable | ForEach-Object { [string]$_.task } | Sort-Object -Unique)
    $hasTwoTasks = @($affected | Where-Object { $_ -in $freshTasks }).Count -ge 2
    $hasOriginalAndCounterexample = $Evidence.Contains('originalTask') -and $Evidence.Contains('newCounterexample') -and
        ([string]$Evidence.originalTask -in $freshTasks) -and ([string]$Evidence.newCounterexample -in $freshTasks)
    $baselineConfirmationScorable = @($baselineConfirmation | Where-Object { $_.integrity.state -eq 'clean' -and
        $null -ne $_.primaryScore -and $_.gradingState -ne 'judge_disagreement' })
    $freshGate = $candidateConfirmation.Count -eq 5 -and $confirmationScorable.Count -eq 5 -and
        $baselineConfirmation.Count -eq 5 -and $baselineConfirmationScorable.Count -eq 5 -and
        $withinConfirmationCap -and ($hasTwoTasks -or $hasOriginalAndCounterexample)

    $explained = if ($Evidence.Contains('explainedRegressions')) { @($Evidence.explainedRegressions | ForEach-Object { [string]$_ }) } else { @() }
    $regressions = [Collections.Generic.List[string]]::new()
    $baselineTaskGroups = @($Baseline.trials | Where-Object { $_.arm -eq $BaselineArm } | Group-Object { [string]$_.task })
    foreach ($priorGroup in $baselineTaskGroups) {
        $priorRows = @($priorGroup.Group | Where-Object { $_.integrity.state -eq 'clean' -and
            $null -ne $_.primaryScore -and $_.gradingState -ne 'judge_disagreement' })
        if ($priorRows.Count -eq 0) { continue }
        $currentRows = @($Full.trials | Where-Object { $_.arm -eq $CandidateArm -and $_.task -eq $priorGroup.Name })
        $currentScorable = @($currentRows | Where-Object { $_.integrity.state -eq 'clean' -and
            $null -ne $_.primaryScore -and $_.gradingState -ne 'judge_disagreement' })
        if ($currentScorable.Count -ne $priorRows.Count) {
            $regressions.Add(([string]$priorGroup.Name))
            continue
        }
        $priorScore = [double](($priorRows | ForEach-Object { [double]$_.primaryScore } | Measure-Object -Average).Average)
        $currentScore = [double](($currentScorable | ForEach-Object { [double]$_.primaryScore } | Measure-Object -Average).Average)
        if ($currentScore -lt $priorScore) { $regressions.Add(([string]$priorGroup.Name)) }
    }
    $unexplained = @($regressions | Where-Object { $_ -notin $explained })
    $servedModels = @(if ($Evidence.Contains('servedModels')) { $Evidence.servedModels | ForEach-Object { [string]$_ } })
    $fullModels = @($Full.arms | ForEach-Object { [string]$_.model } | Where-Object { $_ } | Sort-Object -Unique)
    $modelsCovered = $servedModels.Count -gt 0 -and $servedModels.Count -eq $fullModels.Count -and
        @($servedModels | Where-Object { $_ -notin $fullModels }).Count -eq 0
    $fullGate = $unexplained.Count -eq 0 -and $modelsCovered

    $changeType = if ($Evidence.Contains('changeType')) { [string]$Evidence.changeType } else { '' }
    $gain = $false
    if ($changeType -eq 'correctness') {
        foreach ($taskId in @($affected + @($(if ($Evidence.Contains('originalTask')) { $Evidence.originalTask }))) | Sort-Object -Unique) {
            $old = @($Baseline.trials | Where-Object { $_.arm -eq $BaselineArm -and $_.task -eq $taskId -and
                $_.integrity.state -eq 'clean' -and $null -ne $_.primaryScore })
            $new = @($Full.trials | Where-Object { $_.arm -eq $CandidateArm -and $_.task -eq $taskId -and
                $_.integrity.state -eq 'clean' -and $null -ne $_.primaryScore })
            $fresh = @($Confirmation.trials | Where-Object { $_.arm -eq $CandidateArm -and $_.task -eq $taskId -and $_.integrity.state -eq 'clean' })
            if ($old.Count -gt 0 -and $new.Count -gt 0 -and @($old | Where-Object { $_.success -ne $true }).Count -gt 0 -and
                @($new | Where-Object { $_.success -ne $true }).Count -eq 0 -and $fresh.Count -gt 0 -and
                @($fresh | Where-Object { $_.success -eq $true }).Count -gt 0) { $gain = $true }
        }
    } elseif ($changeType -eq 'efficiency') {
        $oldRows = @($Baseline.trials | Where-Object { $_.arm -eq $BaselineArm -and $_.integrity.state -eq 'clean' -and
            $null -ne $_.primaryScore -and $_.gradingState -ne 'judge_disagreement' } | Group-Object { [string]$_.task } | Sort-Object Name)
        $newRows = @($Full.trials | Where-Object { $_.arm -eq $CandidateArm -and $_.integrity.state -eq 'clean' -and
            $null -ne $_.primaryScore -and $_.gradingState -ne 'judge_disagreement' } | Group-Object { [string]$_.task } | Sort-Object Name)
        $sameTasks = $oldRows.Count -gt 0 -and $oldRows.Count -eq $newRows.Count
        if ($sameTasks) {
            for ($index = 0; $index -lt $oldRows.Count; $index++) {
                $oldScore = [double](($oldRows[$index].Group | ForEach-Object { [double]$_.primaryScore } | Measure-Object -Average).Average)
                $newScore = [double](($newRows[$index].Group | ForEach-Object { [double]$_.primaryScore } | Measure-Object -Average).Average)
                if ($oldRows[$index].Name -ne $newRows[$index].Name -or $oldScore -ne $newScore) { $sameTasks = $false }
            }
        }
        $oldFlat = @($oldRows | ForEach-Object { $_.Group })
        $newFlat = @($newRows | ForEach-Object { $_.Group })
        $oldWall = if ($oldFlat.Count) { [double](($oldFlat | ForEach-Object { [double]$_.wallMs } | Measure-Object -Average).Average) } else { 0.0 }
        $newWall = if ($newFlat.Count) { [double](($newFlat | ForEach-Object { [double]$_.wallMs } | Measure-Object -Average).Average) } else { 0.0 }
        $oldTokens = if ($oldFlat.Count) { [double](($oldFlat | ForEach-Object { [double]$_.inputTokens + [double]$_.outputTokens } | Measure-Object -Average).Average) } else { 0.0 }
        $newTokens = if ($newFlat.Count) { [double](($newFlat | ForEach-Object { [double]$_.inputTokens + [double]$_.outputTokens } | Measure-Object -Average).Average) } else { 0.0 }
        $gain = $sameTasks -and (($oldWall -gt 0 -and $newWall -le 0.8 * $oldWall) -or
            ($oldTokens -gt 0 -and $newTokens -le 0.8 * $oldTokens))
    }
    $gateRows = @(
        [ordered]@{ id = 'comparison-valid'; passed = $valid; detail = if ($valid) { 'Fingerprints, keys, conditions and Judge rulings are recorded.' } else { 'Record matching conditions, verified frozen keys, fingerprints and rulings for every Judge split.' } },
        [ordered]@{ id = 'contract-clean'; passed = $contractClean; detail = if ($contractClean) { 'No critical contract violation is recorded.' } else { 'Resolve each critical contract violation before shipping.' } },
        [ordered]@{ id = 'fresh-evidence'; passed = $freshGate; detail = if ($freshGate) { 'Five confirmation Episodes per Arm support at least two affected tasks or an original/counterexample pair.' } else { 'Collect five fresh Episodes per Arm and cover two affected tasks or the original task plus a new counterexample.' } },
        [ordered]@{ id = 'full-suite'; passed = $fullGate; detail = if ($fullGate) { 'No unexplained regression remains on the served models.' } else { 'Explain or fix each regression and include every served model in the full suite.' } },
        [ordered]@{ id = 'visible-gain'; passed = $gain; detail = if ($gain) { 'The correctness fix or 20% efficiency gain is present.' } elseif ($changeType -eq 'efficiency') { 'Keep correctness unchanged and reduce wall time or tokens by at least 20%.' } else { 'Show a previously failing affected task passing in the full run and fresh confirmation.' } }
    )
    return [ordered]@{
        schema = 'motif-agent-acceptance/v1'; decision = if (@($gateRows | Where-Object { -not $_.passed }).Count -eq 0) { 'Ship' } else { 'Hold' }
        candidateArm = $CandidateArm; baselineArm = $BaselineArm; gates = $gateRows
        regressions = @($regressions); unexplainedRegressions = $unexplained
        whatWouldSettleHold = @($gateRows | Where-Object { -not $_.passed } | ForEach-Object { $_.detail })
        createdUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }
}

function Get-ABBigTierRegression([object] $Current, [object] $Baseline) {
    $reasons = [Collections.Generic.List[string]]::new()
    $expectedModels = @{ 'claude-opus' = 'opus'; 'codex-sol-chatgpt-plan' = 'gpt-6-sol' }
    $expectedFamilies = @{ 'claude-opus' = 'opus'; 'codex-sol-chatgpt-plan' = 'sol' }
    $expectedJudgeFamilies = @{ 'claude-opus' = 'sol'; 'codex-sol-chatgpt-plan' = 'opus' }
    $taskSets = @{}
    foreach ($record in @(@{ label = 'current'; summary = $Current }, @{ label = 'baseline'; summary = $Baseline })) {
        $label = $record.label
        $summary = $record.summary
        if ($null -eq $summary -or $summary -isnot [System.Collections.IDictionary]) {
            $reasons.Add("$label summary is missing or malformed")
            continue
        }
        $missing = @('measurementFingerprint', 'tasks', 'arms', 'trials' | Where-Object { -not $summary.Contains($_) })
        if ($missing.Count -gt 0) {
            $reasons.Add("$label summary is missing required evidence: $($missing -join ', ')")
            continue
        }
        $fingerprint = [string]$summary.measurementFingerprint
        if ($fingerprint -notmatch '^[0-9a-f]{64}$') { $reasons.Add("$label summary has no measurement fingerprint") }
        $taskRows = @($summary.tasks | Where-Object { $null -ne $_ })
        $tasks = @($taskRows | ForEach-Object {
            if ($_ -is [System.Collections.IDictionary] -and $_.Contains('id')) { [string]$_.id } else { '' }
        } | Where-Object { $_ } | Sort-Object -Unique)
        if ($taskRows.Count -ne 21 -or $tasks.Count -ne 21) {
            $reasons.Add("$label summary must cover 21 distinct tasks")
        }
        $taskSets[$label] = $tasks
        $armRows = @($summary.arms | Where-Object { $null -ne $_ })
        $armIds = @($armRows | ForEach-Object {
            if ($_ -is [System.Collections.IDictionary] -and $_.Contains('id')) { [string]$_.id } else { '' }
        } | Where-Object { $_ } | Sort-Object -Unique)
        $validArms = $armRows.Count -eq 2 -and $armIds.Count -eq 2 -and
            @($armRows | Where-Object {
                $_ -isnot [System.Collections.IDictionary] -or -not $_.Contains('id') -or -not $_.Contains('model') -or
                -not $_.Contains('meanWallMs') -or -not $_.Contains('meanToolCalls') -or
                -not $expectedModels.ContainsKey([string]$_.id) -or
                [string]$_.model -cne $expectedModels[[string]$_.id]
            }).Count -eq 0 -and @($expectedModels.Keys | Where-Object { $_ -notin $armIds }).Count -eq 0
        if (-not $validArms) { $reasons.Add("$label summary must include Opus and Sol with wall-time and tool-call measurements") }

        $trials = @($summary.trials | Where-Object { $null -ne $_ })
        if ($trials.Count -ne 42) { $reasons.Add("$label summary must contain 42 scored Episodes") }
        $pairs = @($trials | ForEach-Object {
            if ($_ -is [System.Collections.IDictionary] -and $_.Contains('arm') -and $_.Contains('task')) {
                [string]$_.arm + '|' + [string]$_.task
            } else { '' }
        } | Where-Object { $_ } | Sort-Object -Unique)
        $expectedPairs = @($tasks | ForEach-Object { $task = $_; $expectedModels.Keys | ForEach-Object { $_ + '|' + $task } } | Sort-Object -Unique)
        if ($pairs.Count -ne 42 -or $expectedPairs.Count -ne 42 -or
            @($pairs | Where-Object { $_ -notin $expectedPairs }).Count -gt 0 -or
            @($expectedPairs | Where-Object { $_ -notin $pairs }).Count -gt 0) {
            $reasons.Add("$label summary must contain one Episode for each Arm and task")
        }
        foreach ($trial in $trials) {
            if ($trial -isnot [System.Collections.IDictionary]) {
                $reasons.Add("$label contains a malformed Episode")
                continue
            }
            $hasIntegrity = $trial.Contains('integrity') -and $trial.integrity -is [System.Collections.IDictionary] -and
                $trial.integrity.Contains('state') -and $trial.integrity.state -eq 'clean'
            if (-not $hasIntegrity -or -not $trial.Contains('success') -or $null -eq $trial.success -or
                -not $trial.Contains('measurementFingerprint') -or [string]$trial.measurementFingerprint -cne $fingerprint -or
                -not $trial.Contains('armFingerprint') -or [string]$trial.armFingerprint -notmatch '^[0-9a-f]{64}$') {
                $reasons.Add("$label Episode $($trial.task) / $($trial.arm) is incomplete or has mismatched fingerprints")
            }
            if (-not $trial.Contains('judgeFamilies')) {
                $reasons.Add("$label Episode $($trial.task) / $($trial.arm) does not record its Judge families")
            } else {
                $judgeFamilies = @($trial.judgeFamilies | ForEach-Object { [string]$_ })
                $unknownFamilies = @($judgeFamilies | Where-Object { $_ -notin @('opus', 'sol') })
                if ($unknownFamilies.Count -gt 0 -or $judgeFamilies.Count -gt 2 -or
                    @($judgeFamilies | Sort-Object -Unique).Count -ne $judgeFamilies.Count) {
                    $reasons.Add("$label Episode $($trial.task) / $($trial.arm) records invalid Judge families")
                } elseif ($label -eq 'current' -and $judgeFamilies.Count -gt 0) {
                    $judgeVerdicts = @()
                    if ($trial.Contains('judgeVerdict')) {
                        $judgeVerdicts = @($trial.judgeVerdict | ForEach-Object { [string]$_ })
                    }
                    if ($judgeFamilies.Count -ne 1 -or
                        $judgeFamilies[0] -cne $expectedJudgeFamilies[[string]$trial.arm]) {
                        $reasons.Add("current Episode $($trial.task) / $($trial.arm) must use only the other family's Judge")
                    }
                    if ($judgeVerdicts.Count -ne 1 -or $judgeVerdicts[0] -cne 'single judge') {
                        $reasons.Add("current Episode $($trial.task) / $($trial.arm) must record the verdict as single judge")
                    }
                } elseif ($label -eq 'current' -and $trial.Contains('judgeVerdict') -and
                    @($trial.judgeVerdict | Where-Object { $_ -and $_ -ne 'not judged' }).Count -gt 0) {
                    $reasons.Add("current Episode $($trial.task) / $($trial.arm) records a Judge verdict without a Judge family")
                } elseif ($label -eq 'current' -and
                    (-not $trial.Contains('judgeVerdict') -or [string]$trial.judgeVerdict -cne 'not judged')) {
                    $reasons.Add("current Episode $($trial.task) / $($trial.arm) must mark its outcome as not judged when no Judge ran")
                }
            }
        }
    }
    if ($taskSets.ContainsKey('current') -and $taskSets.ContainsKey('baseline') -and
        @($taskSets.current | Where-Object { $_ -notin $taskSets.baseline }).Count -gt 0) {
        $reasons.Add('current and baseline summaries cover different task sets')
    }
    $outcomeState = {
        param([object] $Trial)
        if (-not $Trial.Contains('success') -or $null -eq $Trial.success) { return 'unscored' }
        if ($Trial.success -eq $true) { return 'pass' }
        return 'fail'
    }
    $reviewEntries = @{}
    $addPersonReview = {
        param([object] $Trial, [string] $OutcomeBefore, [string] $OutcomeNow, [string] $Reason)
        $key = [string]$Trial.arm + '|' + [string]$Trial.task
        if (-not $reviewEntries.ContainsKey($key)) {
            $reviewEntries[$key] = [ordered]@{
                task = [string]$Trial.task; arm = [string]$Trial.arm
                outcomeBefore = $OutcomeBefore; outcomeNow = $OutcomeNow
                reasons = @(); failureClass = if ($Trial.Contains('failureClass')) { $Trial.failureClass } else { $null }
                failureEvidence = if ($Trial.Contains('failureEvidence')) { @($Trial.failureEvidence) } else { @() }
            }
        }
        $entry = $reviewEntries[$key]
        if ($Reason -notin $entry.reasons) { $entry.reasons = @($entry.reasons) + $Reason }
    }
    $currentTrialsByPair = @{}
    $currentTrials = if ($Current -is [System.Collections.IDictionary] -and $Current.Contains('trials')) {
        @($Current.trials | Where-Object { $_ -is [System.Collections.IDictionary] })
    } else { @() }
    $baselineTrials = if ($Baseline -is [System.Collections.IDictionary] -and $Baseline.Contains('trials')) {
        @($Baseline.trials | Where-Object { $_ -is [System.Collections.IDictionary] })
    } else { @() }
    foreach ($trial in $currentTrials) {
        $key = [string]$trial.arm + '|' + [string]$trial.task
        $currentTrialsByPair[$key] = $trial
        $outcome = & $outcomeState $trial
        $hasFailureClass = $trial.Contains('failureClass') -and [string]$trial.failureClass
        if ($outcome -ne 'pass' -or $hasFailureClass) {
            $reason = if ($outcome -eq 'fail') { 'Episode failed' } else { 'Episode did not produce a scored pass' }
            & $addPersonReview $trial '' $outcome $reason
        }
    }
    foreach ($prior in $baselineTrials) {
        $key = [string]$prior.arm + '|' + [string]$prior.task
        if (-not $currentTrialsByPair.ContainsKey($key)) { continue }
        $currentTrial = $currentTrialsByPair[$key]
        $before = & $outcomeState $prior
        $now = & $outcomeState $currentTrial
        if ($before -cne $now) { & $addPersonReview $currentTrial $before $now 'Outcome changed since the previous release check' }
    }
    $personReview = @($reviewEntries.Values | Sort-Object task, arm)
    if ($reasons.Count -gt 0) {
        return [ordered]@{ valid = $false; triggered = $false; taskRegressions = @(); metricRegressions = @()
            schema = 'motif-agent-big-tier-release/v1'; verdict = 'single judge'; decision = 'Hold'
            personReview = $personReview; personReviewRequired = $personReview.Count -gt 0
            reasons = @($reasons); whatWouldSettleHold = @($reasons)
            action = 'Hold the release check until its evidence is complete and valid.' }
    }
    $taskRegressions = [Collections.Generic.List[object]]::new()
    foreach ($prior in $Baseline.trials | Where-Object { $_.success -eq $true -and $_.integrity.state -eq 'clean' }) {
        $matchingTrial = @($Current.trials | Where-Object { $_.task -eq $prior.task -and $_.arm -eq $prior.arm } | Select-Object -First 1)
        if ($matchingTrial.Count -eq 0 -or $matchingTrial[0].success -ne $true) {
            $taskRegressions.Add(@{ task = $prior.task; arm = $prior.arm; reason = 'a task that passed previously did not pass' })
        }
    }
    $metricRegressions = [Collections.Generic.List[object]]::new()
    foreach ($priorArm in $Baseline.arms) {
        $newArm = @($Current.arms | Where-Object { $_.id -eq $priorArm.id } | Select-Object -First 1)
        if ($newArm.Count -eq 0) { continue }
        foreach ($metric in @('meanWallMs', 'meanToolCalls')) {
            $before = [double]$priorArm[$metric]
            $after = [double]$newArm[0][$metric]
            if ($before -gt 0 -and $after -gt 1.2 * $before) {
                $metricRegressions.Add(@{ arm = $priorArm.id; metric = $metric; baseline = $before; current = $after })
            }
        }
    }
    $triggered = $taskRegressions.Count -gt 0 -or $metricRegressions.Count -gt 0
    return [ordered]@{ schema = 'motif-agent-big-tier-release/v1'; verdict = 'single judge'
        decision = if ($triggered) { 'FullComparisonRequired' } else { 'NoTrigger' }
        valid = $true; triggered = $triggered; taskRegressions = @($taskRegressions); metricRegressions = @($metricRegressions)
        personReview = $personReview; personReviewRequired = $personReview.Count -gt 0
        whatWouldSettleHold = @()
        action = if ($triggered) { 'Run the 21-task, 3-Episode full comparison; a person reviews every listed failure and outcome change.' }
            else { 'No full comparison trigger was found; the release check does not accept a change.' } }
}

Export-ModuleMember -Function Get-ABRouteHealthResult, Get-ABAcceptanceResult, Get-ABBigTierRegression
