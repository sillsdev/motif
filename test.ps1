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

  The suite runs as several concurrent test processes ("shards"), each taking a slice of the test
  namespaces. The reason is LibLCM: bootstrapping two caches at once inside one process races, so every
  class that opens one shares a single serialized xUnit collection, and xUnit runs such a collection
  alone after everything else. A second process has its own LibLCM statics and cannot race the first,
  so splitting by process is what lets that serialized work use more than one core. The last shard is
  the complement of the others, so the shards always cover the whole suite: a new namespace lands in
  it without anyone editing the table. A named shard that matches no test fails the run, because it
  means the table has gone stale.

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

$repoRoot = $PSScriptRoot
$solution = Join-Path $repoRoot 'Motif.sln'
$env:MOTIF_DEVELOPER_COMMANDS = '1'

function Write-Step {
    param([string] $Text)
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Cyan
}

# Only a host running from this checkout's output can lock it; one from another worktree is not ours.
$outputRoot = Join-Path $repoRoot 'bin'
$running = @(Get-Process -Name 'testhost' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($outputRoot, [StringComparison]::OrdinalIgnoreCase) })
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

# Balanced from measured durations of the serialized LcmCache classes; the complement shard takes the rest.
$namedShards = [ordered]@{
    'cli-host'             = @('Cli', 'Host')
    'integration-handoff'  = @('Integration', 'Handoff')
    'commands-worker'      = @('Commands', 'Worker')
}

function Get-NamespaceFilter {
    param([string[]] $Namespaces, [string] $Operator, [string] $Joiner)
    ($Namespaces | ForEach-Object { "FullyQualifiedName$Operator" + "SIL.Motif.Tests.$_." }) -join $Joiner
}

$shards = [ordered]@{}
foreach ($name in $namedShards.Keys) {
    $shards[$name] = Get-NamespaceFilter $namedShards[$name] '~' '|'
}
$shards['rest'] = Get-NamespaceFilter @($namedShards.Values | ForEach-Object { $_ }) '!~' '&'

$resultsRoot = Join-Path $repoRoot "bin\$Configuration\test-results"
if (Test-Path $resultsRoot) { Remove-Item $resultsRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $resultsRoot | Out-Null

Write-Step "dotnet test (full suite, $($shards.Count) concurrent shards)"
$clock = [Diagnostics.Stopwatch]::StartNew()
$jobs = foreach ($name in $shards.Keys) {
    Start-ThreadJob -Name $name -ThrottleLimit $shards.Count -ArgumentList $name, $shards[$name] -ScriptBlock {
        param($name, $filter)
        $log = Join-Path $using:resultsRoot "$name.log"
        $shardClock = [Diagnostics.Stopwatch]::StartNew()
        & dotnet test $using:solution --configuration $using:Configuration --nologo --no-build `
            --filter $filter --results-directory (Join-Path $using:resultsRoot $name) `
            --logger "trx;LogFileName=$name.trx" *> $log
        [pscustomobject]@{ Name = $name; ExitCode = $LASTEXITCODE; Seconds = $shardClock.Elapsed.TotalSeconds; Log = $log }
    }
}
$outcomes = @($jobs | Receive-Job -Wait -AutoRemoveJob)

$failed = $false
$totals = @{ total = 0; passed = 0; failed = 0; notExecuted = 0 }
foreach ($outcome in $outcomes) {
    $counters = @{ total = 0; passed = 0; failed = 0; notExecuted = 0 }
    foreach ($trx in Get-ChildItem (Join-Path $resultsRoot $outcome.Name) -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue) {
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

    $stale = $outcome.Name -ne 'rest' -and $counters.total -eq 0
    $ok = $outcome.ExitCode -eq 0 -and -not $stale
    $line = '  {0,-22} {1,6:N1} s  passed {2,5}  failed {3,3}  skipped {4,3}' -f $outcome.Name, $outcome.Seconds,
        $counters.passed, $counters.failed, $counters.notExecuted
    Write-Host $line -ForegroundColor $(if ($ok) { 'Gray' } else { 'Red' })
    if ($stale) { Write-Host "    matched no tests: the shard table in test.ps1 names a namespace that is gone" -ForegroundColor Red }
    if (-not $ok) {
        $failed = $true
        Write-Host "    log: $($outcome.Log)" -ForegroundColor Red
        Get-Content $outcome.Log | Select-String -Pattern '^\s*(Failed|Error Message|\[xUnit.*\] .*FAIL)' -Context 0, 6 |
            Select-Object -First 20 | ForEach-Object { Write-Host "    $($_.Line)"; $_.Context.PostContext | ForEach-Object { Write-Host "    $_" } }
    }
}
Write-Host ('  {0,-22} {1,6:N1} s  passed {2,5}  failed {3,3}  skipped {4,3}  (total {5})' -f 'all shards',
    $clock.Elapsed.TotalSeconds, $totals.passed, $totals.failed, $totals.notExecuted, $totals.total)

if ($failed) {
    Write-Host ''
    Write-Host 'Tests failed.' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'Tests OK: full suite.' -ForegroundColor Green
exit 0
