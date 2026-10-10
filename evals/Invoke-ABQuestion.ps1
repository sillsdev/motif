[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Question,
    [int] $Trials = 0,
    [string] $Tasks,
    [switch] $DryRun,
    [int] $Parallel = 2,
    [string] $Configuration = 'Debug',
    [switch] $SkipValidityCheck,
    [switch] $IncludeFuture,
    [switch] $Confirmation,
    [Alias('-keep')][switch] $Keep
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $PSScriptRoot 'tools/ABHarness.psm1'
Import-Module $modulePath -Force
Import-Module (Join-Path $PSScriptRoot 'tools/HostPlan.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'tools/ProductStaging.psm1') -Force
$setRoot = Get-ABSetRoot $repoRoot -Confirmation:$Confirmation
if ($Confirmation -and -not $env:MOTIF_CONFIRMATION_GRAMMARS) { throw 'Confirmation requires MOTIF_CONFIRMATION_GRAMMARS on the grading side.' }
if ($Parallel -lt 1) { throw '-Parallel must be at least 1.' }
if ($Trials -lt 0) { throw '-Trials must be zero or a positive whole number.' }

$questionPath = if ([IO.Path]::IsPathRooted($Question)) { $Question } else { Join-Path $repoRoot $Question }
if (-not (Test-Path -LiteralPath $questionPath -PathType Leaf)) { throw "Question file not found: $questionPath" }
$questionData = Read-ABJson $questionPath
$questionId = [IO.Path]::GetFileNameWithoutExtension($questionPath)
$judgePolicy = if ($questionData.Contains('judge_policy')) { [string]$questionData.judge_policy } else { 'paired' }
if ($judgePolicy -notin @('paired', 'other-family-alone')) { throw "Unknown judge policy '$judgePolicy'." }
$trialCount = if ($Trials -gt 0) { $Trials } elseif ($questionData.trials) { [int]$questionData.trials } else { 3 }
if ($trialCount -lt 1) { throw 'The question must request at least one trial.' }
$primaryMetric = if ($questionData.primary_metric) { [string]$questionData.primary_metric } else { 'grade' }

function Resolve-ABTask([string] $TaskDirectory) {
    $taskFile = Join-Path $TaskDirectory 'task.yaml'
    $taskData = Read-ABJson $taskFile
    $taskData.taskPath = $TaskDirectory
    $taskData.setPath = Join-Path $setRoot ([string]$taskData.set)
    if (-not (Test-Path -LiteralPath $taskData.setPath)) { throw "Eval set not found: $($taskData.setPath)" }
    return $taskData
}

$allTasks = @(Get-ChildItem -LiteralPath $setRoot -Filter 'task.yaml' -File -Recurse |
    ForEach-Object { Resolve-ABTask $_.Directory.FullName })
$selector = if ($Tasks) { $Tasks } else { $questionData.tasks }
if ($null -eq $selector) { throw 'The question must name tasks.' }
$selectors = if ($selector -is [string]) { @($selector) } else { @($selector) }
$selectedTasks = [Collections.Generic.List[object]]::new()
foreach ($task in $allTasks) {
    if ((Test-ABTaskFuture $task) -and -not $IncludeFuture) { continue }
    $taskRelative = [IO.Path]::GetRelativePath($setRoot, $task.taskPath).Replace('\', '/')
    foreach ($pattern in $selectors) {
        $wildcard = [Management.Automation.WildcardPattern]::new([string]$pattern, [Management.Automation.WildcardOptions]::IgnoreCase)
        if ($wildcard.IsMatch([string]$task.id) -or $wildcard.IsMatch($taskRelative) -or $wildcard.IsMatch([string]$task.set)) {
            $selectedTasks.Add($task)
            break
        }
    }
}
if ($Tasks -and $selectedTasks.Count -eq 0) { throw "-Tasks did not select any eval tasks: $Tasks" }
if (-not $Tasks -and $selectedTasks.Count -eq 0) { throw "Question '$questionId' did not select any eval tasks." }

if (-not $DryRun) {
    $hasHeadArm = $false
    foreach ($armId in @([string]$questionData.arm_a, [string]$questionData.arm_b)) {
        $armData = Read-ABJson (Join-Path $repoRoot ("evals/arms/{0}.yaml" -f $armId))
        if ($armData.server.Contains('ref') -and $armData.server.ref -eq 'HEAD') { $hasHeadArm = $true }
    }
    if (-not $hasHeadArm -and -not $SkipValidityCheck) {
        $build = Invoke-ABProcess (Get-Command pwsh).Source @('-NoProfile', '-File', (Join-Path $repoRoot 'build.ps1'), '-Configuration', $Configuration) `
            $repoRoot @{ MSBUILDDISABLENODEREUSE = '1'; UseSharedCompilation = 'false' } 3600000
        if ($build.ExitCode -ne 0) { throw "Building the control-side grader failed: $($build.Stdout)`n$($build.Stderr)" }
    }
}

function Resolve-ABArm([string] $ArmId) {
    $armPath = Join-Path $repoRoot ("evals/arms/" + $ArmId + '.yaml')
    if (-not (Test-Path -LiteralPath $armPath)) { throw "Arm file not found: $armPath" }
    $arm = Read-ABJson $armPath
    $arm.authMode = Get-ABInferenceAuthMode $arm
    $profileValue = [string]$arm.server.profile
    $profileIsPath = [IO.Path]::IsPathRooted($profileValue) -or $profileValue.Contains('/') -or $profileValue.EndsWith('.json')
    if ($profileIsPath) {
        $profilePath = if ([IO.Path]::IsPathRooted($profileValue)) { $profileValue } else { Join-Path (Join-Path $repoRoot 'evals') $profileValue }
        if (-not (Test-Path -LiteralPath $profilePath -PathType Leaf)) { throw "Profile file not found: $profilePath" }
        $arm.server.profileArgument = [IO.Path]::GetFullPath($profilePath)
        $gradeValue = if ($arm.server.Contains('grade_profile')) { [string]$arm.server.grade_profile } else { $profileValue }
        $gradePath = if ([IO.Path]::IsPathRooted($gradeValue)) { $gradeValue } else { Join-Path (Join-Path $repoRoot 'evals') $gradeValue }
        if (-not (Test-Path -LiteralPath $gradePath -PathType Leaf)) { throw "Grader profile file not found: $gradePath" }
        $arm.server.profilePath = [IO.Path]::GetFullPath($gradePath)
    }
    else {
        $gradeValue = if ($arm.server.Contains('grade_profile')) { [string]$arm.server.grade_profile } else { "profiles/$profileValue.json" }
        $gradePath = if ([IO.Path]::IsPathRooted($gradeValue)) { $gradeValue } else { Join-Path (Join-Path $repoRoot 'evals') $gradeValue }
        if (-not (Test-Path -LiteralPath $gradePath -PathType Leaf)) { throw "Grader profile file not found: $gradePath" }
        $arm.server.profileArgument = $profileValue
        $arm.server.profilePath = [IO.Path]::GetFullPath($gradePath)
    }
    $arm.server.gradeProfilePath = [IO.Path]::GetFullPath($gradePath)
    $arm.server.profilePath = [IO.Path]::GetFullPath($gradePath)
    if ($arm.server.Contains('path') -and $arm.server.path) {
        $exe = [string]$arm.server.path
        if (-not [IO.Path]::IsPathRooted($exe)) { $exe = Join-Path $repoRoot $exe }
        if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Server executable not found: $exe" }
        $arm.server.executable = [IO.Path]::GetFullPath($exe)
        $arm.server.buildRoot = Split-Path $arm.server.executable
        return $arm
    }
    if ($arm.server.Contains('executable') -and $arm.server.executable) {
        $exe = [string]$arm.server.executable
        if (-not [IO.Path]::IsPathRooted($exe)) { $exe = Join-Path $repoRoot $exe }
        if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Server executable not found: $exe" }
        $arm.server.executable = [IO.Path]::GetFullPath($exe)
        $arm.server.buildRoot = Split-Path $arm.server.executable
        return $arm
    }
    $ref = if ($arm.server.Contains('ref')) { [string]$arm.server.ref } else { '' }
    if ([string]::IsNullOrWhiteSpace($ref)) { throw "Arm '$ArmId' must set server.ref, server.path, or server.executable." }
    if ($DryRun) {
        $arm.server.executable = $null
        $arm.server.buildRoot = $null
        return $arm
    }
    if ($ref -eq 'HEAD') {
        [Environment]::SetEnvironmentVariable('MSBUILDDISABLENODEREUSE', '1')
        [Environment]::SetEnvironmentVariable('UseSharedCompilation', 'false')
        $exe = Join-Path $repoRoot ("bin/{0}/motif{1}" -f $Configuration, $(if ($IsWindows) { '.exe' } else { '' }))
        $build = Invoke-ABProcess (Get-Command pwsh).Source @('-NoProfile', '-File', (Join-Path $repoRoot 'build.ps1'), '-Configuration', $Configuration) `
            $repoRoot @{ MSBUILDDISABLENODEREUSE = '1'; UseSharedCompilation = 'false' } 3600000
        if ($build.ExitCode -ne 0) { throw "The required build for Arm '$ArmId' failed: $($build.Stdout)`n$($build.Stderr)" }
        $armStage = New-ABTrialDirectory $repoRoot
        $armBuildRoot = Join-Path $repoRoot ("bin/{0}" -f $Configuration)
        $armProductRoot = Join-Path $armStage 'product'
        Copy-ABProductRuntime $armBuildRoot $armProductRoot $env:MOTIF_PANGLOSS_EXE
        $arm.server.sourceCommit = (& git -C $repoRoot rev-parse HEAD 2>$null | Out-String).Trim()
        $arm.server.executable = [IO.Path]::GetFullPath((Join-Path $armProductRoot ("motif{0}" -f $(if ($IsWindows) { '.exe' } else { '' }))))
        $arm.server.buildRoot = $armProductRoot
        $arm.server.buildStaging = $armStage
        return $arm
    }
    $buildRoot = New-ABTrialDirectory $repoRoot
    $worktreePath = Join-Path $buildRoot 'checkout'
    $git = (Get-Command git).Source
    $added = Invoke-ABProcess $git @('-C', $repoRoot, 'worktree', 'add', '--detach', $worktreePath, $ref) $repoRoot @{} 120000
    if ($added.ExitCode -ne 0) { throw "Could not create a server worktree for '$ref': $($added.Stderr)`n$($added.Stdout)" }
    $build = Invoke-ABProcess (Get-Command pwsh).Source @('-NoProfile', '-File', (Join-Path $worktreePath 'build.ps1'), '-Configuration', $Configuration) `
        $worktreePath @{ MSBUILDDISABLENODEREUSE = '1'; UseSharedCompilation = 'false' } 3600000
    if ($build.ExitCode -ne 0) {
        [void](Invoke-ABProcess $git @('-C', $repoRoot, 'worktree', 'remove', '--force', $worktreePath) $repoRoot @{} 120000)
        throw "Building server ref '$ref' failed: $($build.Stdout)`n$($build.Stderr)"
    }
    $arm.server.executable = Join-Path $worktreePath ("bin/{0}/motif{1}" -f $Configuration, $(if ($IsWindows) { '.exe' } else { '' }))
    $arm.server.buildRoot = Join-Path $worktreePath ("bin/{0}" -f $Configuration)
    $arm.server.sourceCommit = (& git -C $worktreePath rev-parse HEAD 2>$null | Out-String).Trim()
    $arm.server.worktreePath = $worktreePath
    $arm.server.worktreeRoot = $buildRoot
    return $arm
}

$arms = @(
    Resolve-ABArm ([string]$questionData.arm_a)
    Resolve-ABArm ([string]$questionData.arm_b)
)

if ($DryRun) {
    Write-Host "A/B question: $($questionData.question)"
    Write-Host "Judge policy: $judgePolicy"
    Write-Host "Control-side sets: $setRoot"
    Write-Host 'Execution: control preparation -> bubblewrap host and MCP -> frozen output -> separate grader.'
    Write-Host 'Linux requires bubblewrap and external strace auditing. Other platforms require a verified VM/container adapter.'
    foreach ($task in $selectedTasks) {
        foreach ($arm in $arms) {
            $systemAppend = $null
            if ($arm.Contains('system_append') -and $arm.system_append) {
                $path = [string]$arm.system_append
                if (-not [IO.Path]::IsPathRooted($path)) { $path = Join-Path $repoRoot $path }
                $systemAppend = Get-Content -LiteralPath $path -Raw
            }
            $hostPlan = New-ABHostPlan $arm (Get-Content (Join-Path $task.taskPath $task.prompt) -Raw) $task.limits $systemAppend
            Write-Host "$($task.id) / $($arm.id): host $($arm.host), profile $($arm.server.profileArgument), model $($arm.model), auth $($hostPlan.authMode)."
            Write-Host (Format-ABCommand $hostPlan.command $hostPlan.arguments)
            Write-Host ($hostPlan.mcpConfig | ConvertTo-Json -Depth 20 -Compress)
            if ($hostPlan.registrationArguments.Count) { Write-Host (Format-ABCommand $hostPlan.command $hostPlan.registrationArguments) }
            Write-Host 'Agent paths: /workspace/work and private home; project access is through the MCP stdio bridge.'
        }
    }
    return
}

if ($SkipValidityCheck -and @($arms | Where-Object { $_.host -ne 'fake' }).Count -gt 0) {
    throw '-SkipValidityCheck is reserved for fake-host harness checks.'
}

$armFingerprints = [ordered]@{}
foreach ($arm in $arms) {
    $armFingerprints[[string]$arm.id] = Get-ABRepositoryFingerprint $repoRoot $Configuration @($arm)
    $arm.server.fingerprint = $armFingerprints[[string]$arm.id]
}
$fingerprint = Get-ABRepositoryFingerprint $repoRoot $Configuration $arms

if (-not $SkipValidityCheck) {
    $stampPath = Join-Path $repoRoot 'evals/results/.validity.json'
    $fresh = $false
    if (Test-Path -LiteralPath $stampPath) {
        try { $fresh = (Read-ABJson $stampPath).fingerprint -eq $fingerprint } catch { $fresh = $false }
    }
    if (-not $fresh) {
        $armsManifest = Join-Path ([IO.Path]::GetTempPath()) ('motif-validity-arms-' + [Guid]::NewGuid().ToString('N') + '.json')
        Write-ABJson $armsManifest $arms
        try {
            $validationArguments = @(
                '-NoProfile', '-File', (Join-Path $repoRoot 'evals/Test-ABHarness.ps1'),
                '-ValidityOnly', '-ArmManifest', $armsManifest, '-Configuration', $Configuration
            ) + $(if ($Keep) { @('-Keep') } else { @() })
            $validation = Invoke-ABProcess (Get-Command pwsh).Source $validationArguments $repoRoot @{} 14400000
            if ($validation.ExitCode -ne 0) { throw "A/B validity checks failed before the agent run:`n$($validation.Stdout)`n$($validation.Stderr)" }
        }
        finally { Remove-Item -LiteralPath $armsManifest -Force -ErrorAction SilentlyContinue }
        $validatedFingerprint = Get-ABRepositoryFingerprint $repoRoot $Configuration $arms
        if ($validatedFingerprint -ne $fingerprint) { throw 'Validity inputs changed during the check; run again to establish the new fingerprint.' }
        Write-ABJson $stampPath ([ordered]@{
            fingerprint = $validatedFingerprint
            armFingerprints = $armFingerprints
            configuration = $Configuration
            checkedUtc = [DateTimeOffset]::UtcNow.ToString('O')
            taskCount = $selectedTasks.Count
        })
    }
}

$runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $repoRoot ("evals/results/{0}/{1}" -f $questionId, $runId)
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$trialSpecs = [Collections.Generic.List[object]]::new()
foreach ($task in $selectedTasks) {
    foreach ($arm in $arms) {
        $judgeFamilies = @('opus', 'sol')
        if ($judgePolicy -eq 'other-family-alone') {
            $judgedFamily = switch ([string]$arm.model) {
                'opus' { 'sol' }
                'gpt-6-sol' { 'opus' }
                default { throw "The other-family-alone policy has no family mapping for model '$($arm.model)'." }
            }
            $judgeFamilies = @($judgedFamily)
        }
        for ($trial = 1; $trial -le $trialCount; $trial++) {
            $outputRoot = Join-Path $runRoot ("trials/{0}/{1}/trial-{2}" -f $task.id, $arm.id, $trial)
            $privateRoot = New-ABTrialDirectory $repoRoot
            New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
            $fwDataPath = Join-Path $privateRoot 'project/MotifTestProj/MotifTestProj.fwdata'
            $spec = [ordered]@{
                runId = $runId
                questionId = $questionId
                trialRoot = $privateRoot
                outputRoot = $outputRoot
                trial = $trial
                repoRoot = $repoRoot
                configuration = $Configuration
                primaryMetric = $primaryMetric
                judgeFamilies = $judgeFamilies
                setPath = $task.setPath
                taskPath = $task.taskPath
                task = $task
                arm = $arm
                validationMode = $null
                confirmation = [bool]$Confirmation
                measurementFingerprint = $fingerprint
                keep = [bool]$Keep
                attempt = 1
                cloudRetryHistory = @()
            }
            $specPath = Join-Path $privateRoot 'run-manifest.json'
            Write-ABJson $specPath $spec
            $trialSpecs.Add([ordered]@{ path = $specPath; root = $outputRoot; privateRoot = $privateRoot; task = $task;
                arm = $arm; trial = $trial; attempt = 1; cloudRetryHistory = [Collections.Generic.List[object]]::new() })
        }
    }
}

$runner = Join-Path $repoRoot 'evals/tools/Invoke-ABTrial.ps1'
$active = [Collections.Generic.List[object]]::new()
$completed = [Collections.Generic.List[object]]::new()
$nextIndex = 0
$pwshExe = (Get-Command pwsh).Source
while ($nextIndex -lt $trialSpecs.Count -or $active.Count -gt 0) {
    while ($nextIndex -lt $trialSpecs.Count -and $active.Count -lt $Parallel) {
        $spec = $trialSpecs[$nextIndex]
        $nextIndex++
        $stdoutPath = Join-Path $spec.root 'runner.stdout.log'
        $stderrPath = Join-Path $spec.root 'runner.stderr.log'
        $startInfo = [Diagnostics.ProcessStartInfo]::new($pwshExe)
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.CreateNoWindow = $true
        $startInfo.WorkingDirectory = $repoRoot
        foreach ($argument in @('-NoProfile', '-File', $runner, '-RunManifest', $spec.path)) {
            [void]$startInfo.ArgumentList.Add([string]$argument)
        }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        if (-not $process.Start()) { throw "Could not start isolated trial $($spec.path)." }
        $spec.process = $process
        $spec.stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $spec.stderrTask = $process.StandardError.ReadToEndAsync()
        $spec.startedAt = [DateTimeOffset]::UtcNow
        $active.Add($spec)
        Write-Host "Started $($spec.task.id) / $($spec.arm.id) / trial $($spec.trial) ($($active.Count)/$Parallel active)."
    }
    for ($index = $active.Count - 1; $index -ge 0; $index--) {
        $spec = $active[$index]
        if (-not $spec.process.HasExited) { [void]$spec.process.WaitForExit(50) }
        if (-not $spec.process.HasExited) { continue }
        $stdout = $spec.stdoutTask.GetAwaiter().GetResult()
        $stderr = $spec.stderrTask.GetAwaiter().GetResult()
        Set-Content -LiteralPath (Join-Path $spec.root 'runner.stdout.log') -Value $stdout -Encoding utf8
        Set-Content -LiteralPath (Join-Path $spec.root 'runner.stderr.log') -Value $stderr -Encoding utf8
        if (-not (Test-Path -LiteralPath (Join-Path $spec.root 'manifest.json'))) {
            $invalid = [ordered]@{
                schema = 'motif-controlled-trial/v1'; caseId = [Guid]::NewGuid().ToString('N')
                runId = $runId; questionId = $questionId; taskId = $spec.task.id; setId = $spec.task.set
                armId = $spec.arm.id; host = $spec.arm.host; trial = $spec.trial
                authMode = $spec.arm.authMode
                status = 'harness_defect'; failureClass = 'harness_defect'; excludedFromScoring = $true
                failureEvidence = @("Trial setup or execution failed with exit $($spec.process.ExitCode).")
                measurementFingerprint = $fingerprint
                armFingerprint = if ($spec.arm.server.Contains('fingerprint')) { $spec.arm.server.fingerprint } else { $null }
                sourceCommit = if ($spec.arm.server.Contains('sourceCommit')) { $spec.arm.server.sourceCommit } else { $null }
                integrity = @{ state = 'isolation_failure'; failureClass = 'harness_defect';
                    failureEvidence = @(@{ class = 'harness_defect'; evidence = 'Trial setup or supervisor failed before boundary evidence was recorded.' });
                    reasons = @(@{ reason = 'Trial setup or supervisor failed before boundary evidence was recorded.' }) }
                error = (@($stdout, $stderr) | Where-Object { $_ }) -join "`n"
                wallMs = $null; turns = $null; toolCalls = 0; toolErrors = 0
                timeToFirstProposalMs = $null; inputTokens = $null; outputTokens = $null; costUsd = $null
            }
            Write-ABJson (Join-Path $spec.root 'manifest.json') $invalid
            Write-ABJson (Join-Path $spec.root 'grade.json') ([ordered]@{
                grade = $null; success = $null; primaryMetric = $primaryMetric; primaryScore = $null
                operationCount = 0; graders = @(); gradingState = 'unscored'; failureClass = 'harness_defect'
                failureEvidence = @($invalid.failureEvidence)
            })
        }
        $trialManifest = Read-ABJson (Join-Path $spec.root 'manifest.json')
        $trialGrade = Read-ABJson (Join-Path $spec.root 'grade.json')
        $activity = @(Read-ABLines (Join-Path $spec.root 'activity.jsonl'))
        $trialJudgeFamilies = @($trialGrade.graders | Where-Object { $_.Contains('judge') -and $_.judge -and $_.judge.Contains('judges') } |
            ForEach-Object { $_.judge.judges | ForEach-Object { [string]$_.family } } | Sort-Object -Unique)
        $trialJudgeVerdicts = @($trialGrade.graders | Where-Object { $_.Contains('judge') -and $_.judge -and $_.judge.Contains('verdict') } |
            ForEach-Object { [string]$_.judge.verdict } | Sort-Object -Unique)
        $trialJudgeVerdict = if ($trialJudgeVerdicts.Count -eq 0) { 'not judged' }
            elseif ($trialJudgeVerdicts.Count -eq 1) { $trialJudgeVerdicts[0] } else { 'mixed' }
        if ($trialManifest.failureClass -eq 'cloud_failure' -and $trialManifest.integrity.state -eq 'clean' -and $spec.attempt -lt 3) {
            $historyEvidence = if ($trialManifest.failureEvidence) { @($trialManifest.failureEvidence) } else { @('Cloud failure without detail.') }
            $attemptPath = Join-Path $spec.root ("cloud-attempts/attempt-{0}" -f $spec.attempt)
            New-Item -ItemType Directory -Path $attemptPath -Force | Out-Null
            Get-ChildItem -LiteralPath $spec.root -Force | Where-Object { $_.Name -ne 'cloud-attempts' } |
                Move-Item -Destination $attemptPath
            [void]$spec.cloudRetryHistory.Add([ordered]@{ attempt = $spec.attempt; failureClass = 'cloud_failure';
                evidence = $historyEvidence; costUsd = $trialManifest.costUsd; archivedAt = $attemptPath })
            $retrySpec = Read-ABJson $spec.path
            Remove-ABTrialDirectory $spec.privateRoot -Keep:$Keep
            [void]$spec.process.Dispose()
            $active.RemoveAt($index)
            $spec.attempt++
            $spec.privateRoot = New-ABTrialDirectory $repoRoot
            $retrySpec.trialRoot = $spec.privateRoot
            $retrySpec.attempt = $spec.attempt
            $retrySpec.cloudRetryHistory = @($spec.cloudRetryHistory)
            $spec.path = Join-Path $spec.privateRoot 'run-manifest.json'
            Write-ABJson $spec.path $retrySpec
            $trialSpecs.Add($spec)
            Write-Host "Retrying $($spec.task.id) / $($spec.arm.id) / Episode $($spec.trial) after a Cloud failure."
            continue
        }
        $trialManifest.cloudRetries = [Math]::Max(0, [int]$spec.attempt - 1)
        $trialManifest.cloudRetryEvidence = @($spec.cloudRetryHistory)
        $finalAttemptCost = $trialManifest.costUsd
        $attemptCosts = @(@($spec.cloudRetryHistory | ForEach-Object { $_.costUsd }) + @($finalAttemptCost))
        $episodeCostComplete = $attemptCosts.Count -gt 0 -and @($attemptCosts | Where-Object { $null -eq $_ }).Count -eq 0
        $episodeCost = if ($episodeCostComplete) {
            [double](($attemptCosts | ForEach-Object { [double]$_ } | Measure-Object -Sum).Sum)
        } else { $null }
        $trialManifest.costUsd = $episodeCost
        Write-ABJson (Join-Path $spec.root 'manifest.json') $trialManifest
        $completed.Add([ordered]@{
            task = [string]$spec.task.id
            set = [string]$spec.task.set
            arm = [string]$spec.arm.id
            trial = [int]$spec.trial
            authMode = $trialManifest.authMode
            status = [string]$trialManifest.status
            integrity = $trialManifest.integrity
            failureClass = $trialManifest.failureClass
            failureEvidence = $trialManifest.failureEvidence
            measurementFingerprint = $trialManifest.measurementFingerprint
            armFingerprint = $trialManifest.armFingerprint
            judgeFamilies = $trialJudgeFamilies
            judgeVerdict = $trialJudgeVerdict
            cloudRetries = $trialManifest.cloudRetries
            cloudRetryEvidence = $trialManifest.cloudRetryEvidence
            gradingState = if ($trialGrade.Contains('gradingState')) { $trialGrade.gradingState } else { 'unscored' }
            judgeDisagreements = @($trialGrade.graders | Where-Object { $_.Contains('judge') -and $_.judge -and $_.judge.disagreement }).Count
            primaryMetric = $trialGrade.primaryMetric
            primaryScore = $trialGrade.primaryScore
            grade = $trialGrade.grade
            success = $trialGrade.success
            operationCount = $trialGrade.operationCount
            wallMs = $trialManifest.wallMs
            turns = $trialManifest.turns
            toolCalls = $trialManifest.toolCalls
            toolErrors = $trialManifest.toolErrors
            timeToFirstProposalMs = $trialManifest.timeToFirstProposalMs
            inputTokens = $trialManifest.inputTokens
            outputTokens = $trialManifest.outputTokens
            costUsd = $episodeCost
            finalAttemptCostUsd = $finalAttemptCost
            agentBudget = if ($trialManifest.Contains('agentBudget')) { $trialManifest.agentBudget } else { $null }
            agentElapsedMs = if ($trialManifest.Contains('agentElapsedMs')) { $trialManifest.agentElapsedMs } else { $null }
            trialRoot = $spec.root
            activity = $activity
        })
        Write-Host "Finished $($spec.task.id) / $($spec.arm.id) / trial $($spec.trial): $($trialManifest.status), grade $($trialGrade.grade)."
        Remove-ABTrialDirectory $spec.privateRoot -Keep:$Keep
        [void]$spec.process.Dispose()
        $active.RemoveAt($index)
    }
}

Import-Module (Join-Path $PSScriptRoot 'tools/ABReport.psm1') -Force
Write-ABReport $completed $selectedTasks $arms $trialCount $primaryMetric $questionData $questionId $runId $runRoot $fingerprint

foreach ($arm in $arms) {
    if ($arm.server.Contains('worktreePath') -and $arm.server.worktreePath) {
        $git = (Get-Command git).Source
        [void](Invoke-ABProcess $git @('-C', $repoRoot, 'worktree', 'remove', '--force', $arm.server.worktreePath) $repoRoot @{} 120000)
        Remove-ABTrialDirectory $arm.server.worktreeRoot
    } elseif ($arm.server.Contains('buildStaging') -and $arm.server.buildStaging) {
        Remove-ABTrialDirectory $arm.server.buildStaging -Keep:$Keep
    }
}
