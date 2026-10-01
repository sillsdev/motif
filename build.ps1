<#
  .SYNOPSIS
  The build gate: hygiene, then solution compile. Stops at the first failure.

  .DESCRIPTION
  Use this instead of a bare `dotnet build`. The difference is the hygiene gate, and the reason it is
  wired in here rather than left to whoever remembers is that a rule nothing enforces is a rule that
  decays to a suggestion. Comment rot is silent by construction: nothing fails, nothing looks wrong,
  and a stale comment keeps being believed because it looks maintained.

  Every step is a hard failure. The hygiene gates run FIRST and on its own, because it is seconds of
  work against minutes of compilation, and because a violation is a fact about the source that does
  not depend on whether the code compiles.

  The hygiene script is invoked as a child process so that its own `exit` code arrives here intact
  rather than terminating this script's scope.

  The design-token gate is the same idea for the App's look: a view styles itself from Intent and Component
  keys, never from a raw colour or size, so the look changes in one place.

  .PARAMETER Configuration
  MSBuild configuration. Defaults to Debug, matching a bare `dotnet build`.

  .PARAMETER SkipHygiene
  Compile without the comment and design-token gates. For bisecting a build break only -- CI never passes this, so
  anything it lets through fails there instead.

  .EXAMPLE
  ./build.ps1

  .EXAMPLE
  ./build.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Debug',
    [switch] $SkipHygiene
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$solution = Join-Path $repoRoot 'Motif.sln'

$profileRoot = $env:USERPROFILE
if ([string]::IsNullOrWhiteSpace($profileRoot)) { $profileRoot = $env:HOME }
if ([string]::IsNullOrWhiteSpace($profileRoot)) {
    $profileRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
}
if ([string]::IsNullOrWhiteSpace($profileRoot)) {
    throw 'Could not determine the user home directory for NuGet packages.'
}
if ([string]::IsNullOrWhiteSpace($env:HOME)) { $env:HOME = $profileRoot }
if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
    $env:NUGET_PACKAGES = Join-Path $profileRoot '.nuget/packages'
}
New-Item -ItemType Directory -Force -Path $env:NUGET_PACKAGES | Out-Null

# Every per-user tool cache except the shared packages folder lives under bin/.cache in this checkout.
Import-Module (Join-Path $repoRoot 'tools/MotifToolEnvironment.psm1') -Force
Initialize-MotifToolEnvironment -RepoRoot $repoRoot

function Write-Step {
    param([string] $Text)
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Cyan
}

if ($env:LOCAL_NUGET_REPO -and (Test-Path $env:LOCAL_NUGET_REPO)) {
    Write-Host "==> Local libpalaso override active: LOCAL_NUGET_REPO=$env:LOCAL_NUGET_REPO (see AGENTS.md)" -ForegroundColor Yellow
}
elseif ($env:LOCAL_NUGET_REPO) {
    Write-Host "==> LOCAL_NUGET_REPO is set but '$env:LOCAL_NUGET_REPO' does not exist -- ignoring" -ForegroundColor Yellow
}

if ($SkipHygiene) {
    Write-Step 'comment and design-token hygiene -- SKIPPED (-SkipHygiene)'
}
else {
    Write-Step 'comment hygiene'
    Push-Location $repoRoot
    try { & dotnet run --file tools/CommentHygiene/comment-hygiene.cs --artifacts-path (Get-MotifToolArtifactsPath $repoRoot 'comment-hygiene') }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) {
        Write-Host ''
        Write-Host 'Comment hygiene failed. Run: dotnet run tools/CommentHygiene/comment-hygiene.cs -- -List' -ForegroundColor Red
        exit 1
    }

    Write-Step 'design-token hygiene'
    Push-Location $repoRoot
    try { & dotnet run --file tools/TokenHygiene/token-hygiene.cs --artifacts-path (Get-MotifToolArtifactsPath $repoRoot 'token-hygiene') }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) {
        Write-Host ''
        Write-Host 'Design-token hygiene failed. Run: dotnet run --file tools/TokenHygiene/token-hygiene.cs' -ForegroundColor Red
        exit 1
    }
}

$restoreModule = Join-Path $repoRoot 'tools/MotifRestore.psm1'
Import-Module $restoreModule -Force

Write-Step 'dotnet restore'
$restore = Invoke-MotifRestore -Target $solution
$restore.Output | ForEach-Object { Write-Host $_ }
if ($restore.ExitCode -ne 0) {
    Write-Host ''
    Write-Host 'Restore failed.' -ForegroundColor Red
    exit 1
}

Write-Step "dotnet build ($Configuration)"
& dotnet build $solution --configuration $Configuration --nologo --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Write-Host 'Build failed.' -ForegroundColor Red
    exit 1
}

if (-not $IsWindows) {
    Write-Step 'SIL ICU beside the product and test hosts'
    if ($env:MOTIF_SIL_ICU_STAGE -and -not (Copy-MotifStagedIcu -RepoRoot $repoRoot -Configuration $Configuration)) {
        Write-Host ''
        Write-Host 'Staging SIL ICU failed.' -ForegroundColor Red
        exit 1
    }
    elseif (-not $env:MOTIF_SIL_ICU_STAGE) { [void](Copy-MotifStagedIcu -RepoRoot $repoRoot -Configuration $Configuration) }
}

Write-Host ''
Write-Host 'Build OK: comments clean, solution compiles.' -ForegroundColor Green
exit 0
