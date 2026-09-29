[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [string[]] $Only = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$configurationRoot = Join-Path $repositoryRoot "bin\$Configuration"
$mediaRoot = Join-Path $configurationRoot 'media'
$screenshotsRoot = Join-Path $mediaRoot 'screenshots'
$walkthroughOutput = Join-Path $mediaRoot 'walkthroughs'
$helpExport = Join-Path $mediaRoot 'help-export.json'
$siteRoot = Join-Path $repositoryRoot 'site'
$stepNames = @('build', 'icons', 'samples', 'screenshots', 'walkthroughs', 'help', 'media-test', 'site')

New-Item -ItemType Directory -Path $mediaRoot -Force | Out-Null
$selectedSteps = [System.Collections.Generic.List[string]]::new()
foreach ($item in $Only) {
    foreach ($name in $item.Split(',', [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $step = $name.Trim().ToLowerInvariant()
        if ($step -notin $stepNames) {
            throw "Unknown media step '$step'. Choose from: $($stepNames -join ', ')."
        }
        if (-not $selectedSteps.Contains($step)) {
            $selectedSteps.Add($step)
        }
    }
}

function Invoke-Native {
    param([string] $File, [string[]] $Arguments)

    $command = $File
    if ($File -eq 'npm') {
        $windowsShim = Get-Command 'npm.cmd' -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($windowsShim) {
            $command = $windowsShim.Source
        }
    }
    & $command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command '$File $($Arguments -join ' ')' exited with code $LASTEXITCODE."
    }
}

function Remove-GeneratedDirectory {
    param([string] $Path)

    $resolved = [System.IO.Path]::GetFullPath($Path)
    $prefix = [System.IO.Path]::GetFullPath($mediaRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear media output outside '$mediaRoot': $resolved"
    }
    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resolved -Force | Out-Null
}

function Get-WalkthroughManifests {
    param([string] $Root)

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        return @()
    }
    $direct = Join-Path $Root 'manifest.json'
    $manifests = [System.Collections.Generic.List[string]]::new()
    if (Test-Path -LiteralPath $direct -PathType Leaf) {
        $manifests.Add($direct)
    }
    foreach ($directory in Get-ChildItem -LiteralPath $Root -Directory) {
        $candidate = Join-Path $directory.FullName 'manifest.json'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $manifests.Add($candidate)
        }
    }
    return $manifests.ToArray()
}

function Get-UnresolvedGuideShots {
    param([string] $WalkthroughRoot)

    $available = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($manifestPath in Get-WalkthroughManifests $WalkthroughRoot) {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        foreach ($step in $manifest.steps) {
            if ($manifest.id -and $step.id -and $step.screenshot) {
                $assetPath = [System.IO.Path]::GetFullPath((Join-Path (Split-Path $manifestPath -Parent) $step.screenshot))
                if (Test-Path -LiteralPath $assetPath -PathType Leaf) {
                    [void] $available.Add("$($manifest.id)/$($step.id)")
                }
            }
        }
    }

    $unresolved = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::Ordinal)
    $guideRoot = Join-Path $repositoryRoot 'help/en/guide'
    foreach ($file in Get-ChildItem -LiteralPath $guideRoot -Filter '*.md' -File -Recurse) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($match in [regex]::Matches($content, '\]\(shot:([^)]+)\)')) {
            $target = $match.Groups[1].Value
            if (-not $available.Contains($target)) {
                [void] $unresolved.Add("shot:$target")
            }
        }
    }
    return @($unresolved)
}

function Invoke-MediaStep {
    param([string] $Step)

    Write-Host "`n==> Build-Media: $Step" -ForegroundColor Cyan
    switch ($Step) {
        'build' {
            $env:MSBUILDDISABLENODEREUSE = '1'
            $env:UseSharedCompilation = 'false'
            $env:AVALONIA_TELEMETRY_OPTOUT = '1'
            & (Join-Path $repositoryRoot 'build.ps1') -Configuration $Configuration
            if (-not $? -or $LASTEXITCODE -ne 0) {
                throw "./build.ps1 failed for $Configuration."
            }
        }
        'icons' {
            Invoke-Native 'dotnet' @('run', '--file', (Join-Path $repositoryRoot 'tools/Icons/render-icons.cs'))
        }
        'samples' {
            & (Join-Path $repositoryRoot 'tools/Build-Samples.ps1') -Configuration $Configuration
            if (-not $?) { throw 'Build-Samples.ps1 failed.' }
        }
        'screenshots' {
            Remove-GeneratedDirectory $screenshotsRoot
            $previousScreenshots = $env:MOTIF_SCREENSHOTS
            $previousProjects = $env:MOTIF_SCREENSHOT_PROJECTS
            try {
                $env:MOTIF_SCREENSHOTS = $screenshotsRoot
                Remove-Item Env:MOTIF_SCREENSHOT_PROJECTS -ErrorAction SilentlyContinue
                Invoke-Native 'dotnet' @(
                    'test',
                    (Join-Path $repositoryRoot 'tests/SIL.Motif.Tests.App/SIL.Motif.Tests.App.csproj'),
                    '--configuration', $Configuration,
                    '--no-build', '--no-restore', '--nologo',
                    '--filter', 'FullyQualifiedName~PageScreenshots.CaptureEveryPage|FullyQualifiedName~CrashWindowTests.CaptureTheErrorWindow'
                )
            }
            finally {
                $env:MOTIF_SCREENSHOTS = $previousScreenshots
                $env:MOTIF_SCREENSHOT_PROJECTS = $previousProjects
            }
        }
        'walkthroughs' {
            Remove-GeneratedDirectory $walkthroughOutput
            $previousWalkthroughOutput = $env:MOTIF_WALKTHROUGH_OUTPUT
            $previousWalkthroughClips = $env:MOTIF_WALKTHROUGH_CLIPS
            try {
                $env:MOTIF_WALKTHROUGH_OUTPUT = $walkthroughOutput
                $env:MOTIF_WALKTHROUGH_CLIPS = '1'
                Invoke-Native 'dotnet' @(
                    'test',
                    (Join-Path $repositoryRoot 'tests/SIL.Motif.Tests.App/SIL.Motif.Tests.App.csproj'),
                    '--configuration', $Configuration,
                    '--no-build', '--no-restore', '--nologo',
                    '--filter', 'FullyQualifiedName~WalkthroughReplayTests'
                )
            }
            finally {
                $env:MOTIF_WALKTHROUGH_OUTPUT = $previousWalkthroughOutput
                $env:MOTIF_WALKTHROUGH_CLIPS = $previousWalkthroughClips
            }
        }
        'help' {
            $motif = Join-Path $configurationRoot 'motif.exe'
            if (-not (Test-Path -LiteralPath $motif -PathType Leaf)) {
                $motif = Join-Path $configurationRoot 'motif'
            }
            if (-not (Test-Path -LiteralPath $motif -PathType Leaf)) {
                throw "Motif CLI was not found under '$configurationRoot'."
            }
            $json = & $motif help --all --json
            if ($LASTEXITCODE -ne 0) { throw "Help export failed with exit code $LASTEXITCODE." }
            [System.IO.File]::WriteAllText($helpExport, ($json -join "`n"), [System.Text.UTF8Encoding]::new($false))
        }
        'media-test' {
            Invoke-Native 'dotnet' @(
                'test',
                (Join-Path $repositoryRoot 'tests/SIL.Motif.Tests.Contract/SIL.Motif.Tests.Contract.csproj'),
                '--configuration', $Configuration,
                '--no-build', '--no-restore', '--nologo',
                '--filter', 'FullyQualifiedName~MediaManifestTests'
            )
        }
        'site' {
            Invoke-Native 'npm' @('ci', '--prefix', $siteRoot)
            $syncArgs = @(
                'run', 'sync', '--prefix', $siteRoot, '--',
                '--help-export', $helpExport,
                '--help-root', (Join-Path $repositoryRoot 'help'),
                '--samples-root', (Join-Path $repositoryRoot 'samples'),
                '--samples-out', (Join-Path $configurationRoot 'samples'),
                '--api-xml', (Join-Path $configurationRoot 'SIL.Motif.Contract.xml')
            )
            $previousWalkthroughOutput = $env:MOTIF_WALKTHROUGH_OUTPUT
            try {
                $env:MOTIF_WALKTHROUGH_OUTPUT = $walkthroughOutput
                Invoke-Native 'npm' $syncArgs
            }
            finally {
                $env:MOTIF_WALKTHROUGH_OUTPUT = $previousWalkthroughOutput
            }
            Invoke-Native 'npm' @('test', '--prefix', $siteRoot)
            Invoke-Native 'npm' @('run', 'build', '--prefix', $siteRoot, '--ignore-scripts')
        }
    }
}

$failure = $null
$steps = if ($selectedSteps.Count -eq 0) { $stepNames } else { $selectedSteps.ToArray() }
try {
    foreach ($step in $steps) {
        Invoke-MediaStep $step
    }
}
catch {
    $failure = $_.Exception
}
finally {
    Write-Host "`nBuild-Media outputs ($Configuration):" -ForegroundColor Cyan
    foreach ($output in @(
        (Join-Path $configurationRoot 'SIL.Motif.App'),
        (Join-Path $repositoryRoot 'src/SIL.Motif.App/Assets/motif-icon.png'),
        (Join-Path $configurationRoot 'samples'),
        $screenshotsRoot,
        $walkthroughOutput,
        $helpExport,
        (Join-Path $siteRoot 'dist')
    )) {
        if (Test-Path -LiteralPath $output) { Write-Host "  $output" }
    }

    $unresolved = @(Get-UnresolvedGuideShots $walkthroughOutput)
    if ($unresolved.Count -eq 0) {
        Write-Host 'Unresolved Guide shot IDs: none.' -ForegroundColor Green
    }
    else {
        Write-Host "Unresolved Guide shot IDs ($($unresolved.Count)):" -ForegroundColor Yellow
        foreach ($target in $unresolved) { Write-Host "  $target" }
    }
}

if ($failure) {
    throw $failure
}
