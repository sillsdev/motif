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

  Each test project runs in one or more separate processes, with concurrent processes capped at 0.6 of the
  available processor count and at one per 2 GB of memory (at least two, at most twelve; MOTIF_TEST_PROCESSES sets it). The gate is bound by
  total process time, and App tests run one at a time inside a process (one UI dispatcher), so the App project is
  split into many shards that run side by side. Machine-wide test slots still bound how many suites run at once. Classes that open a LibLCM cache share a serialized xUnit collection within their
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

  .PARAMETER Changed
  Run only the test classes the current diff can affect, for a landing that should take a minute or two. The diff is
  `git diff <Base>` plus untracked files. A class is selected when the coverage map says it executes a changed
  source file, when it is declared in a changed or added test file, or when it is in the smoke set
  (tests/test-smoke-classes.txt); every selected class runs at every level, as under -All. Each selected class is
  printed with its seconds and the reason. Anything the map cannot vouch for runs the full suite instead, with the
  reason printed: no map, a map from another history or more than -MaxMapAge commits old, a build, project or
  tooling file, new shared test code, a source file outside src/ and tests/, or a data file no source names. The
  tooling regression scripts run only in that fallback. -All stays the full gate; docs/testing-changed-files.md has
  the rules.

  .PARAMETER Base
  The ref -Changed diffs against. Defaults to the merge-base of HEAD with origin/main, then with main.

  .PARAMETER Explain
  With -Changed, print the selection and its reasons, and the estimated time, then stop: nothing is built or run.

  .PARAMETER BuildMap
  Build the coverage map -Changed reads: one full-suite run under coverlet (restored on demand; no project references
  it) in which every test process runs one class at a time in a private copy of its test folder, and
  SIL.Motif.Tests.Support records which source files each class executed. It takes about as long as the full gate
  plus instrumentation, so run it nightly or on request, not per landing. The map goes to
  bin/.cache/test-map/coverage-map.json, keyed by HEAD, or to -MapPath.

  .PARAMETER MapPath
  The coverage map file -Changed reads and -BuildMap writes.

  .PARAMETER MaxMapAge
  The most commits the map's commit may trail HEAD before -Changed runs everything. Default 40.

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
    [switch] $Changed,
    [string] $Base,
    [switch] $Explain,
    [switch] $BuildMap,
    [string] $MapPath,
    [int] $MaxMapAge = 40,
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
# Test processes copy the seeded LibLCM masters from here; names carry an input hash, old folders go.
$fixtureCache = Join-Path $repoRoot "bin/.cache/test-fixtures/$Configuration"
if (Test-Path -LiteralPath $fixtureCache) {
    Get-ChildItem -LiteralPath $fixtureCache -Directory |
        Where-Object { $_.LastWriteTimeUtc -lt [datetime]::UtcNow.AddDays(-3) } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}
$env:MOTIF_TEST_FIXTURE_CACHE = $fixtureCache
if ($All -and $System) { throw 'Choose either -All or -System.' }
if ($Changed -and ($All -or $System -or $Project -or $Filter -or $BuildMap)) {
    throw '-Changed selects its own classes; do not combine it with -All, -System, -Project, -Filter or -BuildMap.'
}
if ($BuildMap -and ($System -or $Project -or $Filter -or $Coverage)) {
    throw '-BuildMap runs the whole suite; do not combine it with -System, -Project, -Filter or -Coverage.'
}
if ($Explain -and -not $Changed) { throw '-Explain belongs to -Changed.' }
if ($BuildMap) {
    $All = [switch]::Present
    Import-Module (Join-Path $repoRoot 'tools/MotifChangedTests.psm1') -Force
    Assert-MotifSymlinkSupport -Directory (Join-Path $repoRoot 'bin')
}
$levelSelection = if ($All -or $Changed) { 'All' } elseif ($System) { 'System' } else { 'Unit,Integration' }

if ($Filter -and -not $Project) { throw '-Filter requires -Project.' }

# -Changed picks classes from the diff and the coverage map; unknown means everything.
$changedSelection = $null
$changedPlan = $null
$defaultMapPath = Join-Path $repoRoot 'bin/.cache/test-map/coverage-map.json'
if ($Changed) {
    Import-Module (Join-Path $repoRoot 'tools/MotifChangedTests.psm1') -Force
    $changedSelection = Select-MotifChangedTests -RepoRoot $repoRoot -Base $Base -MaxMapAge $MaxMapAge `
        -MapPath $(if ($MapPath) { $MapPath } else { $defaultMapPath }) `
        -SmokePath (Join-Path $repoRoot 'tests/test-smoke-classes.txt')
    $changedPlan = Get-MotifChangedPlan -Selection $changedSelection `
        -WeightsPath (Join-Path $repoRoot 'tests/test-shard-weights.json')
    Write-MotifChangedSelection -Selection $changedSelection -Plan $changedPlan
    if ($Explain) { return }
    if ($changedSelection.Full) { $All = [switch]::Present }
}
$changedClasses = $Changed -and -not $changedSelection.Full

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
    Write-Step 'grammar-data worktree guard'
    & pwsh -NoProfile -File (Join-Path $repoRoot 'tools/Check-GrammarData.ps1') -Mode Worktree -RepositoryRoot $repoRoot
    $buildExitCode = $LASTEXITCODE
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

# A -Project run is an iteration; a full run, which CI and landing use, runs the regressions beside the tests.
$regressions = @()
if (-not $Project -and -not $changedClasses) {
    $regressions = @(
        @{ Name = 'offline restore'; Title = 'offline restore regression'; Script = 'OfflineRestore.Tests.ps1' }
        @{ Name = 'tool environment regression'; Title = 'tool environment regression'; Script = 'MotifToolEnvironment.Tests.ps1' }
        @{ Name = 'resource sizing regression'; Title = 'node, slot and thread sizing regression'; Script = 'MotifTestResources.Tests.ps1' }
        @{ Name = 'artifact plan regression'; Title = 'test artifact plan regression'; Script = 'Test-MotifTestRunPlan.Tests.ps1' }
        @{ Name = 'release version regression'; Title = 'release version regression'; Script = 'MotifReleaseVersion.Tests.ps1' }
        @{ Name = 'PanGloss interface regression'; Title = 'PanGloss interface regression'; Script = 'Test-PanGlossInterfaces.Tests.ps1'; Configured = $true }
        @{ Name = 'artifact preparation host regression'; Title = 'artifact preparation host regression'; Script = 'Test-PrepareTestArtifacts.Tests.ps1'; Configured = $true }
        @{ Name = 'publication policy regression'; Title = 'publication policy regression'; Script = 'MotifReleasePublication.Tests.ps1' }
        @{ Name = 'help golden regression'; Title = 'help golden gate regression'; Script = 'MotifHelpGolden.Tests.ps1' }
        @{ Name = 'changed-files selection regression'; Title = 'changed-files selection regression'; Script = 'MotifChangedTests.Tests.ps1' }
    )
    if ($All) {
        $regressions += @{ Name = 'capture screen regression'; Title = 'capture screenshot tooling regression'; Script = 'Capture-Screens.Tests.ps1' }
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
    if (-not $All -and -not $System -and -not $changedClasses -and $defaultLevel -eq 'System') { continue }
    if ($changedClasses -and -not $changedPlan.Projects.Contains($projectFile.BaseName)) { continue }
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
# 0.6 of the processors, one per 2 GB, at most twelve; see the help text. MOTIF_TEST_PROCESSES overrides.
$projectConcurrency = Get-MotifTestProcessConcurrency -ProcessorCount $availableProcessors
# The biggest projects start first, so the throttle does not leave one long shard running alone at the end.
$testRuns = @(foreach ($testProject in @($testProjects | Sort-Object -Property Shards -Descending)) {
    $runShards = $testProject.Shards
    if ($changedClasses) { $runShards = [Math]::Min($runShards, $changedPlan.Projects[$testProject.Name].Shards) }
    elseif ($Filter) { $runShards = 1 }
    elseif (-not $All) { $runShards = [Math]::Max(1, [int][Math]::Ceiling($runShards / 2.0)) }
    for ($index = 0; $index -lt $runShards; $index++) {
        $label = if ($runShards -eq 1) { $testProject.Name } else { "$($testProject.Name).shard$index" }
        [pscustomobject]@{
            Project = $testProject.Name; Label = $label; Assembly = $testProject.Assembly; CoverageSettings = ''
            Shard = if ($runShards -eq 1) { '' } else { "$index/$runShards" }
            NeedsPortable = ($testProject.Name -eq 'SIL.Motif.Tests.Cli')
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
$mapDirectory = ''
$classSelectionFile = ''
if ($BuildMap) {
    Import-Module (Join-Path $repoRoot 'tools/MotifCoverage.psm1') -Force
    Import-Module (Join-Path $repoRoot 'tools/MotifChangedTests.psm1') -Force
    Write-Step 'coverage collector (coverage-map build: the whole suite, one class at a time per process)'
    $coverageAdapter = Install-MotifCoverageCollector -RepoRoot $repoRoot
    $mapDirectory = Join-Path $resultsRoot 'coverage-map'
    New-Item -ItemType Directory -Force -Path $mapDirectory | Out-Null
    $coverageSettings = Join-Path $repoRoot 'tools/coverage/map.runsettings'
}
if ($changedClasses) {
    $classSelectionFile = Join-Path $resultsRoot 'class-selection.txt'
    Set-Content -LiteralPath $classSelectionFile -Value @($changedPlan.SelectionEntries)
}
if ($Coverage) {
    Import-Module (Join-Path $repoRoot 'tools/MotifCoverage.psm1') -Force
    Write-Step 'coverage collector'
    $coverageAdapter = Install-MotifCoverageCollector -RepoRoot $repoRoot
    $coverageSettings = Join-Path $repoRoot 'tools/coverage/coverage.runsettings'
}

# This one sweeps processes and kills the test run's, so it finishes first.
if (-not $Project -and -not $changedClasses) {
    $phaseClocks['process hygiene regression'] = [Diagnostics.Stopwatch]::StartNew()
    Write-Step 'worktree process hygiene regression'
    & pwsh -NoProfile -File (Join-Path $repoRoot 'tools/Test-MotifProcessHygiene.Tests.ps1')
    $hygieneExitCode = $LASTEXITCODE
    Write-PhaseDuration 'process hygiene regression'
    if ($hygieneExitCode -ne 0) {
        Write-PhaseDuration 'whole gate'
        exit 1
    }
}
$phaseClocks['test artifact preparation'] = [Diagnostics.Stopwatch]::StartNew()
$selectedProjectNames = @($testRuns | Select-Object -ExpandProperty Project -Unique)
$artifactRequirements = Get-MotifTestArtifactRequirements -All:$All -System:$System `
    -SelectedProjects $selectedProjectNames -Filter $Filter
if ($changedClasses) {
    # Only the classes that read a prepared artifact need it prepared.
    $artifactRequirements = [pscustomobject]@{
        PortableWorkerPackage = $changedPlan.NeedsPortable; ExplainedWordCardFixture = $changedPlan.NeedsExplainedWordCard
    }
}
$preparePortable = $artifactRequirements.PortableWorkerPackage
$prepareExplainedWordCard = $artifactRequirements.ExplainedWordCardFixture
$prepareTestArtifacts = Join-Path $repoRoot 'tools/Prepare-TestArtifacts.ps1'
if ($preparePortable -or $prepareExplainedWordCard) {
    if (-not (Test-Path -LiteralPath $prepareTestArtifacts -PathType Leaf)) {
        throw "Required test artifact preparation script is missing: $prepareTestArtifacts"
    }
}
if ($prepareExplainedWordCard) {
    Write-Step 'prepare selected test artifacts: Explained Word Card fixture'
    & $prepareTestArtifacts -Configuration $Configuration -ExplainedWordCardFixture
    if ($LASTEXITCODE -ne 0) {
        throw "Prepare-TestArtifacts.ps1 failed with exit code $LASTEXITCODE."
    }
}
# Only the Cli process consumes the 50 s of publishes, so they run beside the other tests and it waits.
$portablePrepare = $null
if ($preparePortable) {
    Write-Step 'prepare selected test artifacts: portable Worker package (in the background)'
    $portableInput = Join-Path $resultsRoot 'portable-package.stdin'
    [IO.File]::WriteAllText($portableInput, 'portable-package')
    $portablePrepare = Start-Process -FilePath 'pwsh' -PassThru -NoNewWindow `
        -ArgumentList @('-NoProfile', '-File', ('"{0}"' -f $prepareTestArtifacts), '-Configuration', $Configuration,
            '-PortableWorkerPackage') `
        -RedirectStandardInput $portableInput -RedirectStandardOutput (Join-Path $resultsRoot 'portable-package.log') `
        -RedirectStandardError (Join-Path $resultsRoot 'portable-package.stderr.log')
    [void] $portablePrepare.Handle
}
Write-PhaseDuration 'test artifact preparation'

$regressionRuns = foreach ($regression in $regressions) {
    $scriptPath = Join-Path $repoRoot "tools/$($regression.Script)"
    $regressionArguments = @('-NoProfile', '-File', ('"{0}"' -f $scriptPath))
    if ($regression.ContainsKey('Configured')) { $regressionArguments += @('-Configuration', $Configuration) }
    $regressionName = $regression.Name -replace '\s', '-'
    $regressionInput = Join-Path $resultsRoot "regression-$regressionName.stdin"
    [IO.File]::WriteAllText($regressionInput, "regression-$regressionName")
    $regressionLog = Join-Path $resultsRoot "regression-$regressionName.log"
    $regressionProcess = Start-Process -FilePath 'pwsh' -ArgumentList $regressionArguments -PassThru -NoNewWindow `
        -RedirectStandardInput $regressionInput -RedirectStandardOutput $regressionLog `
        -RedirectStandardError (Join-Path $resultsRoot "regression-$regressionName.stderr.log")
    [void] $regressionProcess.Handle
    [pscustomobject]@{ Regression = $regression; Process = $regressionProcess; Log = $regressionLog }
}

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
    $runSettings = $coverageSettings
    $runAssembly = $run.Assembly
    if ($BuildMap) {
        $runAssembly = New-MotifMapRunDirectory -ProductDirectory (Join-Path $repoRoot "bin/$Configuration") `
            -Destination (Join-Path $repoRoot 'bin') -Label $run.Label -Assembly $run.Assembly
        $runSettings = New-MotifMapRunSettings -Template $coverageSettings -Project $run.Project `
            -TestProjects @($testProjects.Name) -OutputPath (Join-Path $resultsRoot "$($run.Label).runsettings")
    }
    Start-ThreadJob -Name $run.Label -ThrottleLimit $projectConcurrency `
        -ArgumentList $run.Label, $runAssembly, $run.Shard, $Filter, $levelSelection, $Configuration,
            $projectResults, $projectLog, $projectErrorLog, $projectInput, $inputToken, $coverageAdapter,
            $runSettings, $testThreads, $(if ($run.NeedsPortable) { $portablePrepare } else { $null }),
            $mapDirectory, $classSelectionFile -ScriptBlock {
        param($name, $assemblyPath, $shard, $filter, $levels, $configuration, $projectResults, $log, $errorLog,
            $inputPath, $inputToken, $coverageAdapter, $coverageSettings, $threads, $portablePrepare,
            $mapDirectory, $classSelectionFile)
        $projectClock = [Diagnostics.Stopwatch]::StartNew()
        if ($portablePrepare) {
            $portablePrepare.WaitForExit()
            if ($portablePrepare.ExitCode -ne 0) {
                Set-Content -Path $log -Value "The portable Worker package preparation failed with exit code $($portablePrepare.ExitCode); see portable-package.log beside this file."
                return [pscustomobject]@{
                    Name = $name; ExitCode = 1; Seconds = $projectClock.Elapsed.TotalSeconds; Log = $log
                    ErrorLog = $errorLog; ResultsDirectory = $projectResults; StopReason = 'artifact preparation failed'
                }
            }
        }
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
        if ($mapDirectory) { $arguments += @('--environment', ('"MOTIF_TEST_COVERAGE_MAP_DIR={0}"' -f $mapDirectory)) }
        if ($classSelectionFile) { $arguments += @('--environment', ('"MOTIF_TEST_CLASS_SELECTION={0}"' -f $classSelectionFile)) }
        # Avalonia code stays unoptimized without call counting, so the App project turns it on.
        if ($name -like 'SIL.Motif.Tests.App*') {
            $arguments += @('--environment', 'DOTNET_TC_CallCounting=1')
            # Layout allocates short-lived objects: no background GC thread saves CPU.
            $arguments += @('--environment', 'DOTNET_gcConcurrent=0')
        }
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

# The regressions ran beside the tests; their output and verdicts come now, in their old order.
$regressionFailed = $false
foreach ($run in $regressionRuns) {
    $run.Process.WaitForExit()
    Write-Step $run.Regression.Title
    foreach ($log in @($run.Log, ($run.Log -replace '\.log$', '.stderr.log'))) {
        if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log | Write-Host }
    }
    Write-Host ('  {0,-34} done' -f $run.Regression.Name)
    if ($run.Process.ExitCode -ne 0) {
        Write-Host "  $($run.Regression.Name) FAILED with exit code $($run.Process.ExitCode)" -ForegroundColor Red
        $regressionFailed = $true
    }
}

$failed = $regressionFailed
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
if ($BuildMap) {
    $mapTarget = if ($MapPath) { $MapPath } else { $defaultMapPath }
    $mapCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
    if (& git -C $repoRoot status --porcelain --untracked-files=no) {
        Write-Host '  the worktree has uncommitted changes; the map is keyed by HEAD but measures the working tree' -ForegroundColor Yellow
    }
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'bin') -Directory -Filter 'map-run-*' | Remove-Item -Recurse -Force
    $mapResult = Merge-MotifCoverageMap -InputDirectory $mapDirectory -OutputPath $mapTarget -RepoRoot $repoRoot `
        -Commit $mapCommit -ResultsDirectory $resultsRoot
    Write-Host ('  coverage map: {0} classes over {1} source files, commit {2}, written to {3}' -f
        $mapResult.Classes, $mapResult.Files, $mapCommit.Substring(0, 9), $mapResult.Path)
    if ($failed) { Write-Host '  the suite had failures, so the classes that failed have partial coverage' -ForegroundColor Yellow }
}
Write-PhaseDuration 'whole gate'

if ($failed) {
    Write-Host ''
    Write-Host 'Tests failed.' -ForegroundColor Red
    exit 1
}

Write-Host ''
if ($changedClasses -and $changedPlan.ClassCount -eq 0) { Write-Host 'No tests ran: the changes touch only files no test executes (-Changed); the full suite is -All.' -ForegroundColor Yellow }
elseif ($changedClasses) { Write-Host "Tests OK: $($changedPlan.ClassCount) selected classes (-Changed); the full suite is -All." -ForegroundColor Green }
elseif ($BuildMap) { Write-Host 'Coverage map built from a full-suite run.' -ForegroundColor Green }
elseif ($All) { Write-Host 'Tests OK: full suite.' -ForegroundColor Green }
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
