<#
.SYNOPSIS
Captures the layout size matrix, then builds the contact sheet and the pixel diff.

.DESCRIPTION
By default the size matrix captures each scene in 7 cells: thin, half, full and short at 100% light; half at
150% light; and thin and full at 100% dark. -All captures every scene at all 4 sizes x 2 zooms (100, 150) x
2 themes. -All may fail on real defects in cells the expected-findings list does not name, and it still
writes the PNGs, findings.json and the contact sheet, so a failing run is still a complete capture.
-Scenes, -Sizes, -Zooms and -Themes narrow the default run and cannot be combined with -All.

.PARAMETER Scenes
Matrix scene names to capture.

.PARAMETER Sizes
Matrix window sizes to capture: thin, half, full or short.

.PARAMETER Zooms
Zoom percentages to capture: 100 or 150.

.PARAMETER Themes
Themes to capture: light or dark.

.PARAMETER All
Captures every screenshot class and every scene at all 4 sizes x 2 zooms x 2 themes.

.PARAMETER Configuration
The build configuration whose binaries are tested. The default is Debug.

.PARAMETER Output
The capture folder to write. Without it each run writes a fresh dated folder under bin/captures.

.PARAMETER Previous
The capture folder to compare with. Without it the newest earlier run is used.

.PARAMETER ReferenceMap
JSON object mapping scene names to H artboard PNG paths, absolute or relative to the JSON file.
Copies the artboards beside the sheet and pairs them with actual captures. Unmapped scenes are labelled.

.PARAMETER NoRun
Rebuilds the sheet and the diff of the folder named by -Output without capturing again.

.PARAMETER NoDiff
Skips the compiled pixel-diff tool and builds only the sheet.
#>
[CmdletBinding()]
param(
    [string[]] $Scenes,
    [string[]] $Sizes,
    [string[]] $Zooms,
    [string[]] $Themes,
    [switch] $All,
    [string] $Configuration = 'Debug',
    [string] $Output,
    [string] $Previous,
    [string] $ReferenceMap,
    [switch] $NoRun,
    [switch] $NoDiff
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$captureRoot = Join-Path $repoRoot 'bin/captures'
$modulePath = Join-Path $repoRoot 'tools/CaptureScreens/CaptureScreens.psm1'
Import-Module $modulePath -Force

if ($NoRun -and [string]::IsNullOrWhiteSpace($Output)) {
    throw '-NoRun requires -Output to name an existing capture folder.'
}

$outputDirectory = Resolve-MotifCaptureOutputPath -RepoRoot $repoRoot -OutputDirectory $Output
$latestBeforeRun = Resolve-MotifCaptureLatest -CaptureRoot $captureRoot
$previousDirectory = $null
$testFailure = $null
if (-not [string]::IsNullOrWhiteSpace($Previous)) {
    $previousDirectory = if ([IO.Path]::IsPathRooted($Previous)) {
        [IO.Path]::GetFullPath($Previous)
    } else {
        [IO.Path]::GetFullPath((Join-Path $repoRoot $Previous))
    }
    if (-not (Test-Path -LiteralPath $previousDirectory -PathType Container)) {
        throw "Previous capture folder not found: $previousDirectory"
    }
}
elseif ($latestBeforeRun -and $latestBeforeRun -ne $outputDirectory) {
    $previousDirectory = $latestBeforeRun
}
if ($previousDirectory -eq $outputDirectory) { $previousDirectory = $null }

$captureEnvironment = Get-MotifCaptureEnvironment -OutputDirectory $outputDirectory -Scenes $Scenes -Sizes $Sizes `
    -Zooms $Zooms -Themes $Themes -All:$All

if ($NoRun) {
    if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
        throw "Capture output folder not found: $outputDirectory"
    }
}
else {
    Clear-MotifCaptureOutput -OutputDirectory $outputDirectory
    [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    if ([string]::IsNullOrWhiteSpace($env:MOTIF_PANGLOSS_EXE)) {
        Write-Warning 'MOTIF_PANGLOSS_EXE is unset; screenshot tests that need the real parser may skip.'
    }

    Import-Module (Join-Path $repoRoot 'tools/MotifToolEnvironment.psm1') -Force
    Initialize-MotifToolEnvironment -RepoRoot $repoRoot
    $oldCaptureEnvironment = Set-MotifCaptureEnvironment -Values $captureEnvironment
    try {
        $testFilter = if ($All) {
            'FullyQualifiedName~Screenshot'
        } else {
            'FullyQualifiedName~SizeMatrixScreenshots'
        }
        $testScript = Join-Path $repoRoot 'test.ps1'
        Write-Host "Capturing to $outputDirectory"
        Write-Host "Running: pwsh ./test.ps1 -All -Project SIL.Motif.Tests.App -Configuration $Configuration -Filter '$testFilter'"
        & pwsh -NoProfile -File $testScript -All -Project 'SIL.Motif.Tests.App' `
            -Configuration $Configuration -Filter $testFilter
        if ($LASTEXITCODE -ne 0) {
            $testFailure = "Screenshot test run failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Restore-MotifCaptureEnvironment -Values $oldCaptureEnvironment
    }
}

if (-not $NoDiff) {
    Import-Module (Join-Path $repoRoot 'tools/MotifToolEnvironment.psm1') -Force
    Initialize-MotifToolEnvironment -RepoRoot $repoRoot
    $diffTool = Join-Path $repoRoot 'tools/CaptureScreens/capture-diff.cs'
    $diffArguments = @('--current', $outputDirectory, '--csv', (Join-Path $outputDirectory 'diff.csv'))
    if ($previousDirectory) { $diffArguments += @('--previous', $previousDirectory) }
    & dotnet run --file $diffTool --artifacts-path (Get-MotifToolArtifactsPath $repoRoot 'capture-diff') -- @diffArguments
    if ($LASTEXITCODE -ne 0) { throw "Capture diff failed with exit code $LASTEXITCODE." }
}

$sheet = New-MotifCaptureSheet -OutputDirectory $outputDirectory -PreviousDirectory $previousDirectory `
    -ReferenceMap $ReferenceMap
if (-not $NoRun) { Write-MotifCaptureLatest -CaptureRoot $captureRoot -OutputDirectory $outputDirectory }
Write-Host "Contact sheet: $sheet"
if ($previousDirectory) { Write-Host "Compared with: $previousDirectory" }
if ($testFailure) {
    Write-Error "$testFailure Findings: $(Join-Path $outputDirectory 'matrix/findings.json'); sheet: $sheet"
    exit 1
}
