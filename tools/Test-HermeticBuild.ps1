<#
  .SYNOPSIS
  Fail when the build or the test harness writes outside this checkout.

  .DESCRIPTION
  Runs build.ps1, then test.ps1 on one project, with HOME pointed at an empty folder nobody can
  write. A sandboxed agent may write only in its worktree; this is that rule, checked on every CI run,
  so a tool that starts keeping a cache beside the user's home fails here and names the path, rather
  than surfacing later as an agent stalled on a permission prompt.

  The shared packages folder is restored first under the real home, as a developer's first restore
  fills it, and is read-only to the build afterwards. The system temporary folder stays writable:
  every sandbox allows it. Linux and macOS only; Windows has no single home variable to redirect.

  .PARAMETER Configuration
  The configuration to build. Defaults to Debug.

  .PARAMETER Project
  The test project to run under the read-only home. A small one keeps the check quick.

  .EXAMPLE
  pwsh tools/Test-HermeticBuild.ps1
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Debug',
    [string] $Project = 'SIL.Motif.Tests.Contract'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($IsWindows) { throw 'Test-HermeticBuild.ps1 runs on Linux and macOS.' }

$repoRoot = Split-Path -Parent $PSScriptRoot
$realHome = $env:HOME
if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) { $env:NUGET_PACKAGES = Join-Path $realHome '.nuget/packages' }

Write-Host '==> restore into the shared packages folder (real home)' -ForegroundColor Cyan
& dotnet restore (Join-Path $repoRoot 'Motif.sln') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }

$fakeHome = Join-Path ([IO.Path]::GetTempPath()) "motif-readonly-home-$PID"
[IO.Directory]::CreateDirectory($fakeHome) | Out-Null
& chmod 0555 $fakeHome

$isolated = @{ HOME = $fakeHome }
foreach ($name in 'USERPROFILE', 'XDG_DATA_HOME', 'XDG_CACHE_HOME', 'XDG_CONFIG_HOME', 'DOTNET_CLI_HOME',
        'NUGET_HTTP_CACHE_PATH', 'NUGET_PLUGINS_CACHE_PATH', 'PSModuleAnalysisCachePath') {
    $isolated[$name] = $null
}
$saved = @{}
foreach ($entry in $isolated.GetEnumerator()) {
    $saved[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key)
    [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
}

$failed = $null
try {
    Write-Host "==> build.ps1 with HOME=$fakeHome (read-only)" -ForegroundColor Cyan
    & pwsh -NoProfile -File (Join-Path $repoRoot 'build.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { $failed = 'build.ps1' }
    else {
        Write-Host "==> test.ps1 -Project $Project with HOME=$fakeHome (read-only)" -ForegroundColor Cyan
        & pwsh -NoProfile -File (Join-Path $repoRoot 'test.ps1') -Configuration $Configuration -SkipBuild -Project $Project
        if ($LASTEXITCODE -ne 0) { $failed = 'test.ps1' }
    }
}
finally {
    foreach ($entry in $saved.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value) }
    & chmod 0755 $fakeHome
    $written = @(Get-ChildItem -LiteralPath $fakeHome -Recurse -Force -ErrorAction SilentlyContinue)
    Remove-Item -LiteralPath $fakeHome -Recurse -Force -ErrorAction SilentlyContinue
}

if ($written.Count -gt 0) {
    Write-Host 'Written under the read-only home:' -ForegroundColor Red
    $written | ForEach-Object { Write-Host "  $($_.FullName)" }
    exit 1
}
if ($failed) {
    Write-Host "$failed failed with a read-only home: a tool wrote outside the checkout. Its log above names the path." -ForegroundColor Red
    exit 1
}
Write-Host 'Hermetic: the build and the test harness wrote only inside the checkout.' -ForegroundColor Green
exit 0
