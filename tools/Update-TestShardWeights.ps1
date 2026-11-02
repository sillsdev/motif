<#
  .SYNOPSIS
  Updates tests/test-shard-weights.json from the TRX files of the last ./test.ps1 run.

  .DESCRIPTION
  test.ps1 splits a project into shards by test class, and deals the classes out by the seconds this file
  records, so that the slow classes do not land in one shard. Run it after a full ./test.ps1 when shards look
  unbalanced, and commit the result. A class missing from the file still runs; the file only affects balance.

  The seconds of the classes in the run replace those classes' entries and every other entry stays, so a run of one
  project keeps the timings of the others. A class that no longer exists stays too until -Replace drops it.
  -Replace refuses unless the results hold a TRX file for every test project, so a run of one project cannot wipe the
  other projects' timings. Do not run this after a -Filter run: a class that ran in part would be recorded at that
  part's seconds.

  A test's wall time grows with the machine's load, and a gate run on a busy host records that load as if it were
  the test's cost (one class measured 190 s at load 68 and 48 s at load 6). -RunLoad scales every duration by
  min(1, processors / load), the share of a processor each runnable thread got, so the weights record work rather
  than the host's crowding. Without it the wall seconds are written unchanged.

  .PARAMETER Configuration
  The configuration whose test results to read. Defaults to Debug, as test.ps1 does.

  .PARAMETER RunLoad
  The load average (runnable threads) the run was made under, from `uptime` during the run. A value at or below the
  processor count changes nothing. Zero, the default, means do not normalise.

  .PARAMETER Replace
  Write only the classes in the run, dropping every other entry. Refused unless the results cover every test project
  (a folder or file named for each project, shard folders included), as the run of the whole suite does.

  .PARAMETER ResultsDirectory
  The folder holding the TRX files. Defaults to bin/<Configuration>/test-results.

  .PARAMETER Target
  The weights file. Defaults to tests/test-shard-weights.json.

  .PARAMETER NormalizeLoad
  Use this machine's current five-minute load average as -RunLoad. Run it straight after the gate, on the host that
  ran it; it is no use for results copied from another machine.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Debug',
    [double] $RunLoad = 0,
    [switch] $NormalizeLoad,
    [switch] $Replace,
    [string] $ResultsDirectory,
    [string] $Target,
    [string] $RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = if ($RepoRoot) { $RepoRoot } else { Split-Path -Parent $PSScriptRoot }
$results = if ($ResultsDirectory) { $ResultsDirectory } else { Join-Path $repoRoot (Join-Path 'bin' (Join-Path $Configuration 'test-results')) }
$target = if ($Target) { $Target } else { Join-Path $repoRoot 'tests/test-shard-weights.json' }
$files = @(Get-ChildItem $results -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue)
if ($files.Count -eq 0) { throw "No TRX files under $results. Run ./test.ps1 first." }

if ($Replace) {
    $testProjects = foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tests') -Filter '*.csproj' -File -Recurse) {
        if ((Get-Content -LiteralPath $file.FullName -Raw) -match '<IsTestProject>\s*true\s*</IsTestProject>') { $file.BaseName }
    }
    $covered = @($files | ForEach-Object {
        $first = [IO.Path]::GetRelativePath($results, $_.FullName).Split([char[]] '/\\')[0] -replace '\.trx$', ''
        $first -replace '\.shard\d+$', ''
    })
    $missing = @($testProjects | Where-Object { $_ -notin $covered } | Sort-Object)
    if ($missing.Count -gt 0) {
        throw "-Replace would drop the timings of projects this run did not cover: $($missing -join ', '). Run the whole suite (./test.ps1 -All) first, or leave -Replace off to merge."
    }
}

if ($NormalizeLoad -and $RunLoad -le 0) {
    $loadFile = '/proc/loadavg'
    if (-not (Test-Path -LiteralPath $loadFile)) { throw '-NormalizeLoad needs /proc/loadavg; pass -RunLoad instead.' }
    $RunLoad = [double](((Get-Content -LiteralPath $loadFile -Raw) -split '\s+')[1])
}
$scale = if ($RunLoad -gt [Environment]::ProcessorCount) { [Environment]::ProcessorCount / $RunLoad } else { 1.0 }
if ($scale -lt 1.0) {
    Write-Host ("Run load {0:N1} on {1} processors: scaling every duration by {2:N2}." -f
        $RunLoad, [Environment]::ProcessorCount, $scale)
}

$seconds = @{}
foreach ($file in $files) {
    $run = [xml](Get-Content $file.FullName -Raw)
    $classOf = @{}
    foreach ($test in $run.TestRun.TestDefinitions.UnitTest) { $classOf[$test.id] = $test.TestMethod.className }
    foreach ($result in $run.TestRun.Results.UnitTestResult) {
        $class = $classOf[$result.testId]
        $seconds[$class] = [double]($seconds[$class] ?? 0) + ([TimeSpan]::Parse($result.duration).TotalSeconds * $scale)
    }
}

# A class in the run takes its new seconds; every other class in the file keeps its entry unless -Replace.
$merged = @{}
if (-not $Replace -and (Test-Path -LiteralPath $target)) {
    foreach ($property in (Get-Content -LiteralPath $target -Raw | ConvertFrom-Json).PSObject.Properties) { $merged[$property.Name] = [double] $property.Value }
}
foreach ($class in $seconds.Keys) { $merged[$class] = [Math]::Round($seconds[$class], 1) }
$ordered = [ordered]@{}
foreach ($class in ($merged.Keys | Sort-Object)) { $ordered[$class] = $merged[$class] }
($ordered | ConvertTo-Json) + "`n" | Set-Content -Path $target -NoNewline -Encoding utf8
Write-Host "Wrote $($ordered.Count) class weights to $target ($($seconds.Count) classes from this run, the rest kept)"
