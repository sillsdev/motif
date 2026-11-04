Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'MotifShardVerdict.psm1') -Force

function Assert-True([bool] $Condition, [string] $Message) { if (-not $Condition) { throw $Message } }

$root = Join-Path ([IO.Path]::GetTempPath()) ("MotifShardVerdict-" + [guid]::NewGuid().ToString('N'))
try {
    # A shard whose host Blame killed: the summary says Failed 0, but the sequence file names the hung test.
    $hung = Join-Path $root 'hung/guid'
    New-Item -ItemType Directory -Path $hung -Force | Out-Null
    Set-Content (Join-Path $hung 'Sequence_1.xml') @'
<?xml version="1.0"?>
<TestSequence>
  <Test Name="SIL.Motif.Tests.App.SwitchProjectWalkthroughTests.Walks" Source="x.dll" Completed="False" />
  <Test Name="SIL.Motif.Tests.App.OtherTests.Fine" Source="x.dll" Completed="True" />
</TestSequence>
'@
    $log = Join-Path $root 'hung.log'
    Set-Content $log @(
        "Data collector 'Blame' message: Inactivity timeout of 10 minutes reached, the test host will be terminated."
        'Passed!  - Failed:     0, Passed:   313, Skipped:     3, Total:   316')
    $reasons = @(Get-MotifShardAbortReasons -ResultsDirectory (Join-Path $root 'hung') -LogPaths @($log) -ExitCode 0)
    Assert-True ($reasons.Count -eq 2) "Expected the hung test and the Blame message, got: $($reasons -join ' | ')"
    Assert-True ($reasons[0] -match 'SwitchProjectWalkthroughTests\.Walks') 'The hung test is not named.'

    # An abort message with no sequence file still fails.
    $abortLog = Join-Path $root 'abort.log'
    Set-Content $abortLog 'The active test run was aborted. Reason: Test host process crashed'
    Assert-True (@(Get-MotifShardAbortReasons -LogPaths @($abortLog)).Count -eq 1) 'A crashed host was not detected.'

    # A non-zero exit and a short TRX each fail.
    Assert-True (@(Get-MotifShardAbortReasons -ExitCode 1).Count -eq 1) 'A non-zero exit was not detected.'
    $short = Join-Path $root 'short'
    New-Item -ItemType Directory -Path $short -Force | Out-Null
    Set-Content (Join-Path $short 'a.trx') '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary outcome="Completed"><Counters total="316" executed="313" /></ResultSummary></TestRun>'
    Assert-True (@(Get-MotifShardAbortReasons -ResultsDirectory $short).Count -eq 1) 'A short TRX was not detected.'

    # A clean shard (the Blame "all tests finished" note, complete sequence/TRX) has no reasons.
    $clean = Join-Path $root 'clean'
    New-Item -ItemType Directory -Path $clean -Force | Out-Null
    Set-Content (Join-Path $clean 'a.trx') '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary outcome="Completed"><Counters total="316" executed="316" /></ResultSummary></TestRun>'
    Set-Content (Join-Path $clean 'Sequence_2.xml') '<TestSequence><Test Name="A" Completed="True" /></TestSequence>'
    $cleanLog = Join-Path $root 'clean.log'
    Set-Content $cleanLog "Data collector 'Blame' message: All tests finished running, Sequence file will not be generated."
    $none = @(Get-MotifShardAbortReasons -ResultsDirectory $clean -LogPaths @($cleanLog))
    Assert-True ($none.Count -eq 0) "A clean shard was flagged: $($none -join ' | ')"
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host 'shard verdict regression passed'
