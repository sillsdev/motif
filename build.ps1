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

  .PARAMETER Project
  Restore and build only tests/<Project>/<Project>.csproj and what it references, not Motif.sln. For
  iterating on one test project: the hygiene gates, ICU staging, PanGloss staging and interface gate still
  run. The test project (through SIL.Motif.Tests.Support) builds the CLI and worker apphosts its tests start.

.PARAMETER SkipHygiene
  Compile without the comment and design-token gates. For bisecting a build break only -- CI never passes this, so
  anything it lets through fails there instead.

.PARAMETER NoProcessSweep
Keep the worktree lock and process policy but skip stale-process cleanup.

  .NOTES
  MSBuild node count: restore and build pass -m:N. N is MOTIF_BUILD_NODES when that is a positive whole
  number; otherwise there is no cap on CI (the CI variable is true, 1 or yes) and a quarter of the
  processors, at least two, on a developer machine, where many worktrees build at once.

  .EXAMPLE
  ./build.ps1

  .EXAMPLE
  ./build.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Debug',
    [string] $Project,
    [switch] $SkipHygiene,
    [switch] $NoProcessSweep
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$solution = Join-Path $repoRoot 'Motif.sln'
$buildTarget = $solution
if ($Project) {
    if ($Project -notmatch '^[A-Za-z0-9_.]+$') { throw "-Project must be a test project name, not '$Project'." }
    $buildTarget = Join-Path $repoRoot "tests/$Project/$Project.csproj"
    if (-not (Test-Path -LiteralPath $buildTarget -PathType Leaf)) { throw "No test project file at $buildTarget." }
}
$targetText = if ($Project) { $Project } else { 'Motif.sln' }

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

# Every per-user tool cache except the shared packages folder lives under bin/.cache in this checkout.
Import-Module (Join-Path $repoRoot 'tools/MotifToolEnvironment.psm1') -Force
Initialize-MotifToolEnvironment -RepoRoot $repoRoot
Import-Module (Join-Path $repoRoot 'tools/MotifProcessHygiene.psm1') -Force
Set-MotifBuildProcessPolicy
$processLock = Enter-MotifWorktreeLock -RepoRoot $repoRoot -Context 'Motif build'

try {
    New-Item -ItemType Directory -Force -Path $env:NUGET_PACKAGES | Out-Null
    if (-not $NoProcessSweep) {
        Stop-MotifStaleProcesses -RepoRoot $repoRoot -LockHandle $processLock
    }

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

    Write-Step 'localization hygiene (report only)'
    Push-Location $repoRoot
    try { & dotnet run --file tools/LocalizationHygiene/localization-hygiene.cs --artifacts-path (Get-MotifToolArtifactsPath $repoRoot 'localization-hygiene') }
    finally { Pop-Location }
    $global:LASTEXITCODE = 0
}

$restoreModule = Join-Path $repoRoot 'tools/MotifRestore.psm1'
Import-Module $restoreModule -Force

$buildNodes = Get-MotifBuildNodeLimit
$nodeText = if ($buildNodes -gt 0) { "-m:$buildNodes" } else { 'no node cap' }
$nodeArguments = @(if ($buildNodes -gt 0) { "-m:$buildNodes" })

Write-Step "dotnet restore $targetText ($nodeText)"
$restore = Invoke-MotifRestore -Target $buildTarget -MaxNodes $buildNodes
$restore.Output | ForEach-Object { Write-Host $_ }
if ($restore.ExitCode -ne 0) {
    Write-Host ''
    Write-Host 'Restore failed.' -ForegroundColor Red
    exit 1
}

Write-Step "dotnet build $targetText ($Configuration, $nodeText)"
& dotnet build $buildTarget --configuration $Configuration --nologo --no-restore @nodeArguments
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

# A development build finds its parser beside itself, as a shipped one does, so it runs the pinned PanGloss.
Write-Step 'Pinned PanGloss beside the product'
$parserTarget = Join-Path $repoRoot "bin/$Configuration/$(if ($IsWindows) { 'pangloss.exe' } else { 'pangloss' })"
$pinnedParser = $null
try {
    Import-Module (Join-Path $repoRoot 'tools/PanGlossRelease.psm1') -Force
    $pinnedParser = Get-PinnedPanGlossArtifact -RepositoryRoot $repoRoot
    Copy-Item -LiteralPath $pinnedParser.Path -Destination $parserTarget -Force
    if (-not $IsWindows) { & chmod +x $parserTarget }
    Write-Host "PanGloss $($pinnedParser.Version) at $parserTarget"
}
catch {
    Write-Host "PanGloss was not staged beside the product: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host 'The window and CLI from this build need MOTIF_PANGLOSS_EXE until it is.' -ForegroundColor Yellow
}

Write-Step 'PanGloss interface gate'
$interfaceGateArguments = @{
    RepositoryRoot = $repoRoot
    PinnedParserPath = $parserTarget
    Configuration = $Configuration
}
if (-not [string]::IsNullOrWhiteSpace($env:MOTIF_PANGLOSS_EXE)) {
    $interfaceGateArguments.ParserPath = $env:MOTIF_PANGLOSS_EXE
}
if ($Configuration -eq 'Release' -or $env:CI -match '^(?i:true|1|yes)$') {
    $interfaceGateArguments.RequireParser = $true
}
& (Join-Path $repoRoot 'tools/Test-PanGlossInterfaces.ps1') @interfaceGateArguments
if ($LASTEXITCODE -ne 0) {
    Write-Host 'PanGloss interface gate failed.' -ForegroundColor Red
    exit 1
}

Write-Step 'Help golden'
Import-Module (Join-Path $repoRoot 'tools/MotifHelpGolden.psm1') -Force
$helpDiffers = Compare-MotifHelpGolden -RepoRoot $repoRoot -Configuration $Configuration
if ($helpDiffers) {
    Write-Host ''
    Write-Host "Help golden is stale; entries that differ: $($helpDiffers -join ', ')" -ForegroundColor Red
    Write-Host "Regenerate with: pwsh ./tools/Update-HelpGolden.ps1 -Configuration $Configuration" -ForegroundColor Red
    exit 1
}

Write-Host ''
$hygieneText = if ($SkipHygiene) { 'hygiene skipped' } else { 'comment and design-token hygiene' }
Write-Host "Build OK: $hygieneText, restore, $targetText compiles, PanGloss interface gate and Help golden all passed." -ForegroundColor Green
exit 0
}
finally {
    try {
        if (-not $NoProcessSweep) {
            Stop-MotifStaleProcesses -RepoRoot $repoRoot -LockHandle $processLock
        }
    }
    finally { Exit-MotifWorktreeLock -LockHandle $processLock }
}
