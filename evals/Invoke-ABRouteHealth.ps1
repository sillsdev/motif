[CmdletBinding()]
param(
    [string] $Question = 'evals/questions/route-health.yaml',
    [string] $Configuration = 'Debug',
    [int] $Parallel = 1,
    [switch] $DryRun,
    [Alias('-keep')][switch] $Keep
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'tools/ABHarness.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'tools/ABAcceptance.psm1') -Force
$questionPath = if ([IO.Path]::IsPathRooted($Question)) { $Question } else { Join-Path $repoRoot $Question }
$arguments = @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Invoke-ABQuestion.ps1'),
    '-Question', $questionPath, '-Configuration', $Configuration, '-Parallel', [string]$Parallel)
if ($DryRun) { $arguments += '-DryRun' }
if ($Keep) { $arguments += '-Keep' }
$run = Invoke-ABProcess (Get-Command pwsh).Source $arguments $repoRoot @{} 14400000
if ($run.ExitCode -ne 0) { throw "Route-health Episodes failed:`n$($run.Stdout)`n$($run.Stderr)" }
if ($DryRun) { Write-Host $run.Stdout; return }
$summaryLine = [regex]::Match($run.Stdout, '(?m)^Summary: (?<path>.+)$')
if (-not $summaryLine.Success) { throw "The route-health run did not report its summary path:`n$($run.Stdout)" }
$summaryPath = $summaryLine.Groups['path'].Value.Trim()
$summary = Read-ABJson $summaryPath
$routes = @(
    @{ id = 'claude-haiku-subscription'; authMode = 'claude-plan'; allowedHost = 'api.anthropic.com'; episodes = 3 },
    @{ id = 'codex-luna-chatgpt-plan'; authMode = 'chatgpt-plan'; allowedHost = 'chatgpt.com'; episodes = 3 }
)
$result = Get-ABRouteHealthResult $summary $routes
$result.summaryPath = $summaryPath
$result.questionId = $summary.questionId
$resultPath = Join-Path (Split-Path -Parent $summaryPath) 'route-health.json'
Write-ABJson $resultPath $result
$report = [Collections.Generic.List[string]]::new()
$report.Add("Route health: $(if ($result.passed) { 'PASS' } else { 'FAIL' })")
$report.Add('')
$report.Add("Selected route: $(if ($result.selectedRoute) { $result.selectedRoute } else { 'none' })")
$report.Add("Fallback reason: $(if ($result.fallbackReason) { $result.fallbackReason } else { 'none' })")
$report.Add('')
$report.Add('| Route | Billing mode | Episodes | Median Episode time | Cloud failures | Cloud retries | Result |')
$report.Add('|---|---|---:|---:|---:|---:|---|')
foreach ($route in $result.routes) {
    $median = if ($null -ne $route.medianEpisodeMs) { '{0:N1}s' -f ($route.medianEpisodeMs / 1000) } else { 'not recorded' }
    $report.Add("| $($route.arm) | $($route.authMode) | $($route.episodes) | $median | $($route.cloudFailures) | $($route.cloudRetries) | $(if ($route.passed) { 'pass' } else { 'fail' }) |")
    foreach ($reason in $route.reasons) { $report.Add("  - $reason") }
}
Set-Content -LiteralPath (Join-Path (Split-Path -Parent $summaryPath) 'route-health.md') -Value ($report -join "`n") -Encoding utf8
Write-Host "Route-health report: $resultPath"
Write-Host "Selected route: $($result.selectedRoute)"
if (-not $result.passed) { exit 1 }
