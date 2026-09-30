<#
  .SYNOPSIS
  Rewrites tests/test-shard-weights.json from the TRX files of the last ./test.ps1 run.

  .DESCRIPTION
  test.ps1 splits a project into shards by test class, and deals the classes out by the seconds this file
  records, so that the slow classes do not land in one shard. Run it after a full ./test.ps1 when shards look
  unbalanced, and commit the result. A class missing from the file still runs; the file only affects balance.

  .PARAMETER Configuration
  The configuration whose test results to read. Defaults to Debug, as test.ps1 does.
#>
[CmdletBinding()]
param([string] $Configuration = 'Debug')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$results = Join-Path $repoRoot (Join-Path 'bin' (Join-Path $Configuration 'test-results'))
$files = @(Get-ChildItem $results -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue)
if ($files.Count -eq 0) { throw "No TRX files under $results. Run ./test.ps1 first." }

$seconds = @{}
foreach ($file in $files) {
    $run = [xml](Get-Content $file.FullName -Raw)
    $classOf = @{}
    foreach ($test in $run.TestRun.TestDefinitions.UnitTest) { $classOf[$test.id] = $test.TestMethod.className }
    foreach ($result in $run.TestRun.Results.UnitTestResult) {
        $class = $classOf[$result.testId]
        $seconds[$class] = [double]($seconds[$class] ?? 0) + [TimeSpan]::Parse($result.duration).TotalSeconds
    }
}

$ordered = [ordered]@{}
foreach ($class in ($seconds.Keys | Sort-Object)) { $ordered[$class] = [Math]::Round($seconds[$class], 1) }
$target = Join-Path $repoRoot 'tests/test-shard-weights.json'
($ordered | ConvertTo-Json) + "`n" | Set-Content -Path $target -NoNewline -Encoding utf8
Write-Host "Wrote $($ordered.Count) class weights to $target"
