<#
  .SYNOPSIS
  Run the Unit and Integration test levels, the System level, or the full suite with -All.

  .DESCRIPTION
  With no level switch, this runs Unit and Integration only for a quick developer loop. That default
  run is not a merge gate. Pass -All for every level; CI and release validation use that mode. Unless
  -SkipBuild is passed, the script runs build.ps1 first so the comment gate and compile precede tests.

  The suite needs no project or checkout from outside this repo: every LibLCM project it exercises is
  built at run time by `NewLangProjFixture` and seeded by `SeededProject`. The one external dependency
  that remains is the `pangloss` executable, a separate Rust build gated by `RealParserFactAttribute`
  -- those tests skip, rather than fail, when it is not built.

  Each test project runs in one or more separate processes, with concurrent processes capped at a sixth of the
  available processor count (at least two). Test processes start CLI, worker and parser child processes, and
  the cap is sized so that four suites running side by side in separate worktrees still share the machine
  without oversubscribing it: on 20 processors, four suites at 10 processes each took 326 s, at 3 each 276-300 s. Classes that open a LibLCM cache share a serialized xUnit collection within their
  assembly, because two caches opening in one process race; a project that declares
  <MotifTestShards>N</MotifTestShards> is split by test class into N processes, which cannot race, so its
  LibLCM tests run N at a time. Classes are dealt to shards by the seconds tests/test-shard-weights.json records
  for them (renew it with tools/Update-TestShardWeights.ps1 after a full run); a class the file does not know
  still runs. Projects are discovered from Motif.sln and their IsTestProject declarations.
  New test projects are included automatically.

  No process the run starts can show a Windows crash dialog. The script sets the error mode that suppresses
  it before anything else starts, and Windows hands that mode to every child: dotnet, the test hosts, and
  every motif, runner and parser process they launch, whatever build they come from. A crash is still
  reported, through the exit code, standard error and the Windows event log.

  .PARAMETER Configuration
  MSBuild configuration. Must match what build.ps1 produced, since the suite runs with --no-build.

  .PARAMETER SkipBuild
  Reuse the existing binaries and skip the build gate. For re-running a suite you just built.

  .PARAMETER All
  Run Unit, Integration and System tests. This is the merge-gate mode.

  .PARAMETER System
  Run only System tests, without selecting the full suite.

  .PARAMETER Project
  Run only the named test project, and build only it (build.ps1 -Project) rather than Motif.sln. A -Project
  run also skips the tooling regression scripts (offline restore, tool environment, resource sizing, test
  artifact plan, process hygiene, release version, publication policy, capture screens); a run without
  -Project is the full run that CI and landing batches use, and runs them.

  A filtered run waits for a machine-wide test slot too. MOTIF_TEST_SLOTS=0 skips the wait when an outside
  scheduler already admits runs.

  .PARAMETER Filter
  Run only matching tests in the selected project, using the VSTest filter syntax.

  .PARAMETER Coverage
  Collect line coverage with coverlet (restored on demand into the shared NuGet packages folder; no project
  references it) and write coverage-summary.md and coverage-summary.json under
  bin/<Configuration>/test-results/coverage/: one row per Motif assembly, plus a subtotal over every source file
  in a Parsimony or Retirement folder. Off by default, and a normal run pays nothing for it. Instrumented
  tests run slower, and the summary covers only the projects this run selected, so pair it with -Project.

  .PARAMETER NoProcessSweep
  Keep the worktree lock and process policy but skip stale-process cleanup.

  .EXAMPLE
  ./test.ps1
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Debug',
    [switch] $SkipBuild,
    [switch] $All,
    [switch] $System,
    [string] $Project,
    [string] $Filter,
    [switch] $Coverage,
    [switch] $NoProcessSweep
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$gateClock = [Diagnostics.Stopwatch]::StartNew()
$phaseClocks = @{}
$phaseClocks['whole gate'] = $gateClock

# Windows hands this error mode to every process started from here, so no crash in the run shows a dialog.
if ($IsWindows) {
    Add-Type -Namespace Motif -Name ErrorMode -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern uint SetErrorMode(uint mode);
[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern uint GetErrorMode();
'@
    # SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX, as CrashDialogs.Suppress sets.
    [void][Motif.ErrorMode]::SetErrorMode([Motif.ErrorMode]::GetErrorMode() -bor 0x8003)
}

$repoRoot = $PSScriptRoot
$solution = Join-Path $repoRoot 'Motif.sln'
$testsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'tests')) + [IO.Path]::DirectorySeparatorChar
$env:MOTIF_DEVELOPER_COMMANDS = '1'
# Every per-user tool cache except the shared packages folder lives under bin/.cache in this checkout.
Import-Module (Join-Path $repoRoot 'tools/MotifToolEnvironment.psm1') -Force
Initialize-MotifToolEnvironment -RepoRoot $repoRoot
# Slots are machine-wide, so their folder is fixed before TEMP moves into this checkout.
Import-Module (Join-Path $repoRoot 'tools/MotifTestSlot.psm1') -Force
$testSlotDirectory = Get-MotifTestSlotDirectory
# Defender scans the user's Temp folder; a developer checkout usually sits where build churn goes unscanned.
$testTemporaryDirectory = Initialize-MotifTestTemporaryDirectory -RepoRoot $repoRoot
if ($testTemporaryDirectory) { Write-Host "Test temporary folder: $testTemporaryDirectory" }
Import-Module (Join-Path $repoRoot 'tools/MotifProcessHygiene.psm1') -Force
Set-MotifBuildProcessPolicy
$processLock = Enter-MotifWorktreeLock -RepoRoot $repoRoot -Context 'Motif tests'
$testSlot = $null

try {
    if (-not $NoProcessSweep) {
        Stop-MotifStaleProcesses -RepoRoot $repoRoot -LockHandle $processLock
    }

# Short-lived, oversubscribed test processes spend CPU on tiering and spinning: 4 suites used 25% less without.
$env:DOTNET_TieredPGO = '0'
$env:DOTNET_ThreadPool_UnfairSemaphoreSpinLimit = '0'
$env:DOTNET_TC_CallCounting = '0'
# Shards take classes by recorded seconds, not by hash; tools/Update-TestShardWeights.ps1 renews the file.
$shardWeights = Join-Path $PSScriptRoot 'tests/test-shard-weights.json'
if (Test-Path $shardWeights) { $env:MOTIF_TEST_SHARD_WEIGHTS = $shardWeights }
if ($All -and $System) { throw 'Choose either -All or -System.' }
$levelSelection = if ($All) { 'All' } elseif ($System) { 'System' } else { 'Unit,Integration' }

if ($Filter -and -not $Project) { throw '-Filter requires -Project.' }

function Write-Step {
    param([string] $Text)
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Write-PhaseDuration {
    param([string] $Name)
    $phase = $phaseClocks[$Name]
    $phase.Stop()
    Write-Host ('  {0,-34} {1,6:N1} s' -f $Name, $phase.Elapsed.TotalSeconds)
}

$phaseClocks['build gate'] = [Diagnostics.Stopwatch]::StartNew()
$buildExitCode = 0
if ($SkipBuild) {
    Write-Step 'build gate -- SKIPPED (-SkipBuild)'
}
else {
    $buildArguments = @{ Configuration = $Configuration }
    if ($Project) { $buildArguments.Project = $Project }
    if ($NoProcessSweep) { $buildArguments.NoProcessSweep = $true }
    & pwsh -NoProfile -File (Join-Path $repoRoot 'build.ps1') @buildArguments
    $buildExitCode = $LASTEXITCODE
}
Write-PhaseDuration 'build gate'
if ($buildExitCode -ne 0) {
    Write-PhaseDuration 'whole gate'
    exit 1
}

# A -Project run is an iteration; the tooling regressions run in a full run, which CI and landing use.
if (-not $Project) {
    $phaseClocks['offline restore'] = [Diagnostics.Stopwatch]::StartNew()
    Write-Step 'offline restore regression'
    & pwsh -NoProfile -File (Join-Path $repoRoot 'tools/OfflineRestore.Tests.ps1')
    Write-PhaseDuration 'offline restore'
    if ($LASTEXITCODE -ne 0) {
        Write-PhaseDuration 'whole gate'
        exit 1
    }

    $phaseClocks['tool environment regression'] = [Diagnostics.Stopwatch]::StartNew()
    Write-Step 'tool environment regression'
    & pwsh -NoProfile -File (Join-Path $repoRoot 'tools/MotifToolEnvironment.Tests.ps1')
    Write-PhaseDuration 'tool environment regression'
    if ($LASTEXITCODE -ne 0) {
        Write-PhaseDuration 'whole gate'
        exit 1
    }

    $phaseClocks['resource sizing regression'] = [Diagnostics.Stopwatch]::StartNew()
    Write-Step 'node, slot and thread sizing regression'
    & pwsh -NoProfile -File (Join-Path $repoRoot 'tools/MotifTestResources.Tests.ps1')
    Write-PhaseDuration 'resource sizing regression'
    if ($LASTEXITCODE -ne 0) {
        Write-PhaseDuration 'whole gate'
        exit 1
    }

    $phaseClocks['artifact plan regression'] = [Diagnostics.Stopwatch]::StartNew()
    Write-Step 'test artifact plan regression'
    & pwsh -NoProfile -File (Join-Path $repoRoot 'tools/Test-MotifTestRunPlan.Tests.ps1')
    Write-PhaseDuration 'artifact plan regression'
    if ($LASTEXITCODE -ne 0) {
        Write-PhaseDuration 'whole gate'
        exit 1
    }

    $phaseClocks['process hygiene regression'] = [Diagnostics.Stopwatch]::StartNew()
    Write-Step 'worktree process hygiene regression'
    & pwsh -NoProfile -File (Join-Path $repoRoot 'tools/Test-MotifProcessHygiene.Tests.ps1')
    Write-PhaseDuration 'process hygiene regression'
    if ($LASTEXITCODE -ne 0) {
        Write-PhaseDuration 'whole gate'
        exit 1
    }

    $phaseClocks['release version regression'] = [Diagnostics.Stopwatch]::StartNew()
    Write-Step 'release version regression'
    & pwsh -NoProfile -File (Join-Path $repoRoot 'tools/MotifReleaseVersion.Tests.ps1')
    Write-PhaseDuration 'release version regression'
    if ($LASTEXITCODE -ne 0) {
        Write-PhaseDuration 'whole gate'
        exit 1
    }

    $phaseClocks['publication policy regression'] = [Diagnostics.Stopwatch]::StartNew()
    Write-Step 'publication policy regression'
    & pwsh -NoProfile -File (Join-Path $repoRoot 'tools/MotifReleasePublication.Tests.ps1')
    Write-PhaseDuration 'publication policy regression'
    if ($LASTEXITCODE -ne 0) {
        Write-PhaseDuration 'whole gate'
        exit 1
    }

    $phaseClocks['help golden regression'] = [Diagnostics.Stopwatch]::StartNew()
    Write-Step 'help golden gate regression'
    & pwsh -NoProfile -File (Join-Path $repoRoot 'tools/MotifHelpGolden.Tests.ps1')
    Write-PhaseDuration 'help golden regression'
    if ($LASTEXITCODE -ne 0) {
        Write-PhaseDuration 'whole gate'
        exit 1
    }

    if ($All) {
        $phaseClocks['capture screen regression'] = [Diagnostics.Stopwatch]::StartNew()
        Write-Step 'capture screenshot tooling regression'
        & pwsh -NoProfile -File (Join-Path $repoRoot 'tools/Capture-Screens.Tests.ps1')
        Write-PhaseDuration 'capture screen regression'
        if ($LASTEXITCODE -ne 0) {
            Write-PhaseDuration 'whole gate'
            exit 1
        }
    }
}

$solutionProjectPaths = @{}
foreach ($line in Get-Content $solution) {
    if ($line -match '^Project\("[^"]+"\)\s*=\s*"[^"]+",\s*"([^"]+\.csproj)"') {
        $relativePath = $Matches[1].Replace('\', [IO.Path]::DirectorySeparatorChar)
        $projectPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $relativePath))
        if ($projectPath.StartsWith($testsRoot, [StringComparison]::OrdinalIgnoreCase)) {
            $solutionProjectPaths[$projectPath] = $true
        }
    }
}

$testProjects = @()
foreach ($projectFile in Get-ChildItem -LiteralPath $testsRoot -Filter '*.csproj' -File -Recurse) {
    $projectPath = [IO.Path]::GetFullPath($projectFile.FullName)
    [xml] $projectDocument = [IO.File]::ReadAllText($projectPath)
    $testProjectNode = $projectDocument.SelectSingleNode("//*[local-name()='IsTestProject']")
    if (-not $testProjectNode -or $testProjectNode.InnerText.Trim() -ne 'true') { continue }
    if (-not $solutionProjectPaths.ContainsKey($projectPath)) {
        throw "Test project is not listed in Motif.sln: $projectPath"
    }
    $shardNode = $projectDocument.SelectSingleNode("//*[local-name()='MotifTestShards']")
    $shards = 1
    if ($shardNode -and -not [int]::TryParse($shardNode.InnerText.Trim(), [ref] $shards)) { $shards = 0 }
    if ($shards -lt 1) { throw "MotifTestShards must be a positive whole number in $projectPath" }
    $levelNode = $projectDocument.SelectSingleNode("//*[local-name()='MotifTestDefaultLevel']")
    $defaultLevel = if ($levelNode) { $levelNode.InnerText.Trim() } else { '' }
    if ($defaultLevel -notin @('Unit', 'Integration', 'System')) {
        throw "MotifTestDefaultLevel must be Unit, Integration or System in $projectPath"
    }
    if (-not $All -and -not $System -and $defaultLevel -eq 'System') { continue }
    if ($Project -and $projectFile.BaseName -ne $Project) { continue }
    $testProjects += [pscustomobject]@{
        Name = $projectFile.BaseName; Path = $projectPath; Shards = $shards; DefaultLevel = $defaultLevel
        Assembly = Join-Path (Join-Path $repoRoot "bin/$Configuration/tests") "$($projectFile.BaseName).dll"
    }
}

if ($testProjects.Count -eq 0) {
    if ($Project) { throw "No selected test project named '$Project' was found under tests/ and listed in Motif.sln." }
    throw 'No test projects were found under tests/ and listed in Motif.sln.'
}
$duplicateNames = @($testProjects | Group-Object Name | Where-Object Count -gt 1)
if ($duplicateNames.Count -gt 0) { throw "Test project names must be unique: $($duplicateNames.Name -join ', ')" }
Import-Module (Join-Path $repoRoot 'tools/MotifTestRunPlan.psm1') -Force
$availableProcessors = [Environment]::ProcessorCount
# Sized so four suites side by side do not oversubscribe the machine; see the help text.
$projectConcurrency = [Math]::Max(2, [int][Math]::Floor($availableProcessors / 6))
# The biggest projects start first, so the throttle does not leave one long shard running alone at the end.
$testRuns = @(foreach ($testProject in @($testProjects | Sort-Object -Property Shards -Descending)) {
    $runShards = $testProject.Shards
    if ($Filter) { $runShards = 1 }
    elseif (-not $All) { $runShards = [Math]::Max(1, [int][Math]::Ceiling($runShards / 2.0)) }
    for ($index = 0; $index -lt $runShards; $index++) {
        $label = if ($runShards -eq 1) { $testProject.Name } else { "$($testProject.Name).shard$index" }
        [pscustomobject]@{
            Project = $testProject.Name; Label = $label; Assembly = $testProject.Assembly
            Shard = if ($runShards -eq 1) { '' } else { "$index/$runShards" }
        }
    }
})
# Sized from the cap, not the run count, so a one-project run still leaves room for other worktrees' suites.
$testThreads = Get-MotifTestThreadCount -ProjectConcurrency $projectConcurrency -ProcessorCount $availableProcessors
$projectConcurrency = [Math]::Min($testRuns.Count, $projectConcurrency)

$binRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'bin')) + [IO.Path]::DirectorySeparatorChar
$resultsRoot = [IO.Path]::GetFullPath((Join-Path (Join-Path $repoRoot 'bin') (Join-Path $Configuration 'test-results')))
if (-not $resultsRoot.StartsWith($binRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Results path must stay under the repository bin directory: $resultsRoot"
}
if (Test-Path $resultsRoot) { Remove-Item $resultsRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $resultsRoot | Out-Null

# Held until this script exits, so a machine running many worktrees runs only a few suites at once.
$phaseClocks['test slot wait'] = [Diagnostics.Stopwatch]::StartNew()
$testSlot = Enter-MotifTestSlot -Directory $testSlotDirectory
Write-PhaseDuration 'test slot wait'

# build.ps1 stages the pinned parser beside the tests; use it unless the caller named one explicitly.
if (-not $env:MOTIF_PANGLOSS_EXE) {
    $stagedParser = Join-Path $repoRoot "bin/$Configuration/$(if ($IsWindows) { 'pangloss.exe' } else { 'pangloss' })"
    if (Test-Path -LiteralPath $stagedParser -PathType Leaf) {
        $stagedParser = [IO.Path]::GetFullPath($stagedParser)
        $env:MOTIF_PANGLOSS_EXE = $stagedParser
        if (-not $env:MOTIF_PANGLOSS_SERVE_EXE) { $env:MOTIF_PANGLOSS_SERVE_EXE = $stagedParser }
        Write-Host "Using the staged parser for PanGloss-dependent tests: $stagedParser"
    }
}

$coverageAdapter = ''
$coverageSettings = ''
if ($Coverage) {
    Import-Module (Join-Path $repoRoot 'tools/MotifCoverage.psm1') -Force
    Write-Step 'coverage collector'
    $coverageAdapter = Install-MotifCoverageCollector -RepoRoot $repoRoot
    $coverageSettings = Join-Path $repoRoot 'tools/coverage/coverage.runsettings'
}

$phaseClocks['test artifact preparation'] = [Diagnostics.Stopwatch]::StartNew()
$selectedProjectNames = @($testRuns | Select-Object -ExpandProperty Project -Unique)
$artifactRequirements = Get-MotifTestArtifactRequirements -All:$All -System:$System `
    -SelectedProjects $selectedProjectNames -Filter $Filter
$preparePortable = $artifactRequirements.PortableWorkerPackage
$prepareExplainedWordCard = $artifactRequirements.ExplainedWordCardFixture
$prepareTestArtifacts = Join-Path $repoRoot 'tools/Prepare-TestArtifacts.ps1'
if ($preparePortable -or $prepareExplainedWordCard) {
    if (-not (Test-Path -LiteralPath $prepareTestArtifacts -PathType Leaf)) {
        throw "Required test artifact preparation script is missing: $prepareTestArtifacts"
    }

    Write-Step 'prepare selected test artifacts'
    $prepareParameters = @{
        Configuration = $Configuration
        PortableWorkerPackage = [bool] $preparePortable
        ExplainedWordCardFixture = [bool] $prepareExplainedWordCard
    }
    & $prepareTestArtifacts @prepareParameters
    if ($LASTEXITCODE -ne 0) {
        throw "Prepare-TestArtifacts.ps1 failed with exit code $LASTEXITCODE."
    }
}
Write-PhaseDuration 'test artifact preparation'

Write-Step ("dotnet test ($levelSelection levels, $($testProjects.Count) projects in $($testRuns.Count) processes, " +
    "up to $projectConcurrency at a time, $(if ($testThreads -gt 0) { "$testThreads xUnit threads each" } else { 'default xUnit threads' }); " +
    "$availableProcessors processors)")
$clock = [Diagnostics.Stopwatch]::StartNew()
$phaseClocks['test processes'] = $clock
$jobs = foreach ($run in $testRuns) {
    $projectResults = Join-Path $resultsRoot $run.Label
    $projectLog = Join-Path $resultsRoot "$($run.Label).log"
    $projectErrorLog = Join-Path $resultsRoot "$($run.Label).stderr.log"
    # A private standard input: git and python stall at startup on a shared pipe that something is reading.
    $projectInput = Join-Path $resultsRoot "$($run.Label).stdin"
    $inputToken = "$($run.Label)-$([guid]::NewGuid().ToString('N'))"
    [IO.File]::WriteAllText($projectInput, $inputToken)
    Start-ThreadJob -Name $run.Label -ThrottleLimit $projectConcurrency `
        -ArgumentList $run.Label, $run.Assembly, $run.Shard, $Filter, $levelSelection, $Configuration,
            $projectResults, $projectLog, $projectErrorLog, $projectInput, $inputToken, $coverageAdapter,
            $coverageSettings, $testThreads -ScriptBlock {
        param($name, $assemblyPath, $shard, $filter, $levels, $configuration, $projectResults, $log, $errorLog,
            $inputPath, $inputToken, $coverageAdapter, $coverageSettings, $threads)
        $projectClock = [Diagnostics.Stopwatch]::StartNew()
        $arguments = @(
            'test'
            ('"{0}"' -f $assemblyPath)
            '--configuration'
            $configuration
            '--nologo'
            '--no-build'
            '--results-directory'
            ('"{0}"' -f $projectResults)
            '--logger'
            ('"trx;LogFileName={0}.trx"' -f $name)
            # A hung test fails and names itself in the blame sequence file instead of stalling the run.
            '--blame-hang-timeout'
            '10m'
            '--blame-hang-dump-type'
            'none'
        )
        if ($filter) { $arguments += @('--filter', ('"{0}"' -f $filter)) }
        if ($coverageAdapter) {
            $arguments += @('--collect', '"XPlat Code Coverage"', '--test-adapter-path', ('"{0}"' -f $coverageAdapter),
                '--settings', ('"{0}"' -f $coverageSettings))
        }
        # Passed to the test host alone: a thread job's own environment is shared with every other job.
        if ($shard) { $arguments += @('--environment', ('"MOTIF_TEST_SHARD={0}"' -f $shard)) }
        $arguments += @('--environment', ('"MOTIF_TEST_LEVELS={0}"' -f $levels))
        $arguments += @('--environment', ('"TESTINGPLATFORM_TELEMETRY_OPTOUT={0}"' -f $env:TESTINGPLATFORM_TELEMETRY_OPTOUT))
        $arguments += @('--environment', ('"MOTIF_TEST_STDIN_TOKEN={0}"' -f $inputToken))
        # RunSettings arguments follow a literal -- and must come last; xUnit hands them to the sharded framework.
        if ($threads -gt 0) { $arguments += @('--', ('xUnit.MaxParallelThreads={0}' -f $threads)) }
        # Without -NoNewWindow, Start-Process gives every project its own console window for the whole run.
        $process = Start-Process -FilePath 'dotnet' -ArgumentList $arguments -PassThru -NoNewWindow `
            -RedirectStandardInput $inputPath -RedirectStandardOutput $log -RedirectStandardError $errorLog
        $summarySeenAt = $null
        $stopReason = $null
        while (-not $process.HasExited) {
            if ($null -eq $summarySeenAt) {
                $summary = @(Select-String -Path @($log, $errorLog) `
                    -Pattern '^\s*(Passed!|Failed!|Test Run Successful|Test Run Failed)' `
                    -ErrorAction SilentlyContinue | Select-Object -First 1)
                if ($summary.Count -gt 0) { $summarySeenAt = [datetime]::UtcNow }
            }
            if ($null -ne $summarySeenAt -and ([datetime]::UtcNow - $summarySeenAt).TotalSeconds -ge 30) {
                $stopReason = 'the test summary was written but dotnet did not exit within 30 seconds'
            }
            if ($stopReason) {
                try { $process.Kill($true); $process.WaitForExit() } catch { }
                break
            }
            [void] $process.WaitForExit(1000)
        }
        [pscustomobject]@{
            Name = $name
            ExitCode = if ($stopReason) { 124 } elseif ($process.HasExited) { $process.ExitCode } else { 125 }
            Seconds = $projectClock.Elapsed.TotalSeconds
            Log = $log
            ErrorLog = $errorLog
            ResultsDirectory = $projectResults
            StopReason = $stopReason
        }
    }
}
$outcomes = @($jobs | Receive-Job -Wait -AutoRemoveJob | Sort-Object Name)
Write-PhaseDuration 'test processes'

$failed = $false
$totals = @{ total = 0; passed = 0; failed = 0; notExecuted = 0 }
$skipCategories = [ordered]@{ platform = 0; capture = 0; harness = 0; 'parser-absent' = 0; capability = 0; other = 0 }
$missingParserTests = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($outcome in $outcomes) {
    $counters = @{ total = 0; passed = 0; failed = 0; notExecuted = 0 }
    foreach ($trx in Get-ChildItem $outcome.ResultsDirectory -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue) {
        # Counted from the results: the TRX summary counters leave xUnit's skips out of notExecuted.
        [xml] $trxDocument = Get-Content $trx.FullName -Raw
        foreach ($result in $trxDocument.SelectNodes("//*[local-name()='UnitTestResult']")) {
            $counters.total++
            if ($result.outcome -eq 'NotExecuted' -and $result.OuterXml -match '(?i)pangloss not found') {
                [void] $missingParserTests.Add([string] $result.testName)
            }
            switch ($result.outcome) {
                'Passed' { $counters.passed++ }
                'NotExecuted' {
                    $counters.notExecuted++
                    $skipText = $result.OuterXml
                    $skipCategory = if ($skipText -match '(?i)pangloss not found|RealParserFactAttribute') { 'parser-absent' }
                    elseif ($skipText -match '(?i)screen.?shot|walkthrough capture|MOTIF_(?:SCREENSHOTS|WALKTHROUGH)') { 'capture' }
                    elseif ($skipText -match '(?i)not available on (?:Windows|Linux|macOS)|only available on (?:Windows|Linux|macOS)|requires Administrator elevation') { 'platform' }
                    elseif ($skipText -match '(?i)No Python 3 interpreter|helper.*(?:missing|not found)|tool.*not installed') { 'harness' }
                    elseif ($skipText -match '(?i)capabilit|not supported|requires .* feature') { 'capability' }
                    else { 'other' }
                    $skipCategories[$skipCategory]++
                }
                default { $counters.failed++ }
            }
        }
    }
    foreach ($key in @($totals.Keys)) { $totals[$key] += $counters[$key] }

    $stale = $counters.total -eq 0
    $ok = $outcome.ExitCode -eq 0 -and -not $stale
    $line = '  {0,-36} {1,6:N1} s  passed {2,5}  failed {3,3}  skipped {4,3}' -f $outcome.Name, $outcome.Seconds,
        $counters.passed, $counters.failed, $counters.notExecuted
    Write-Host $line -ForegroundColor $(if ($ok) { 'Gray' } else { 'Red' })
    if ($stale) { Write-Host '    reported zero test results (a shard with none means too many shards)' -ForegroundColor Red }
    if (-not $ok) {
        $failed = $true
        if ($outcome.StopReason) { Write-Host "    stopped: $($outcome.StopReason)" -ForegroundColor Red }
        Write-Host "    log: $($outcome.Log)" -ForegroundColor Red
        $failureDetails = foreach ($log in @($outcome.Log, $outcome.ErrorLog)) {
            Get-Content $log -ErrorAction SilentlyContinue |
                Select-String -Pattern '^\s*(Failed|Error Message|\[xUnit.*\] .*FAIL)' -Context 0, 6 |
                ForEach-Object { $_ }
        }
        $failureDetails | Select-Object -First 20 | ForEach-Object {
            Write-Host "    $($_.Line)"
            $_.Context.PostContext | ForEach-Object { Write-Host "    $_" }
        }
    }
}
if ($totals.notExecuted -gt 0) {
    $categorySummary = @($skipCategories.GetEnumerator() | Where-Object Value -gt 0 |
        ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', '
    Write-Host "  skip reasons: $categorySummary"
}
Write-Host ('  {0,-36} {1,6:N1} s  passed {2,5}  failed {3,3}  skipped {4,3}  (total {5})' -f 'all projects',
    $clock.Elapsed.TotalSeconds, $totals.passed, $totals.failed, $totals.notExecuted, $totals.total)
if ($missingParserTests.Count -gt 0) {
    Write-Host "Parser validation incomplete: $($missingParserTests.Count) PanGloss-dependent test(s) skipped because pangloss was not found." -ForegroundColor Yellow
}
if ($Coverage) {
    $coverageSummary = Merge-MotifCoverage -ResultsDirectory $resultsRoot -OutputDirectory (Join-Path $resultsRoot 'coverage')
    Write-Host ('  line coverage: Parsimony and Retirement folders {0}% ({1} of {2} lines); per assembly in {3}' -f
        $coverageSummary.parsimony.percent, $coverageSummary.parsimony.covered, $coverageSummary.parsimony.total,
        (Join-Path $resultsRoot 'coverage/coverage-summary.md'))
}
Write-PhaseDuration 'whole gate'

if ($failed) {
    Write-Host ''
    Write-Host 'Tests failed.' -ForegroundColor Red
    exit 1
}

Write-Host ''
if ($All) { Write-Host 'Tests OK: full suite.' -ForegroundColor Green }
elseif ($System) { Write-Host 'Tests OK: System subset.' -ForegroundColor Green }
else { Write-Host 'Tests OK: Unit+Integration subset; default run is not a merge gate.' -ForegroundColor Green }
exit 0
}
finally {
    if ($testSlot) { $testSlot.Dispose() }
    try {
        if (-not $NoProcessSweep) {
            Stop-MotifStaleProcesses -RepoRoot $repoRoot -LockHandle $processLock
        }
    }
    finally { Exit-MotifWorktreeLock -LockHandle $processLock }
}
