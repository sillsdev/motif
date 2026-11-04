Set-StrictMode -Version Latest

# A test host that xUnit's Blame collector aborted (hang or crash) still lets dotnet test print a summary of the
# tests that did run, so "Passed 313 Failed 0" can hide ~150 tests that never ran. This reads the evidence the
# collector leaves behind and returns one reason per finding; an empty result means the host was not aborted.
function Get-MotifShardAbortReasons {
    [CmdletBinding()]
    param(
        [string] $ResultsDirectory,
        [string[]] $LogPaths = @(),
        [int] $ExitCode = 0
    )

    $reasons = [System.Collections.Generic.List[string]]::new()

    if ($ExitCode -ne 0) { $reasons.Add("dotnet test exited with code $ExitCode") }

    # Sequence_<guid>.xml: <Test Name=... Completed="False"/> marks a test that was running when the host died.
    if ($ResultsDirectory -and (Test-Path -LiteralPath $ResultsDirectory)) {
        foreach ($sequence in Get-ChildItem -LiteralPath $ResultsDirectory -Filter 'Sequence_*.xml' -Recurse -ErrorAction SilentlyContinue) {
            try { [xml] $document = Get-Content -LiteralPath $sequence.FullName -Raw } catch {
                $reasons.Add("unreadable Blame sequence file $($sequence.Name)")
                continue
            }
            foreach ($test in $document.SelectNodes("//*[local-name()='Test'][@Completed='False']")) {
                $reasons.Add("hung or killed test: $($test.GetAttribute('Name'))")
            }
        }

        foreach ($trx in Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue) {
            try { [xml] $trxDocument = Get-Content -LiteralPath $trx.FullName -Raw } catch {
                $reasons.Add("unreadable TRX $($trx.Name)")
                continue
            }
            $summary = $trxDocument.SelectSingleNode("//*[local-name()='ResultSummary']")
            if ($summary -and $summary.GetAttribute('outcome') -eq 'Aborted') { $reasons.Add("TRX $($trx.Name) is marked Aborted") }
            $counters = $trxDocument.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
            if ($counters -and $counters.GetAttribute('total') -and $counters.GetAttribute('executed')) {
                $total = [int] $counters.GetAttribute('total')
                $executed = [int] $counters.GetAttribute('executed')
                if ($executed -lt $total) { $reasons.Add("TRX $($trx.Name) is short: $executed of $total tests executed") }
            }
        }
    }

    foreach ($log in $LogPaths) {
        if (-not $log -or -not (Test-Path -LiteralPath $log)) { continue }
        foreach ($line in Get-Content -LiteralPath $log -ErrorAction SilentlyContinue) {
            if ($line -match "Data collector 'Blame' message:" -and $line -notmatch 'All tests finished running') {
                $reasons.Add("Blame collector: " + ($line -replace "^.*Data collector 'Blame' message:\s*", ''))
            }
            elseif ($line -match '(?i)test run was aborted|test host process crashed|the active test run was aborted|Test host process exited unexpectedly') {
                $reasons.Add("test host aborted: $($line.Trim())")
            }
        }
    }

    # Repeats (the same line in the log and the error log) collapse.
    return @($reasons | Select-Object -Unique)
}

Export-ModuleMember -Function Get-MotifShardAbortReasons
