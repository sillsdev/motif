Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'CaptureScreens/CaptureScreens.psm1') -Force
$fixtureRoot = Join-Path $repoRoot ('bin/.cache/capture-screen-tests/' + [guid]::NewGuid().ToString('N'))
$captureScript = Join-Path $PSScriptRoot 'Capture-Screens.ps1'
$passed = 0

function Assert-CaptureEqual {
    param([object] $Actual, [object] $Expected, [string] $Name)

    if ($Actual -cne $Expected) { throw "$Name expected '$Expected', received '$Actual'." }
    $script:passed++
}

try {
    $environment = Get-MotifCaptureEnvironment -OutputDirectory '/tmp/captures' `
        -Scenes 'overview,settings' -Sizes 'thin,half' -Zooms '100,150' -Themes 'light,dark'
    Assert-CaptureEqual $environment.MOTIF_SCREENSHOTS ([IO.Path]::GetFullPath('/tmp/captures')) 'MOTIF_SCREENSHOTS'
    Assert-CaptureEqual $environment.MOTIF_CAPTURE_SCENES 'overview,settings' 'scene selection'
    Assert-CaptureEqual $environment.MOTIF_CAPTURE_SIZES 'thin,half' 'size selection'
    Assert-CaptureEqual $environment.MOTIF_CAPTURE_ZOOMS '100,150' 'zoom selection'
    Assert-CaptureEqual $environment.MOTIF_CAPTURE_THEMES 'light,dark' 'theme selection'
    Assert-CaptureEqual (Get-MotifCaptureEnvironment -OutputDirectory '/tmp/all' -All).MOTIF_CAPTURE_MATRIX `
        'all' 'full matrix selection'

    $captures = Join-Path $fixtureRoot 'captures'
    $run = Join-Path $captures 'run-one'
    [IO.Directory]::CreateDirectory($run) | Out-Null
    Write-MotifCaptureLatest -CaptureRoot $captures -OutputDirectory $run
    Assert-CaptureEqual (Resolve-MotifCaptureLatest -CaptureRoot $captures) ([IO.Path]::GetFullPath($run)) 'latest path'
    Assert-CaptureEqual (Get-Content -LiteralPath (Join-Path $captures 'latest') -Raw).Trim() 'run-one' 'latest marker'

    $sample = Join-Path $fixtureRoot 'sample'
    $previous = Join-Path $fixtureRoot 'previous'
    $matrix = Join-Path $sample 'matrix'
    [IO.Directory]::CreateDirectory($matrix) | Out-Null
    [IO.Directory]::CreateDirectory($previous) | Out-Null
    $png = [Convert]::FromBase64String(
        'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p68AAAAASUVORK5CYII=')
    [IO.File]::WriteAllBytes((Join-Path $matrix 'overview-thin-100-light.png'), $png)
    [IO.File]::WriteAllBytes((Join-Path $matrix 'settings-half-150-dark.png'), $png)
    [IO.File]::WriteAllBytes((Join-Path $sample 'overview-page.png'), $png)
    foreach ($path in @('large-project/nested overview.png', 'references-neighbor/neighbor.png',
            'matrix-neighbor/neighbor.png',
            'diff/old-diff.png', 'references/old-reference.png')) {
        $image = Join-Path $sample $path
        [IO.Directory]::CreateDirectory((Split-Path -Parent $image)) | Out-Null
        [IO.File]::WriteAllBytes($image, $png)
    }
    $removed = Join-Path $previous 'large-project/old overview.png'
    [IO.Directory]::CreateDirectory((Split-Path -Parent $removed)) | Out-Null
    [IO.File]::WriteAllBytes($removed, $png)
    [IO.File]::WriteAllBytes((Join-Path $previous 'old-root-capture.png'), $png)
    $previousMatrix = Join-Path $previous 'matrix'
    [IO.Directory]::CreateDirectory($previousMatrix) | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $previousMatrix 'removed-scene-thin-100-dark.png'), $png)
    [IO.File]::WriteAllText((Join-Path $previousMatrix 'index.csv'),
        "file,scene,size,width,height,zoom,theme,findings`nmatrix/removed-scene-thin-100-dark.png,removed-scene,thin,1,1,100,dark,0`n")
    if (-not $IsWindows -and -not $IsMacOS) {
        foreach ($path in @((Join-Path $sample 'case-folder/Overview.png'),
                (Join-Path $previous 'case-folder/overview.png'))) {
            [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
            [IO.File]::WriteAllBytes($path, $png)
        }
    }
    [IO.File]::WriteAllText((Join-Path $matrix 'index.csv'),
        "file,scene,size,width,height,zoom,theme,findings`noverview-thin-100-light.png,overview,thin,1,1,100,light,1`nsettings-half-150-dark.png,settings,half,1,1,150,dark,0`n")
    [IO.File]::WriteAllText((Join-Path $matrix 'findings.json'),
        '[{"file":"overview-thin-100-light.png","scene":"overview","size":"thin","zoom":100,"theme":"light","check":"sample","control":"Button","bounds":"0,0,1,1","detail":"sample finding","expected":"visible"}]')
    & pwsh -NoProfile -File $captureScript -NoRun -NoDiff -Output $sample -Previous $previous
    if ($LASTEXITCODE -ne 0) { throw "-NoRun failed with exit code $LASTEXITCODE." }
    $html = Get-Content -LiteralPath (Join-Path $sample 'index.html') -Raw
    foreach ($term in @('overview', 'settings', 'sample finding', 'overview-thin-100-light.png',
            'only cells with findings', '0 findings')) {
        if ($html.IndexOf($term, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw "The contact sheet does not contain '$term'."
        }
    }
    Assert-CaptureEqual (Test-Path -LiteralPath (Join-Path $sample 'diff.csv')) $false '-NoDiff writes no diff.csv'
    foreach ($path in @('large-project/nested%20overview.png', 'references-neighbor/neighbor.png',
            'matrix-neighbor/neighbor.png',
            'large-project/old overview.png', 'old-root-capture.png')) {
        Assert-CaptureEqual $html.Contains($path) $true "nested capture appears: $path"
    }
    foreach ($path in @('diff/old-diff.png', 'references/old-reference.png')) {
        Assert-CaptureEqual $html.Contains($path) $false "generated image stays outside the gallery: $path"
    }
    Assert-CaptureEqual ([regex]::Matches($html, 'src="matrix/overview-thin-100-light.png"').Count) 1 `
        'a matrix capture appears only in its matrix cell'
    [IO.File]::WriteAllText((Join-Path $sample 'diff.csv'),
        "file,status`nlarge-project/nested overview.png,changed`n")
    New-MotifCaptureSheet -OutputDirectory $sample -PreviousDirectory $previous | Out-Null
    $html = Get-Content -LiteralPath (Join-Path $sample 'index.html') -Raw
    Assert-CaptureEqual ([regex]::Match($html,
        '<figcaption>large-project/old overview[.]png<div class="cell-meta">([^ <]+)').Groups[1].Value) `
        'removed' 'previous-only nested capture infers removed status'
    Assert-CaptureEqual ([regex]::Match($html,
        '<figcaption>old-root-capture[.]png<div class="cell-meta">([^ <]+)').Groups[1].Value) `
        'removed' 'previous-only root capture infers removed status'
    Assert-CaptureEqual ([regex]::Match($html,
        '<figcaption>large-project/nested overview[.]png<div class="cell-meta">([^ <]+)').Groups[1].Value) `
        'changed' 'explicit current capture diff status is preserved'
    Assert-CaptureEqual ([regex]::Match($html,
        '<figcaption>overview-page[.]png<div class="cell-meta">([^ <]+)').Groups[1].Value) `
        'same' 'available current capture defaults to same with previous'
    $removedMatrixHtml = [regex]::Match($html,
        '<article class="matrix-cell capture-cell"[^>]*data-scene="removed-scene".*?</article>',
        [Text.RegularExpressions.RegexOptions]::Singleline).Value
    Assert-CaptureEqual ($removedMatrixHtml.Length -gt 0) $true 'previous-only matrix row is rendered'
    Assert-CaptureEqual $removedMatrixHtml.Contains('<div class="missing">Capture removed</div>') $true `
        'previous-only matrix row has removed caption'
    Assert-CaptureEqual $removedMatrixHtml.Contains('src="matrix/removed-scene-thin-100-dark.png"') $false `
        'previous-only matrix row does not reference a missing image'
    if (-not $IsWindows -and -not $IsMacOS) {
        Assert-CaptureEqual $html.Contains('case-folder/Overview.png') $true 'Linux keeps the current path case'
        Assert-CaptureEqual $html.Contains('case-folder/overview.png') $true 'Linux keeps the removed path case'
        [IO.File]::WriteAllText((Join-Path $sample 'diff.csv'),
            "file,status`ncase-folder/Overview.png,added`ncase-folder/overview.png,removed`n")
        New-MotifCaptureSheet -OutputDirectory $sample -PreviousDirectory $previous | Out-Null
        $html = Get-Content -LiteralPath (Join-Path $sample 'index.html') -Raw
        $currentStatus = [regex]::Match($html,
            '<figcaption>case-folder/Overview[.]png<div class="cell-meta">([^ <]+)').Groups[1].Value
        $previousStatus = [regex]::Match($html,
            '<figcaption>case-folder/overview[.]png<div class="cell-meta">([^ <]+)').Groups[1].Value
        Assert-CaptureEqual $currentStatus 'added' 'current case is added'
        Assert-CaptureEqual $previousStatus 'removed' 'previous case is removed'
    }

    $artboard = Join-Path $fixtureRoot 'H overview.png'
    [IO.File]::WriteAllBytes($artboard, $png)
    $referenceMap = Join-Path $fixtureRoot 'references.json'
    [IO.File]::WriteAllText($referenceMap, '{"overview":"H overview.png"}')
    & pwsh -NoProfile -File $captureScript -NoRun -NoDiff -Output $sample -ReferenceMap $referenceMap
    if ($LASTEXITCODE -ne 0) { throw "Artboard comparison failed with exit code $LASTEXITCODE." }
    $html = Get-Content -LiteralPath (Join-Path $sample 'index.html') -Raw
    foreach ($term in @('H artboard', 'Actual capture', 'No H artboard supplied', 'references/overview.png')) {
        Assert-CaptureEqual $html.Contains($term) $true "comparison contains $term"
    }
    Assert-CaptureEqual ([Convert]::ToBase64String([IO.File]::ReadAllBytes(
        (Join-Path $sample 'references/overview.png')))) ([Convert]::ToBase64String($png)) 'portable artboard copy'
    Assert-CaptureEqual $html.Contains($artboard) $false 'comparison uses relative links'
    [IO.File]::WriteAllText($referenceMap, '{"overview":"absent.png"}')
    & pwsh -NoProfile -File $captureScript -NoRun -NoDiff -Output $sample -ReferenceMap $referenceMap 2>$null
    Assert-CaptureEqual ($LASTEXITCODE -ne 0) $true 'a missing artboard fails clearly'

    $stray = Join-Path $sample 'notes.txt'
    [IO.File]::WriteAllText($stray, 'keep')
    $nestedNote = Join-Path $sample 'large-project/notes.txt'
    [IO.File]::WriteAllText($nestedNote, 'keep nested note')
    [IO.File]::WriteAllText((Join-Path $sample 'diff.csv'), 'file,status')
    Clear-MotifCaptureOutput -OutputDirectory $sample
    foreach ($gone in @('matrix', 'references', 'index.html', 'diff.csv', 'overview-page.png')) {
        Assert-CaptureEqual (Test-Path -LiteralPath (Join-Path $sample $gone)) $false "rerun clears $gone"
    }
    Assert-CaptureEqual (Test-Path -LiteralPath $stray) $true 'rerun keeps files it did not write'
    Assert-CaptureEqual (Test-Path -LiteralPath $nestedNote) $true 'rerun keeps nested notes'
    Assert-CaptureEqual (Test-Path -LiteralPath (Join-Path $sample 'large-project/nested overview.png')) $false 'rerun clears nested capture'
    [IO.Directory]::CreateDirectory($matrix) | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $matrix 'overview-thin-100-light.png'), $png)
    New-MotifCaptureSheet -OutputDirectory $sample | Out-Null
    $html = Get-Content -LiteralPath (Join-Path $sample 'index.html') -Raw
    Assert-CaptureEqual $html.Contains('large-project/nested%20overview.png') $false 'new matrix run excludes old nested capture'
    Assert-CaptureEqual $html.Contains('matrix/overview-thin-100-light.png') $true 'new matrix capture is indexed'

    Write-Host "Capture-Screens regression checks passed: $passed assertions." -ForegroundColor Green
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Capture-Screens.All.Tests.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Capture-All regression checks failed with exit code $LASTEXITCODE." }
}
finally {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}
