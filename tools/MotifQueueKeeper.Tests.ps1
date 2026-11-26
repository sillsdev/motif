Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'MotifQueueKeeper.psm1') -Force

function Assert-True([bool] $Condition, [string] $Message) { if (-not $Condition) { throw $Message } }

# Log parsing: timestamps, the xUnit.net prefix, theory arguments, duplicates and non-test lines.
$log = @'
2026-11-26T10:00:00.1Z [xUnit.net 00:00:12.34]     SIL.Motif.Tests.Worker.RunnerTests.SweepRuns [FAIL]
2026-11-26T10:00:00.2Z   Failed SIL.Motif.Tests.Worker.RunnerTests.SweepRuns [8 ms]
2026-11-26T10:00:00.3Z [xUnit.net 00:00:12.50]     SIL.Motif.Tests.App.GridTests.Cell(size: 3) [FAIL]
2026-11-26T10:00:00.4Z [xUnit.net 00:00:12.60]     SIL.Motif.Tests.Worker.RunnerTests.SweepRuns [FAIL]
Failed!  - Failed:     2, Passed:   10, Skipped:     0, Total:    12
'@
$names = Get-FailedTestNames -LogText $log
Assert-True ($names.Count -eq 2) "Expected 2 distinct names, got: $($names -join ' | ')"
Assert-True ($names[0] -eq 'SIL.Motif.Tests.Worker.RunnerTests.SweepRuns') 'First name wrong.'
Assert-True ($names[1] -eq 'SIL.Motif.Tests.App.GridTests.Cell') 'Theory arguments were not stripped.'
Assert-True ((Get-FailedTestNames -LogText 'all green').Count -eq 0) 'A clean log must yield no names.'

# Dead runs: only non-completed queue-group runs whose branch is gone.
$runs = @(
    [pscustomobject]@{ id = 1; status = 'in_progress'; head_branch = 'gh-readonly-queue/main/pr-1-aaa' }
    [pscustomobject]@{ id = 2; status = 'queued'; head_branch = 'gh-readonly-queue/main/pr-2-bbb' }
    [pscustomobject]@{ id = 3; status = 'completed'; head_branch = 'gh-readonly-queue/main/pr-3-ccc' }
    [pscustomobject]@{ id = 4; status = 'in_progress'; head_branch = 'cli/feature' }
)
$dead = @(Get-DeadQueueRuns -Runs $runs -LiveRefs @('refs/heads/gh-readonly-queue/main/pr-1-aaa'))
Assert-True ($dead.Count -eq 1 -and $dead[0].id -eq 2) 'Only run 2 is dead.'

# Requeue decision.
$flake = @('Flake: SweepRuns fails under load', 'Timing: SIL.Motif.Tests.App.GridTests.Cell is slow')
$t1 = 'SIL.Motif.Tests.Worker.RunnerTests.SweepRuns'
$t2 = 'SIL.Motif.Tests.App.GridTests.Cell'
$d = Get-RequeueDecision -FailingTests @($t1, $t2) -FlakeTexts $flake
Assert-True $d.Requeue "All named in flake issues must requeue: $($d.Reason)"

$d = Get-RequeueDecision -FailingTests @($t1, 'SIL.Motif.Tests.App.RealTests.Broken') -FlakeTexts $flake
Assert-True (-not $d.Requeue) 'One unknown failing test must block the requeue.'

$d = Get-RequeueDecision -FailingTests @($t1) -FlakeTexts $flake -PrFiles @('tests/W/RunnerTests.cs') -TestFiles @{ $t1 = 'tests/W/RunnerTests.cs' }
Assert-True (-not $d.Requeue -and $d.Reason -match 'changes') 'A failing test in the PR diff must block the requeue.'

$d = Get-RequeueDecision -FailingTests @($t1) -FlakeTexts $flake -RequeuesToday 2
Assert-True (-not $d.Requeue -and $d.Reason -match 'today') 'The daily cap must block the requeue.'

$d = Get-RequeueDecision -FailingTests @($t1) -FlakeTexts @() -QuarantineNames @($t1)
Assert-True $d.Requeue 'A quarantined test must requeue.'

$d = Get-RequeueDecision -FailingTests @() -FlakeTexts $flake
Assert-True (-not $d.Requeue) 'No parsed failures must never requeue.'

# A method name must match as a whole word.
Assert-True (-not (Test-NamedInText -TestName 'A.B.Run' -Text 'Running is slow')) 'Substring of a longer word matched.'

# Eviction: only a trailing failed_checks removal counts.
$ev = @([pscustomobject]@{ __typename = 'AddedToMergeQueueEvent' }, [pscustomobject]@{ __typename = 'RemovedFromMergeQueueEvent'; reason = 'failed_checks' })
Assert-True ((Get-LatestEvictionReason -Events $ev) -eq 'failed_checks') 'Trailing removal not seen.'
Assert-True ($null -eq (Get-LatestEvictionReason -Events @($ev[1], $ev[0]))) 'A later add must clear the eviction.'

Write-Host 'MotifQueueKeeper tests passed'
