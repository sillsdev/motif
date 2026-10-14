Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MatrixBatches.ps1')

function ConvertTo-MotifCaptureValues {
    param(
        [AllowNull()][string[]] $Values,
        [string[]] $Allowed,
        [string] $Name
    )

    $items = @($Values | ForEach-Object { ([string]$_ -split ',') } | ForEach-Object { $_.Trim() } |
        Where-Object { $_.Length -gt 0 })
    if ($items.Count -eq 0) { return @() }
    foreach ($item in $items) {
        if ($Allowed -and $item -notin $Allowed) {
            throw "Unsupported $Name '$item'. Choose from: $($Allowed -join ', ')."
        }
    }
    return @($items | Select-Object -Unique)
}

function Get-MotifCaptureEnvironment {
    param(
        [Parameter(Mandatory)][string] $OutputDirectory,
        [string[]] $Scenes,
        [string[]] $Sizes,
        [string[]] $Zooms,
        [string[]] $Themes,
        [switch] $All
    )

    $scenes = @(ConvertTo-MotifCaptureValues $Scenes @() 'scene')
    $sizes = @(ConvertTo-MotifCaptureValues $Sizes @('thin', 'half', 'full', 'short') 'size')
    $zooms = @(ConvertTo-MotifCaptureValues $Zooms @('100', '150') 'zoom')
    $themes = @(ConvertTo-MotifCaptureValues $Themes @('light', 'dark') 'theme')
    if ($All -and ($scenes.Count -gt 0 -or $sizes.Count -gt 0 -or
            $zooms.Count -gt 0 -or $themes.Count -gt 0)) {
        throw '-All cannot be combined with -Scenes, -Sizes, -Zooms, or -Themes.'
    }

    return [ordered]@{
        MOTIF_SCREENSHOTS = [IO.Path]::GetFullPath($OutputDirectory)
        MOTIF_CAPTURE_SCENES = if ($scenes.Count) { $scenes -join ',' } else { $null }
        MOTIF_CAPTURE_SIZES = if ($sizes.Count) { $sizes -join ',' } else { $null }
        MOTIF_CAPTURE_ZOOMS = if ($zooms.Count) { $zooms -join ',' } else { $null }
        MOTIF_CAPTURE_THEMES = if ($themes.Count) { $themes -join ',' } else { $null }
        MOTIF_CAPTURE_MATRIX = if ($All) { 'all' } else { $null }
    }
}

function Resolve-MotifCaptureOutputPath {
    param(
        [Parameter(Mandatory)][string] $RepoRoot,
        [string] $OutputDirectory
    )

    if (-not [string]::IsNullOrWhiteSpace($OutputDirectory)) {
        if ([IO.Path]::IsPathRooted($OutputDirectory)) { return [IO.Path]::GetFullPath($OutputDirectory) }
        return [IO.Path]::GetFullPath((Join-Path $RepoRoot $OutputDirectory))
    }

    $captureRoot = Join-Path $RepoRoot 'bin/captures'
    $stamp = Get-Date -Format 'yyyy-MM-dd-HHmm'
    $candidate = Join-Path $captureRoot $stamp
    $suffix = 1
    while (Test-Path -LiteralPath $candidate) {
        $candidate = Join-Path $captureRoot ('{0}-{1:D2}' -f $stamp, $suffix)
        $suffix++
    }
    return [IO.Path]::GetFullPath($candidate)
}

function Clear-MotifCaptureOutput {
    param([Parameter(Mandatory)][string] $OutputDirectory)

    if (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container)) { return }
    # A skipped nested scene must not leave old pictures in the next run.
    foreach ($name in @('matrix', 'diff', 'references')) {
        Remove-Item -LiteralPath (Join-Path $OutputDirectory $name) -Recurse -Force -ErrorAction SilentlyContinue
    }
    foreach ($name in @('index.html', 'diff.csv')) {
        Remove-Item -LiteralPath (Join-Path $OutputDirectory $name) -Force -ErrorAction SilentlyContinue
    }
    Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File -Filter '*.png' | Remove-Item -Force
}

function Resolve-MotifCaptureLatest {
    param([Parameter(Mandatory)][string] $CaptureRoot)

    $latestFile = Join-Path $CaptureRoot 'latest'
    if (-not (Test-Path -LiteralPath $latestFile -PathType Leaf)) { return $null }
    $value = (Get-Content -LiteralPath $latestFile -Raw).Trim()
    if (-not $value) { return $null }
    $path = if ([IO.Path]::IsPathRooted($value)) { $value } else { Join-Path $CaptureRoot $value }
    $path = [IO.Path]::GetFullPath($path)
    if (Test-Path -LiteralPath $path -PathType Container) { return $path }
    return $null
}

function Write-MotifCaptureLatest {
    param(
        [Parameter(Mandatory)][string] $CaptureRoot,
        [Parameter(Mandatory)][string] $OutputDirectory
    )

    $root = [IO.Path]::GetFullPath($CaptureRoot)
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $relative = [IO.Path]::GetRelativePath($root, $output)
    $value = if ($relative -ne '..' -and -not $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::Ordinal)) { $relative } else { $output }
    [IO.File]::WriteAllText((Join-Path $root 'latest'), $value + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
}

function Set-MotifCaptureEnvironment {
    param([Parameter(Mandatory)][System.Collections.IDictionary] $Values)

    $previous = [ordered]@{}
    foreach ($name in $Values.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable([string]$name, 'Process')
        $value = $Values[$name]
        [Environment]::SetEnvironmentVariable([string]$name, $value, 'Process')
    }
    return $previous
}

function Restore-MotifCaptureEnvironment {
    param([Parameter(Mandatory)][System.Collections.IDictionary] $Values)

    foreach ($name in $Values.Keys) {
        [Environment]::SetEnvironmentVariable([string]$name, $Values[$name], 'Process')
    }
}

function Get-CaptureField {
    param([AllowNull()][object] $Value, [Parameter(Mandatory)][string] $Name)

    if ($null -eq $Value) { return $null }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) { if ([string]$key -ieq $Name) { return $Value[$key] } }
        return $null
    }
    foreach ($property in $Value.PSObject.Properties) {
        if ($property.Name -ieq $Name) { return $property.Value }
    }
    return $null
}

function ConvertTo-CaptureRelativePath {
    param(
        [Parameter(Mandatory)][string] $CaptureRoot,
        [Parameter(Mandatory)][string] $File,
        [switch] $Matrix
    )

    $normalized = $File.Replace('\', '/') -replace '^\./', ''
    if ($Matrix -and -not $normalized.StartsWith('matrix/', [StringComparison]::OrdinalIgnoreCase)) {
        $matrixRelative = 'matrix/' + $normalized
        $matrixPath = Join-Path $CaptureRoot ($matrixRelative.Replace('/', [IO.Path]::DirectorySeparatorChar))
        $rootPath = Join-Path $CaptureRoot ($normalized.Replace('/', [IO.Path]::DirectorySeparatorChar))
        if ((Test-Path -LiteralPath $matrixPath -PathType Leaf) -or
            -not (Test-Path -LiteralPath $rootPath -PathType Leaf)) { return $matrixRelative }
    }
    return $normalized
}

function Get-MotifMatrixEntries {
    param([Parameter(Mandatory)][string] $CaptureRoot)

    $matrixDirectory = Join-Path $CaptureRoot 'matrix'
    $indexPath = Join-Path $matrixDirectory 'index.csv'
    $entries = [System.Collections.Generic.List[object]]::new()
    if (Test-Path -LiteralPath $indexPath -PathType Leaf) {
        foreach ($row in (Import-Csv -LiteralPath $indexPath)) {
            $file = [string](Get-CaptureField $row 'file')
            $scene = [string](Get-CaptureField $row 'scene')
            if (-not $file -or -not $scene) { continue }
            $size = [string](Get-CaptureField $row 'size')
            $zoom = [string](Get-CaptureField $row 'zoom')
            $theme = [string](Get-CaptureField $row 'theme')
            if ($size -match '^(?<base>.+?)(?<suffix>100|150)$') {
                $size = $Matches['base']
                if (-not $zoom) { $zoom = $Matches['suffix'] }
            }
            if (-not $zoom) { $zoom = '100' }
            if (-not $theme -and $file -match '-(?<theme>light|dark)\.png$') { $theme = $Matches['theme'] }
            if (-not $theme) { $theme = 'light' }
            $relative = ConvertTo-CaptureRelativePath $CaptureRoot $file -Matrix
            $entries.Add([pscustomobject]@{
                File = $relative
                Scene = $scene
                Size = if ($size) { $size } else { 'unknown' }
                Zoom = $zoom
                Theme = $theme
                Width = Get-CaptureField $row 'width'
                Height = Get-CaptureField $row 'height'
                FindingCount = if ($null -ne (Get-CaptureField $row 'findings')) {
                    Get-CaptureField $row 'findings'
                } else {
                    Get-CaptureField $row 'finding count'
                }
                Available = Test-Path -LiteralPath (Join-Path $CaptureRoot $relative.Replace('/', [IO.Path]::DirectorySeparatorChar)) -PathType Leaf
            })
        }
        return @($entries)
    }

    if (Test-Path -LiteralPath $matrixDirectory -PathType Container) {
        foreach ($image in Get-ChildItem -LiteralPath $matrixDirectory -File -Filter '*.png') {
            if ($image.BaseName -match '^(?<scene>.+)-(?<size>thin|half|full|short)-(?<zoom>100|150)-(?<theme>light|dark)$') {
                $entries.Add([pscustomobject]@{
                    File = 'matrix/' + $image.Name; Scene = $Matches['scene']; Size = $Matches['size']
                    Zoom = $Matches['zoom']; Theme = $Matches['theme']; Width = $null; Height = $null
                    FindingCount = 0; Available = $true
                })
            }
        }
    }
    return @($entries)
}

function New-MotifCapturePathMap {
    $comparer = if ($IsWindows -or $IsMacOS) { [StringComparer]::OrdinalIgnoreCase }
        else { [StringComparer]::Ordinal }
    return [System.Collections.Generic.Dictionary[string,object]]::new($comparer)
}

function Get-MotifFindingMap {
    param([Parameter(Mandatory)][string] $CaptureRoot)

    $path = Join-Path $CaptureRoot 'matrix/findings.json'
    $map = New-MotifCapturePathMap
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $map }
    $items = @(Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable)
    foreach ($item in $items) {
        $file = [string](Get-CaptureField $item 'file')
        if (-not $file) { continue }
        $relative = ConvertTo-CaptureRelativePath $CaptureRoot $file -Matrix
        if (-not $map.ContainsKey($relative)) { $map[$relative] = [System.Collections.Generic.List[object]]::new() }
        $map[$relative].Add($item)
    }
    return $map
}

function Get-MotifDiffMap {
    param([Parameter(Mandatory)][string] $CaptureRoot)

    $path = Join-Path $CaptureRoot 'diff.csv'
    $map = New-MotifCapturePathMap
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $map }
    foreach ($row in Import-Csv -LiteralPath $path) {
        $file = ([string](Get-CaptureField $row 'file')).Replace('\', '/')
        if ($file) { $map[$file] = $row }
    }
    return $map
}

function ConvertTo-CaptureHtmlPath {
    param([Parameter(Mandatory)][string] $File)

    return (($File -split '/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/'
}

function ConvertTo-CaptureFindingHtml {
    param([object[]] $Findings, [string] $RecordedCount)

    $count = @($Findings).Count
    $shownCount = if ($RecordedCount -match '^\d+$') { [int]$RecordedCount } else { $count }
    if ($shownCount -eq 0) { return '<span class="finding-count">0 findings</span>' }
    $builder = [Text.StringBuilder]::new()
    [void]$builder.Append('<details class="findings"><summary>').Append($shownCount).Append(' findings</summary><ul>')
    foreach ($finding in $Findings) {
        $check = [string](Get-CaptureField $finding 'check')
        $detail = [string](Get-CaptureField $finding 'detail')
        $expected = [string](Get-CaptureField $finding 'expected')
        $bounds = Get-CaptureField $finding 'bounds'
        $control = Get-CaptureField $finding 'control'
        $controlText = if ($control -is [string]) { $control } else {
            $name = [string](Get-CaptureField $control 'name')
            $type = [string](Get-CaptureField $control 'type')
            if ($name -and $type) { "${type}: $name" } elseif ($name) { $name } else { $type }
        }
        $boundsText = if ($bounds -is [string]) { $bounds } elseif ($null -ne $bounds) {
            ConvertTo-Json -InputObject $bounds -Compress -Depth 8
        }
        $line = @($check, $controlText, $boundsText, $detail, $(if ($expected) { "Expected: $expected" })) |
            Where-Object { $_ } | ForEach-Object { [Net.WebUtility]::HtmlEncode($_) }
        [void]$builder.Append('<li>').Append(($line -join ' — ')).Append('</li>')
    }
    if ($count -eq 0) {
        [void]$builder.Append('<li>Finding details are unavailable in findings.json.</li>')
    }
    [void]$builder.Append('</ul></details>')
    return $builder.ToString()
}

function Get-MotifCapturePrefix {
    param([Parameter(Mandatory)][string] $FileName)

    $baseName = [IO.Path]::GetFileNameWithoutExtension($FileName)
    if ($baseName -match '^(?<prefix>.+?)-\d{3,4}(?:-.*)?$') { return $Matches['prefix'] }
    if ($baseName -match '^(?<prefix>\d+[a-z]?-[^-]+)') { return $Matches['prefix'] }
    return ($baseName -split '-')[0]
}

function Get-MotifOtherCaptureImages {
    param([Parameter(Mandatory)][string] $Directory)

    $root = [IO.Path]::GetFullPath($Directory)
    $comparison = if ($IsWindows -or $IsMacOS) { [StringComparison]::OrdinalIgnoreCase }
        else { [StringComparison]::Ordinal }
    $excludedRoots = @('matrix', 'diff', 'references') | ForEach-Object {
        [IO.Path]::GetFullPath((Join-Path $root $_)) + [IO.Path]::DirectorySeparatorChar
    }
    foreach ($image in Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.png' | Sort-Object FullName) {
        $excluded = $false
        foreach ($excludedRoot in $excludedRoots) {
            if ($image.FullName.StartsWith($excludedRoot, $comparison)) { $excluded = $true; break }
        }
        if (-not $excluded) { [IO.Path]::GetRelativePath($root, $image.FullName).Replace('\', '/') }
    }
}

function New-MotifCaptureSheet {
    param(
        [Parameter(Mandatory)][string] $OutputDirectory,
        [string] $PreviousDirectory,
        [string] $ReferenceMap
    )

    $output = [IO.Path]::GetFullPath($OutputDirectory)
    if (-not (Test-Path -LiteralPath $output -PathType Container)) {
        throw "Capture output folder not found: $output"
    }
    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in (Get-MotifMatrixEntries $output)) { $entries.Add($entry) }
    $entryArray = @($entries.ToArray())
    $references = @{}
    if ($ReferenceMap) {
        $mapPath = [IO.Path]::GetFullPath($ReferenceMap)
        $map = Get-Content -LiteralPath $mapPath -Raw | ConvertFrom-Json -AsHashtable
        if ($map -isnot [System.Collections.IDictionary]) { throw 'ReferenceMap must contain a JSON object.' }
        foreach ($scene in @($entryArray | ForEach-Object { $_.Scene } | Sort-Object -Unique)) {
            if (-not $map.ContainsKey($scene)) { continue }
            $source = [string]$map[$scene]
            if (-not [IO.Path]::IsPathRooted($source)) {
                $source = Join-Path (Split-Path -Parent $mapPath) $source
            }
            if ([IO.Path]::GetExtension($source) -ine '.png' -or
                -not (Test-Path -LiteralPath $source -PathType Leaf)) {
                throw "H artboard for '$scene' must name an existing PNG: $source"
            }
            $safeScene = $scene -replace '[^a-zA-Z0-9_-]', '-'
            $relative = "references/$safeScene.png"
            $destination = Join-Path $output $relative
            [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
            if ([IO.Path]::GetFullPath($source) -ne [IO.Path]::GetFullPath($destination)) {
                Copy-Item -LiteralPath $source -Destination $destination -Force
            }
            $references[$scene] = ConvertTo-CaptureHtmlPath $relative
        }
    }
    $currentFiles = New-MotifCapturePathMap
    foreach ($entry in $entryArray) { $currentFiles[$entry.File] = $true }
    $previousEntries = @()
    if (-not [string]::IsNullOrWhiteSpace($PreviousDirectory) -and
        (Test-Path -LiteralPath $PreviousDirectory -PathType Container) -and
        [IO.Path]::GetFullPath($PreviousDirectory) -ne $output) {
        $previousEntries = @(Get-MotifMatrixEntries ([IO.Path]::GetFullPath($PreviousDirectory)))
        foreach ($entry in $previousEntries) {
            if (-not $currentFiles.ContainsKey($entry.File)) {
                $entries.Add([pscustomobject]@{
                    File = $entry.File; Scene = $entry.Scene; Size = $entry.Size; Zoom = $entry.Zoom
                    Theme = $entry.Theme; Width = $entry.Width; Height = $entry.Height
                    FindingCount = 0; Available = $false
                })
            }
        }
    }
    $entryArray = @($entries.ToArray())

    $findings = Get-MotifFindingMap $output
    $diffs = Get-MotifDiffMap $output
    $sizes = @('thin', 'half', 'full', 'short') + @($entryArray | ForEach-Object { $_.Size } |
        Where-Object { $_ -notin @('thin', 'half', 'full', 'short') } | Sort-Object -Unique)
    $sizes = @($sizes | Select-Object -Unique)
    $zooms = @($entryArray | ForEach-Object { [int]$_.Zoom } | Sort-Object -Unique)
    if ($zooms.Count -eq 0) { $zooms = @(100, 150) }
    $themes = @($entryArray | ForEach-Object { $_.Theme } | Sort-Object -Unique)
    if ($themes.Count -eq 0) { $themes = @('light', 'dark') }
    $scenes = @($entryArray | Group-Object Scene | Sort-Object Name)
    $builder = [Text.StringBuilder]::new()
    [void]$builder.AppendLine('<!doctype html>')
    [void]$builder.AppendLine('<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">')
    [void]$builder.AppendLine('<title>Motif capture review</title><style>')
    [void]$builder.AppendLine(@'
:root{color-scheme:light dark;font:14px/1.4 system-ui,sans-serif;background:#1e1e1e;color:#ddd}body{margin:0;padding:1rem}h1{margin:.2rem 0 1rem}h2{margin:.3rem 0 .8rem;font-size:1.1rem}h3{margin:.3rem 0}.toolbar{position:sticky;top:0;z-index:2;display:flex;gap:1rem;align-items:center;flex-wrap:wrap;padding:.8rem;background:#252526;border-bottom:1px solid #555}input[type=search]{min-width:18rem;padding:.5rem}label{display:flex;gap:.4rem;align-items:center}.scene,.capture-group{margin:1rem 0;padding:.8rem;background:#252526;border:1px solid #444}.matrix-grid{display:grid;grid-template-columns:minmax(5rem,.45fr) repeat(var(--columns),minmax(10rem,1fr));gap:.35rem;overflow-x:auto}.column-label,.size-label{align-self:center;padding:.35rem .5rem;font-weight:600}.matrix-cell,.capture-cell{min-width:0;padding:.4rem;background:#303030;border:1px solid #484848}.matrix-cell img,.capture-cell img{display:block;width:100%;height:auto;max-height:34rem;object-fit:contain;background:#151515}.thumb-link{display:block}.cell-meta{display:flex;gap:.45rem;align-items:center;flex-wrap:wrap;margin:.3rem 0}.badge{padding:.1rem .4rem;border-radius:999px;background:#9e3d20;color:white;font-size:.75rem}.badge.same{background:#3d6945}.finding-count{color:#efb366}.findings{margin-top:.25rem}.findings ul{padding-left:1.25rem;margin:.3rem 0}.findings li{margin:.35rem 0}.missing{padding:2rem .3rem;text-align:center;color:#e38d78}.capture-list{display:grid;grid-template-columns:repeat(auto-fill,minmax(15rem,1fr));gap:.5rem}.capture-cell{overflow:hidden}.capture-cell figcaption{overflow-wrap:anywhere}.empty{color:#999;padding:.7rem}.foot{color:#aaa;font-size:.85rem;margin-top:1rem}a{color:#75baff}details summary{cursor:pointer}
'@)
    [void]$builder.AppendLine('.comparison{display:grid;grid-template-columns:1fr 1fr;gap:.4rem}.comparison figure{margin:0;min-width:0}.comparison figcaption{font-size:.8rem}')
    [void]$builder.AppendLine('</style></head><body>')
    [void]$builder.AppendLine('<h1>Motif capture review</h1>')
    $completenessPath = Join-Path $output 'matrix/capture-completeness.json'
    if (Test-Path -LiteralPath $completenessPath -PathType Leaf) {
        $completeness = [IO.File]::ReadAllText($completenessPath) | ConvertFrom-Json -AsHashtable
        $status = if ($completeness.complete) { 'Matrix capture complete' } else { 'Matrix capture incomplete' }
        $runStatus = if ($completeness.success) { 'Screenshot runs passed.' } else { 'Capture command failed; inspect the batch logs and findings.' }
        [void]$builder.Append('<p class="capture-status">').Append($status).Append(': ')
        [void]$builder.Append($completeness.captured.Count).Append(' of ').Append($completeness.expected.Count)
        [void]$builder.Append(' validated cells. ').Append($completeness.absent.Count).Append(' absent. ')
        [void]$builder.Append($runStatus).AppendLine(' <a href="matrix/capture-completeness.json">Completeness inventory</a></p>')
    }
    [void]$builder.AppendLine('<div class="toolbar"><label>Filter scenes <input id="scene-filter" type="search" placeholder="Scene name"></label><label><input id="findings-only" type="checkbox">Only cells with findings</label><label><input id="changed-only" type="checkbox">Only changed since previous</label></div>')
    foreach ($sceneGroup in $scenes) {
        $scene = [string]$sceneGroup.Name
        $sceneEncoded = [Net.WebUtility]::HtmlEncode($scene)
        [void]$builder.Append('<section class="scene" data-scene="').Append($sceneEncoded).AppendLine('"><h2>')
        [void]$builder.Append($sceneEncoded).AppendLine('</h2>')
        if ($ReferenceMap -and -not $references.ContainsKey($scene)) {
            [void]$builder.AppendLine('<p>No H artboard supplied for this scene.</p>')
        }
        [void]$builder.Append('<div class="matrix-grid" style="--columns:').Append($zooms.Count * $themes.Count).AppendLine('">')
        [void]$builder.AppendLine('<div></div>')
        foreach ($zoom in $zooms) {
            foreach ($theme in $themes) {
                [void]$builder.Append('<div class="column-label">').Append($zoom).Append('% ').Append([Net.WebUtility]::HtmlEncode($theme)).AppendLine('</div>')
            }
        }
        foreach ($size in $sizes) {
            [void]$builder.Append('<div class="size-label">').Append([Net.WebUtility]::HtmlEncode([string]$size)).AppendLine('</div>')
            foreach ($zoom in $zooms) {
                foreach ($theme in $themes) {
                    $cell = @($sceneGroup.Group | Where-Object { $_.Size -eq $size -and [int]$_.Zoom -eq $zoom -and $_.Theme -eq $theme } | Select-Object -First 1)
                    if ($cell.Count -eq 0) { [void]$builder.AppendLine('<div class="empty">—</div>'); continue }
                    $capture = $cell[0]
                    $relative = [string]$capture.File
                    $status = 'same'
                    if ($diffs.ContainsKey($relative)) { $status = [string](Get-CaptureField $diffs[$relative] 'status') }
                    elseif (-not $capture.Available) { $status = 'removed' }
                    elseif ($null -eq $PreviousDirectory) { $status = 'added' }
                    [object[]]$findingList = @()
                    if ($findings.ContainsKey($relative)) {
                        $findingList = $findings[$relative].ToArray()
                    }
                    $recordedCount = [string]$capture.FindingCount
                    if ($findingList.Count -gt 0) { $recordedCount = [string]$findingList.Count }
                    $hasFindings = $recordedCount -match '^[1-9]\d*$'
                    $isChanged = $status -in @('changed', 'added', 'removed', 'resized')
                    $htmlPath = ConvertTo-CaptureHtmlPath $relative
                    $fullPath = Join-Path $output $relative.Replace('/', [IO.Path]::DirectorySeparatorChar)
                    [void]$builder.Append('<article class="matrix-cell capture-cell" data-findings="').Append(([string][bool]$hasFindings).ToLowerInvariant()).Append('" data-changed="').Append(([string][bool]$isChanged).ToLowerInvariant()).Append('" data-scene="').Append($sceneEncoded).AppendLine('">')
                    if ($references.ContainsKey($scene)) {
                        $reference = $references[$scene]
                        [void]$builder.Append('<div class="comparison"><figure><figcaption>H artboard (reference geometry)</figcaption><a href="').Append($reference).Append('" target="_blank" rel="noopener"><img loading="lazy" src="').Append($reference).Append('" alt="H artboard: ').Append($sceneEncoded).AppendLine('"></a></figure><figure><figcaption>Actual capture</figcaption>')
                    }
                    if ($capture.Available -and (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
                        [void]$builder.Append('<a class="thumb-link" href="').Append($htmlPath).Append('" target="_blank" rel="noopener"><img loading="lazy" src="').Append($htmlPath).Append('" alt="').Append($sceneEncoded).Append(' ').Append($size).Append(' ').Append($zoom).Append('% ').Append($theme).AppendLine('"></a>')
                    }
                    else { [void]$builder.AppendLine('<div class="missing">Capture removed</div>') }
                    if ($references.ContainsKey($scene)) { [void]$builder.AppendLine('</figure></div>') }
                    [void]$builder.Append('<div class="cell-meta"><span>').Append([Net.WebUtility]::HtmlEncode([string]$status)).Append('</span>')
                    if ($isChanged) { [void]$builder.Append('<span class="badge">').Append([Net.WebUtility]::HtmlEncode([string]$status)).Append('</span>') }
                    if ($diffs.ContainsKey($relative) -and $status -in @('changed', 'resized', 'same')) {
                        $diffPath = 'diff/' + $relative
                        [void]$builder.Append('<a href="').Append((ConvertTo-CaptureHtmlPath $diffPath)).Append('" target="_blank" rel="noopener">diff</a>')
                    }
                    [void]$builder.AppendLine('</div>')
                    [void]$builder.Append((ConvertTo-CaptureFindingHtml $findingList $recordedCount))
                    [void]$builder.AppendLine('</article>')
                }
            }
        }
        [void]$builder.AppendLine('</div></section>')
    }

    $otherImages = [System.Collections.Generic.List[object]]::new()
    foreach ($relative in Get-MotifOtherCaptureImages -Directory $output) {
        $otherImages.Add([pscustomobject]@{ Name = $relative; Available = $true })
    }
    if (-not [string]::IsNullOrWhiteSpace($PreviousDirectory) -and
        (Test-Path -LiteralPath $PreviousDirectory -PathType Container) -and
        [IO.Path]::GetFullPath($PreviousDirectory) -ne $output) {
        $currentOther = New-MotifCapturePathMap
        foreach ($image in $otherImages) { $currentOther[$image.Name] = $true }
        foreach ($relative in Get-MotifOtherCaptureImages -Directory $PreviousDirectory) {
            if (-not $currentOther.ContainsKey($relative)) {
                $otherImages.Add([pscustomobject]@{ Name = $relative; Available = $false })
            }
        }
    }
    if ($otherImages.Count -gt 0) {
        [void]$builder.AppendLine('<section><h2>Other captures</h2>')
        foreach ($group in ($otherImages | Group-Object { Get-MotifCapturePrefix $_.Name } | Sort-Object Name)) {
            [void]$builder.Append('<section class="capture-group" data-scene="').Append([Net.WebUtility]::HtmlEncode($group.Name)).AppendLine('"><h3>')
            [void]$builder.Append([Net.WebUtility]::HtmlEncode($group.Name)).AppendLine('</h3><div class="capture-list">')
            foreach ($image in $group.Group) {
                $relative = $image.Name
                $status = 'same'
                if ($diffs.ContainsKey($relative)) { $status = [string](Get-CaptureField $diffs[$relative] 'status') }
                elseif (-not $image.Available) { $status = 'removed' }
                elseif ($null -eq $PreviousDirectory) { $status = 'added' }
                $isChanged = $status -in @('changed', 'added', 'removed', 'resized')
                $htmlPath = ConvertTo-CaptureHtmlPath $relative
                [void]$builder.Append('<figure class="capture-cell" data-findings="false" data-changed="').Append(([string][bool]$isChanged).ToLowerInvariant()).Append('" data-scene="').Append([Net.WebUtility]::HtmlEncode($group.Name)).AppendLine('">')
                if ($image.Available) {
                    [void]$builder.Append('<a class="thumb-link" href="').Append($htmlPath).Append('" target="_blank" rel="noopener"><img loading="lazy" src="').Append($htmlPath).Append('" alt="').Append([Net.WebUtility]::HtmlEncode($image.Name)).AppendLine('"></a>')
                } else { [void]$builder.AppendLine('<div class="missing">Capture removed</div>') }
                [void]$builder.Append('<figcaption>').Append([Net.WebUtility]::HtmlEncode($image.Name)).Append('<div class="cell-meta">').Append([Net.WebUtility]::HtmlEncode($status))
                if ($isChanged) { [void]$builder.Append(' <span class="badge">').Append([Net.WebUtility]::HtmlEncode($status)).Append('</span>') }
                if ($diffs.ContainsKey($relative) -and $status -in @('changed', 'resized', 'same')) {
                    [void]$builder.Append(' <a href="').Append((ConvertTo-CaptureHtmlPath ('diff/' + $relative))).Append('" target="_blank" rel="noopener">diff</a>')
                }
                [void]$builder.AppendLine('</div></figcaption></figure>')
            }
            [void]$builder.AppendLine('</div></section>')
        }
        [void]$builder.AppendLine('</section>')
    }
    if ($entryArray.Count -eq 0 -and $otherImages.Count -eq 0) {
        [void]$builder.AppendLine('<p class="empty">No PNG captures were found in this folder.</p>')
    }
    [void]$builder.AppendLine('<p class="foot">Images stay in this capture folder. Use the links to open full-size captures and pixel diffs.</p>')
    [void]$builder.AppendLine(@'
<script>
const sceneFilter=document.getElementById('scene-filter');
const findingsOnly=document.getElementById('findings-only');
const changedOnly=document.getElementById('changed-only');
function updateSheet(){
 const query=sceneFilter.value.trim().toLocaleLowerCase();
 document.querySelectorAll('.capture-cell').forEach(cell=>{
  const scene=(cell.dataset.scene||'').toLocaleLowerCase();
  const visible=scene.includes(query)&&(!findingsOnly.checked||cell.dataset.findings==='true')&&(!changedOnly.checked||cell.dataset.changed==='true');
  cell.hidden=!visible;
 });
 document.querySelectorAll('.scene,.capture-group').forEach(section=>{
  const name=(section.dataset.scene||'').toLocaleLowerCase();
  const visible=section.querySelector('.capture-cell:not([hidden])')!==null&&(section.classList.contains('capture-group')||name.includes(query));
  section.hidden=!visible;
 });
}
sceneFilter.addEventListener('input',updateSheet);
findingsOnly.addEventListener('change',updateSheet);
changedOnly.addEventListener('change',updateSheet);
updateSheet();
</script></body></html>
'@)
    $indexPath = Join-Path $output 'index.html'
    [IO.File]::WriteAllText($indexPath, $builder.ToString(), [Text.UTF8Encoding]::new($false))
    return $indexPath
}

Export-ModuleMember -Function ConvertTo-MotifCaptureValues, Get-MotifCaptureEnvironment,
    Resolve-MotifCaptureOutputPath, Clear-MotifCaptureOutput, Resolve-MotifCaptureLatest, Write-MotifCaptureLatest,
    Set-MotifCaptureEnvironment, Restore-MotifCaptureEnvironment, New-MotifCaptureSheet, Split-MotifCaptureScenes, Read-MotifCapturePlan,
    Save-MotifCaptureBatch, Merge-MotifCaptureBatches, Invoke-MotifAllCaptures
