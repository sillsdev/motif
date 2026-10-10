[CmdletBinding()]
param([Parameter(Mandatory)][string] $RunManifest, [Alias('-keep')][switch] $Keep)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ABHarness.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'McpClient.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'ProductStaging.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'HostPlan.psm1') -Force
$manifest = Read-ABJson $RunManifest
$trialRoot = [string]$manifest.trialRoot
$keepStaging = $Keep -or ($manifest.Contains('keep') -and [bool]$manifest.keep)
try {
$authMode = Get-ABInferenceAuthMode $manifest.arm
$repoRoot = [string]$manifest.repoRoot
$task = $manifest.task
$task.taskPath = [string]$manifest.taskPath
$profilePath = if ($manifest.arm.server.Contains('gradeProfilePath')) {
    [string]$manifest.arm.server.gradeProfilePath
} else { [string]$manifest.arm.server.profilePath }
$profile = Read-ABJson $profilePath
$outputRoot = [string]$manifest.outputRoot
$motifExe = [string]$manifest.arm.server.executable
$builderExe = Join-Path $repoRoot ("bin/{0}/SIL.Motif.EvalSets{1}" -f $manifest.configuration, $(if ($IsWindows) { '.exe' } else { '' }))
$sessionRoot = Join-Path $trialRoot 'session'
$inputRoot = Join-Path $trialRoot 'input'
$runtimeRoot = Join-Path $trialRoot 'runtime'
$controlRoot = Join-Path $trialRoot 'control'
$projectRoot = Join-Path $sessionRoot 'project'
foreach ($path in @($outputRoot, $inputRoot, $runtimeRoot, $controlRoot, $projectRoot)) {
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}
if ($authMode -eq 'claude-plan') {
    $claudeConfig = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR }
        else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.claude' }
    $credentialSource = Join-Path $claudeConfig '.credentials.json'
    if (-not (Test-Path -LiteralPath $credentialSource -PathType Leaf)) {
        throw 'Claude plan auth requires the owner login at CLAUDE_CONFIG_DIR/.credentials.json.'
    }
    $credentialFile = Get-Item -LiteralPath $credentialSource -Force
    if ($credentialFile.LinkTarget -or ($credentialFile.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Claude plan credentials must be a regular file.'
    }
    $jailClaudeConfig = Join-Path $sessionRoot 'agent/config/claude'
    New-Item -ItemType Directory -Path $jailClaudeConfig -Force | Out-Null
    Copy-Item -LiteralPath $credentialSource -Destination (Join-Path $jailClaudeConfig '.credentials.json')
    $chmod = Get-Command chmod -ErrorAction SilentlyContinue
    if ($chmod) { & $chmod.Source '400' (Join-Path $jailClaudeConfig '.credentials.json') }
}
$controlEnvironment = @{
    MOTIF_WORKER_ROOT = Join-Path $controlRoot 'work'
    MOTIF_RUNNER_NAMESPACE = 'prepare-' + [Guid]::NewGuid().ToString('N')
    MOTIF_WRITING_SYSTEM_REPOSITORY_PATH = Join-Path $controlRoot 'writing-systems'
    MOTIF_TEST_SLDR_CACHE_PATH = Join-Path $controlRoot 'sldr'
    MOTIF_TEST_SLDR_OFFLINE = '1'
    MOTIF_ADVANCED_AI_MODE_PATH = Join-Path $controlRoot 'choice.json'
    MOTIF_DEVELOPER_COMMANDS = '1'
}
foreach ($key in $controlEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $controlEnvironment[$key]) }
Write-ABJson $controlEnvironment.MOTIF_ADVANCED_AI_MODE_PATH @{ advancedAiMode = $true }
$language = Read-ABJson (Join-Path $manifest.setPath 'language/language.yaml')
$language.description = 'A constructed language for exploring its grammar.'
$language.disclaimer = 'All forms and meanings in this language are invented.'
$language.language.name = 'Constructed language'
$seedRoot = Join-Path $controlRoot 'source'
Write-ABJson (Join-Path $seedRoot 'language/language.yaml') $language
foreach ($directory in @('words', 'gold')) {
    Copy-Item -LiteralPath (Join-Path $manifest.setPath $directory) -Destination $seedRoot -Recurse
}
$seed = Invoke-ABProcess $builderExe @('build-project', '--set', $seedRoot, '--start', $task.start, '--out', (Join-Path $controlRoot 'prepared')) $controlRoot @{} 300000
if ($seed.ExitCode) { throw "Project preparation failed: $($seed.Stderr)" }
$seedPath = [string](($seed.Stdout | ConvertFrom-Json -AsHashtable).projectPath)
Copy-Item -Path (Join-Path (Split-Path $seedPath) '*') -Destination $projectRoot -Recurse
Get-ChildItem $projectRoot -Filter '*.fwdata*' | Where-Object { $_.Name -notlike '*.fwdata' } | Remove-Item -Force
$fwDataPath = Join-Path $projectRoot 'project.fwdata'
Move-Item -LiteralPath (Join-Path $projectRoot ([IO.Path]::GetFileName($seedPath))) -Destination $fwDataPath

$clientRoot = Join-Path $runtimeRoot 'client'
New-Item -ItemType Directory $clientRoot -Force | Out-Null
$buildRoot = Split-Path $motifExe
$runtimeSource = if ($env:DOTNET_ROOT) { $env:DOTNET_ROOT } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.dotnet' }
$layerRoot = Join-Path (Get-ABTrialBase $repoRoot) 'layers'
$layers = Get-ABRuntimeLayers $buildRoot $env:MOTIF_PANGLOSS_EXE $runtimeSource $layerRoot
$productRoot = $layers.product
$dotnetRoot = $layers.runtime
$profileArgument = [string]$manifest.arm.server.profileArgument
$serverProfilePath = if (Test-Path -LiteralPath $profileArgument -PathType Leaf) { $profileArgument } else { Join-Path $buildRoot ("profiles/$profileArgument.json") }
$serverProfile = Read-ABJson $serverProfilePath
if ($serverProfile.Contains('instructionsFile')) {
    $serverProfile.instructions = Get-Content (Join-Path (Split-Path $serverProfilePath) $serverProfile.instructionsFile) -Raw
    $serverProfile.Remove('instructionsFile')
}
$serverProfile.name = 'workspace'
Write-ABJson (Join-Path $inputRoot 'surface.json') $serverProfile
Copy-Item (Join-Path $PSScriptRoot 'Invoke-IsolatedHost.ps1') (Join-Path $clientRoot 'host.ps1')
Copy-Item (Join-Path $PSScriptRoot 'Invoke-ABFakeHost.ps1') (Join-Path $clientRoot 'relay.ps1')
Copy-Item (Join-Path $PSScriptRoot 'McpClient.psm1') (Join-Path $clientRoot 'protocol.psm1')
Copy-Item (Join-Path $PSScriptRoot 'InferenceRelay.py') (Join-Path $clientRoot 'connection.py')
Copy-Item (Join-Path $PSScriptRoot 'IsolatedHost.psm1') (Join-Path $clientRoot 'host-functions.psm1')
Copy-Item (Join-Path $PSScriptRoot 'HostPlan.psm1') (Join-Path $clientRoot 'host-plan.psm1')
Copy-Item (Join-Path $PSScriptRoot 'AgentBoundary.py') (Join-Path $clientRoot 'agent-boundary.py')
Copy-Item (Join-Path $PSScriptRoot 'McpBridgeClient.py') (Join-Path $clientRoot 'stdio.py')
$prompt = Get-Content -LiteralPath (Join-Path $manifest.taskPath $task.prompt) -Raw
$systemAppend = $null
if ($manifest.arm.Contains('system_append') -and $manifest.arm.system_append) {
    $path = [string]$manifest.arm.system_append
    if (-not [IO.Path]::IsPathRooted($path)) { $path = Join-Path $repoRoot $path }
    $systemAppend = Get-Content -LiteralPath $path -Raw
}
$inside = [ordered]@{
    arm = @{ host = $manifest.arm.host; model = $manifest.arm.model; effort = $manifest.arm.effort; authMode = $authMode }
    task = @{ limits = $task.limits; promptText = $prompt }
    profile = '/input/surface.json'
    profileName = if ($profileArgument -eq 'lean') { 'lean' } else { 'workspace' }
    systemAppend = $systemAppend
    validationMode = $manifest.validationMode
    preflight = $false
    mcpTimeoutMs = Get-ABFakeMcpTimeoutMs
}
Write-ABJson (Join-Path $inputRoot 'session.json') $inside
$inside.preflight = $true
Write-ABJson (Join-Path $inputRoot 'preflight.json') $inside
if ($manifest.Contains('fakeScript')) { $fakeScript = [string]$manifest.fakeScript }
else { $fakeScript = Join-Path $repoRoot ("evals/fake-scripts/$($task.id).jsonl") }
if ($manifest.validationMode -eq 'empty') {
    Set-Content (Join-Path $inputRoot 'actions.jsonl') '{"finalMessage":""}'
    $inside.validationMode = $null
    $inside.preflight = $false
    Write-ABJson (Join-Path $inputRoot 'session.json') $inside
} elseif ($manifest.arm.host -eq 'fake') { Copy-Item $fakeScript (Join-Path $inputRoot 'actions.jsonl') }

$listRoot = Join-Path $controlRoot 'listing'
Copy-Item -LiteralPath $projectRoot -Destination $listRoot -Recurse
$listSession = New-ABMcpSession $motifExe @('mcp', '--project', (Join-Path $listRoot 'project.fwdata'), '--profile', $serverProfilePath) $controlRoot @{} (Join-Path $controlRoot 'listing.jsonl')
try { $allowed = @($listSession.Tools | ForEach-Object { [string]$_.name }) }
finally { [void](Close-ABMcpSession $listSession) }
$gradeTools = @{}
foreach ($tool in $profile.tools) { $gradeTools[$tool.name] = $tool.class }
$profile.tools = @($allowed | ForEach-Object { @{ name = $_; class = if ($gradeTools.Contains($_)) { $gradeTools[$_] } else { 'Read' } } })
$boundaryConfig = [ordered]@{
    session = $sessionRoot
    archive = $outputRoot
    host = $manifest.arm.host
    model = $manifest.arm.model
    authMode = $authMode
    timeout = [int]$task.limits.wall_seconds + 30
    mounts = @(@($productRoot, '/opt/product'), @($clientRoot, '/opt/client'), @($dotnetRoot, '/opt/runtime'), @((Split-Path (Get-Command pwsh).Source), '/opt/powershell'), @($inputRoot, '/input'))
    allowedTools = $allowed
    protected = @($repoRoot, $env:MOTIF_TEST_GRAMMARS, $env:MOTIF_CONFIRMATION_GRAMMARS, [string]$manifest.setPath, [string]$manifest.taskPath, $controlRoot, [string]$RunManifest, [Environment]::GetFolderPath('UserProfile'))
}
$boundaryConfig.protected = @($boundaryConfig.protected | Where-Object { $_ })
$pwshFile = [IO.FileInfo](Get-Command pwsh).Source
$pwshResolved = $pwshFile.ResolveLinkTarget($true)
$boundaryConfig.mounts[3][0] = Split-Path $(if ($pwshResolved) { $pwshResolved.FullName } else { $pwshFile.FullName })
if ($manifest.arm.host -ne 'fake') {
    $nativeVariable = if ($manifest.arm.host -eq 'codex') { 'MOTIF_CODEX_NATIVE' } else { 'MOTIF_CLAUDE_NATIVE' }
    $hostCommand = [Environment]::GetEnvironmentVariable($nativeVariable)
    if (-not $hostCommand) { $hostCommand = (Get-Command $manifest.arm.host).Source }
    $hostFile = [IO.FileInfo]$hostCommand
    if ($hostFile.LinkTarget) { $hostCommand = $hostFile.ResolveLinkTarget($true).FullName }
    $magic = [IO.File]::ReadAllBytes($hostCommand)[0..3]
    if (($magic -join ',') -ne '127,69,76,70') {
        throw "Live host packaging requires a native Linux ELF binary; set $nativeVariable to the installed native host executable."
    }
    $hostRoot = Get-ABHostLayer $hostCommand $layerRoot
    $boundaryConfig.mounts += ,@($hostRoot, '/opt/host')
}
$configPath = Join-Path $controlRoot 'boundary.json'
Write-ABJson $configPath $boundaryConfig
$boundaryRun = Invoke-ABProcess (Get-Command python3).Source @((Join-Path $PSScriptRoot 'TrialBoundary.py'), $configPath) $controlRoot @{} ([int]$task.limits.wall_seconds * 1000 + 420000)
$integrityPath = Join-Path $outputRoot 'integrity.json'
if (-not (Test-Path $integrityPath)) { throw "Boundary supervisor failed: $($boundaryRun.Stderr)" }
$integrity = Read-ABJson $integrityPath
$gradePath = Join-Path $outputRoot 'grade.json'
if ($integrity.state -eq 'clean' -and $integrity.failureClass -notin @('cloud_failure', 'harness_defect')) {
    $gradeConfig = [ordered]@{
        repoRoot = $repoRoot
        confirmation = $manifest.Contains('confirmation') -and [bool]$manifest.confirmation
        bundle = Join-Path $outputRoot 'frozen'
        integrityPath = $integrityPath
        task = $task
        setPath = $seedRoot
        builder = $builderExe
        primaryMetric = $manifest.primaryMetric
        profile = $profile
        judgeFamilies = if ($manifest.Contains('judgeFamilies')) { @($manifest.judgeFamilies) } else { @('opus', 'sol') }
        outputRoot = $outputRoot
        scratchRoot = Join-Path $controlRoot 'measurement'
    }
    $gradeConfigPath = Join-Path $controlRoot 'grading.json'
    Write-ABJson $gradeConfigPath $gradeConfig
    $gradeRun = Invoke-ABProcess (Get-Command pwsh).Source @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Invoke-ABGrade.ps1'), '-Config', $gradeConfigPath) $controlRoot @{} 1600000
    if ($gradeRun.ExitCode) {
        $integrity.failureClass = 'harness_defect'
        $integrity.failureEvidence += @{ class = 'harness_defect'; evidence = 'The control-side grader failed: ' + $gradeRun.Stderr }
        $grade = @{ grade = $null; success = $null; primaryScore = $null; primaryMetric = $manifest.primaryMetric;
            operationCount = 0; graders = @(); gradingState = 'harness_defect'; failureClass = 'harness_defect';
            failureEvidence = @($gradeRun.Stderr) }
        Write-ABJson $gradePath $grade
        Write-ABJson $integrityPath $integrity
    }
}
if ($integrity.state -ne 'clean' -or $integrity.failureClass -in @('cloud_failure', 'harness_defect')) {
    if (-not (Test-Path -LiteralPath $gradePath)) {
        Write-ABJson $gradePath @{ grade = $null; success = $null; primaryScore = $null; primaryMetric = $manifest.primaryMetric;
            operationCount = 0; graders = @(); gradingState = 'unscored'; failureClass = $integrity.failureClass;
            failureEvidence = @($integrity.failureEvidence) }
    }
}
$hostPath = Join-Path $outputRoot 'frozen/host.json'
$hostData = if (Test-Path $hostPath) { Read-ABJson $hostPath } else { @{ wallMs = $null; turns = $null; inputTokens = $null; outputTokens = $null; costUsd = $null } }
foreach ($name in @('host.json', 'transcript.jsonl', 'activity.jsonl')) {
    $source = Join-Path $outputRoot "frozen/$name"
    if (Test-Path $source) { Copy-Item $source (Join-Path $outputRoot $name) }
}
$activity = @(Read-ABLines (Join-Path $outputRoot 'activity.jsonl'))
$grade = Read-ABJson $gradePath
$status = if ($integrity.state -ne 'clean') { $integrity.state }
    elseif ($grade.failureClass -in @('cloud_failure', 'harness_defect')) { $grade.failureClass }
    elseif ($integrity.failureClass -in @('cloud_failure', 'harness_defect')) { $integrity.failureClass }
    elseif ($grade.gradingState -eq 'judge_disagreement') { 'judge_disagreement' }
    elseif ($grade.success -eq $true) { 'passed' } elseif ($grade.success -eq $false) { 'failed' } else { 'inconclusive' }
Write-ABJson (Join-Path $outputRoot 'manifest.json') ([ordered]@{
    schema = 'motif-controlled-trial/v1'
    runId = $manifest.runId; questionId = $manifest.questionId; taskId = $task.id; setId = $task.set
    caseId = [Guid]::NewGuid().ToString('N'); armId = $manifest.arm.id; host = $manifest.arm.host; trial = $manifest.trial
    status = $status; integrity = $integrity
    failureClass = if ($grade.failureClass) { $grade.failureClass } else { $integrity.failureClass }
    failureEvidence = @($grade.failureEvidence) + @($integrity.failureEvidence)
    measurementFingerprint = if ($manifest.Contains('measurementFingerprint')) { $manifest.measurementFingerprint } else { $null }
    armFingerprint = if ($manifest.arm.server.Contains('fingerprint')) { $manifest.arm.server.fingerprint } else { $null }
    sourceCommit = if ($manifest.arm.server.Contains('sourceCommit')) { $manifest.arm.server.sourceCommit } else { $null }
    authMode = $authMode
    staging = @{ retained = [bool]$keepStaging; path = if ($keepStaging) { $trialRoot } else { $null } }
    excludedFromScoring = $integrity.state -ne 'clean' -or $grade.failureClass -in @('cloud_failure', 'harness_defect') -or
        $integrity.failureClass -in @('cloud_failure', 'harness_defect') -or
        $grade.gradingState -eq 'judge_disagreement'
    wallMs = $hostData.wallMs; turns = $hostData.turns; toolCalls = $activity.Count
    toolErrors = @($activity | Where-Object { $_.isError }).Count
    timeToFirstProposalMs = $null; inputTokens = $hostData.inputTokens; outputTokens = $hostData.outputTokens; costUsd = $hostData.costUsd
    agentBudget = if ($hostData.Contains('agentBudget')) { $hostData.agentBudget } else { $null }
    agentElapsedMs = if ($hostData.Contains('agentElapsedMs')) { $hostData.agentElapsedMs } else { $null }
})
Write-Host (Join-Path $outputRoot 'manifest.json')
}
finally { Remove-ABTrialDirectory $trialRoot -Keep:$keepStaging }
