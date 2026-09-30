<#
  .SYNOPSIS
  The test gate: everything build.ps1 checks, then the whole test suite.

  .DESCRIPTION
  Use this instead of a bare `dotnet test`. It runs build.ps1 first so that a green test run always
  implies clean comments and a clean compile -- one command whose success means the whole thing is
  good, rather than three that have to be remembered in order.

  There is no filter parameter, deliberately. This script exists so that one green run means one
  thing, and a subset cannot mean it. The filter that used to live here excluded every test needing a
  FieldWorks checkout, which is exactly how that dependency survived unexamined for so long: a filter
  nobody questions is where the next one hides. Run `dotnet test --filter` directly when narrowing a
  hunt -- knowing that it skips this gate, which is the point.

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

  .PARAMETER AllowRunningTestHosts
  Proceed even though a test host from an earlier run is still alive. A stale one holds a lock on the
  build output, which stalls the build rather than failing it -- and a stalled build prints nothing, so
  the gate looks like a test that never finishes. This script therefore stops and names the processes
  instead. Pass this only when the running host is a deliberate second run you want to race.

  .EXAMPLE
  ./test.ps1
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Debug',
    [switch] $SkipBuild,
    [switch] $AllowRunningTestHosts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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
# Short-lived, oversubscribed test processes spend CPU on tiering and spinning: 4 suites used 25% less without.
$env:DOTNET_TieredPGO = '0'
$env:DOTNET_ThreadPool_UnfairSemaphoreSpinLimit = '0'
# Shards take classes by recorded seconds, not by hash; tools/Update-TestShardWeights.ps1 renews the file.
$shardWeights = Join-Path $PSScriptRoot 'tests/test-shard-weights.json'
if (Test-Path $shardWeights) { $env:MOTIF_TEST_SHARD_WEIGHTS = $shardWeights }

function Write-Step {
    param([string] $Text)
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Cyan
}

# Only a host running from this checkout's output can lock it; one from another worktree is not ours.
$outputRoot = (Join-Path $repoRoot 'bin') + [IO.Path]::DirectorySeparatorChar
$running = @(Get-Process -Name 'testhost' -ErrorAction SilentlyContinue | Where-Object {
    # A host this user cannot inspect is another session's, and cannot be holding this checkout's output.
    try { $_.Path -and $_.Path.StartsWith($outputRoot, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
})
if ($running.Count -gt 0 -and -not $AllowRunningTestHosts) {
    Write-Host ''
    Write-Host "A test host from an earlier run is still alive (PID $($running.Id -join ', '))." -ForegroundColor Red
    Write-Host 'It holds a lock on the build output, so this gate would stall with no output at all.'
    Write-Host "Stop it and run again:  Stop-Process -Id $($running.Id -join ',') -Force"
    Write-Host 'Or pass -AllowRunningTestHosts to proceed anyway.'
    exit 1
}

if ($SkipBuild) {
    Write-Step 'build gate -- SKIPPED (-SkipBuild)'
}
else {
    & pwsh -NoProfile -File (Join-Path $repoRoot 'build.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { exit 1 }
}

Write-Step 'offline restore regression'
& pwsh -NoProfile -File (Join-Path $repoRoot 'tools/OfflineRestore.Tests.ps1')
if ($LASTEXITCODE -ne 0) { exit 1 }

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
    $testProjects += [pscustomobject]@{ Name = $projectFile.BaseName; Path = $projectPath; Shards = $shards }
}

if ($testProjects.Count -eq 0) { throw 'No test projects were found under tests/ and listed in Motif.sln.' }
$duplicateNames = @($testProjects | Group-Object Name | Where-Object Count -gt 1)
if ($duplicateNames.Count -gt 0) { throw "Test project names must be unique: $($duplicateNames.Name -join ', ')" }
$availableProcessors = [Environment]::ProcessorCount
# Sized so four suites side by side do not oversubscribe the machine; see the help text.
$projectConcurrency = [Math]::Max(2, [int][Math]::Floor($availableProcessors / 6))
# The biggest projects start first, so the throttle does not leave one long shard running alone at the end.
$testRuns = @(foreach ($project in @($testProjects | Sort-Object -Property Shards -Descending)) {
    for ($index = 0; $index -lt $project.Shards; $index++) {
        $label = if ($project.Shards -eq 1) { $project.Name } else { "$($project.Name).shard$index" }
        [pscustomobject]@{
            Project = $project.Name; Label = $label; Path = $project.Path
            Shard = if ($project.Shards -eq 1) { '' } else { "$index/$($project.Shards)" }
        }
    }
})
$projectConcurrency = [Math]::Min($testRuns.Count, $projectConcurrency)

$binRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'bin')) + [IO.Path]::DirectorySeparatorChar
$resultsRoot = [IO.Path]::GetFullPath((Join-Path (Join-Path $repoRoot 'bin') (Join-Path $Configuration 'test-results')))
if (-not $resultsRoot.StartsWith($binRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Results path must stay under the repository bin directory: $resultsRoot"
}
if (Test-Path $resultsRoot) { Remove-Item $resultsRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $resultsRoot | Out-Null

Write-Step ("dotnet test (full suite, $($testProjects.Count) projects in $($testRuns.Count) processes, " +
    "up to $projectConcurrency at a time; $availableProcessors processors)")
$clock = [Diagnostics.Stopwatch]::StartNew()
$jobs = foreach ($run in $testRuns) {
    $projectResults = Join-Path $resultsRoot $run.Label
    $projectLog = Join-Path $resultsRoot "$($run.Label).log"
    $projectErrorLog = Join-Path $resultsRoot "$($run.Label).stderr.log"
    Start-ThreadJob -Name $run.Label -ThrottleLimit $projectConcurrency `
        -ArgumentList $run.Label, $run.Path, $run.Shard, $Configuration, $projectResults, $projectLog, $projectErrorLog -ScriptBlock {
        param($name, $projectPath, $shard, $configuration, $projectResults, $log, $errorLog)
        $projectClock = [Diagnostics.Stopwatch]::StartNew()
        $arguments = @(
            'test'
            ('"{0}"' -f $projectPath)
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
        # Passed to the test host alone: a thread job's own environment is shared with every other job.
        if ($shard) { $arguments += @('--environment', ('"MOTIF_TEST_SHARD={0}"' -f $shard)) }
        # Without -NoNewWindow, Start-Process gives every project its own console window for the whole run.
        $process = Start-Process -FilePath 'dotnet' -ArgumentList $arguments -PassThru -NoNewWindow `
            -RedirectStandardOutput $log -RedirectStandardError $errorLog
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

$failed = $false
$totals = @{ total = 0; passed = 0; failed = 0; notExecuted = 0 }
foreach ($outcome in $outcomes) {
    $counters = @{ total = 0; passed = 0; failed = 0; notExecuted = 0 }
    foreach ($trx in Get-ChildItem $outcome.ResultsDirectory -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue) {
        # Counted from the results: the TRX summary counters leave xUnit's skips out of notExecuted.
        foreach ($result in ([xml](Get-Content $trx.FullName -Raw)).TestRun.Results.UnitTestResult) {
            $counters.total++
            switch ($result.outcome) {
                'Passed' { $counters.passed++ }
                'NotExecuted' { $counters.notExecuted++ }
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
Write-Host ('  {0,-36} {1,6:N1} s  passed {2,5}  failed {3,3}  skipped {4,3}  (total {5})' -f 'all projects',
    $clock.Elapsed.TotalSeconds, $totals.passed, $totals.failed, $totals.notExecuted, $totals.total)

if ($failed) {
    Write-Host ''
    Write-Host 'Tests failed.' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'Tests OK: full suite.' -ForegroundColor Green
exit 0
