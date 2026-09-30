<#
.SYNOPSIS
  Builds both decks (pitch.md and pitch-short.md) into dist/ as PDF, HTML or PPTX.
.EXAMPLE
  ./build.ps1                # both decks: A-Working-Grammar-for-Every-Language and Motif-in-Brief, PDF and HTML
  ./build.ps1 -Format pptx   # one format: pdf, html or pptx
  ./build.ps1 -Source pitch-short.md   # one deck only
  ./build.ps1 -Watch         # rebuild the full deck's HTML on every save (-Source picks another)
#>
param(
    [ValidateSet('all', 'pdf', 'html', 'pptx')] [string] $Format = 'all',
    [string] $Source = '',
    [switch] $Watch
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Each deck's output is named after its title; a deck not listed here keeps its file name.
$names = [ordered]@{ 'pitch.md' = 'A-Working-Grammar-for-Every-Language'; 'pitch-short.md' = 'Motif-in-Brief' }
$decks = if ($Source) { @($Source) } elseif ($Watch) { @('pitch.md') } else { @($names.Keys) }
foreach ($deck in $decks) { if (-not (Test-Path $deck)) { throw "no such deck: $deck" } }

function Get-OutputName([string] $deck) {
    $leaf = Split-Path -Leaf $deck
    if ($names.Contains($leaf)) { $names[$leaf] } else { [IO.Path]::GetFileNameWithoutExtension($leaf) }
}

$marpBase = @('--yes', '@marp-team/marp-cli@4')
$marpOptions = @('--theme', 'theme/pitch.css', '--html', '--allow-local-files')
New-Item -ItemType Directory -Force dist | Out-Null
# The HTML build references art/ relatively, so it travels with the output.
Copy-Item -Recurse -Force art dist/

if ($Watch) {
    # Watch mode reads the deck directly, so diagrams show as images in fallback fonts.
    $cmd = $marpBase + @($decks[0]) + $marpOptions + @('--watch', '-o', "dist/$(Get-OutputName $decks[0]).html")
    npx @cmd
    return
}

# A diagram is inlined rather than linked: an SVG loaded as an image cannot use the page's web fonts.
$diagram = '(?m)^!\[(?<class>[^\]]*)\]\((?<path>diagrams/[^)]+\.svg)\)[ \t]*\r?$'
$figure = '(?m)^!\[(?<class>[^\]]*)\]\((?<path>figures/[^)]+\.dc\.html)\)[ \t]*\r?$'
$formats = if ($Format -eq 'all') { @('pdf', 'html') } else { @($Format) }
foreach ($deck in $decks) {
    $name = Get-OutputName $deck
    # One intermediate file per deck, so two builds in this folder never overwrite each other's.
    $build = ".$([IO.Path]::GetFileNameWithoutExtension($deck)).build.md"
    $text = Get-Content -Raw -Encoding utf8 $deck
    $inlined = [regex]::Replace($text, $diagram, {
            param($m)
            $svg = (Get-Content -Raw -Encoding utf8 $m.Groups['path'].Value).Trim()
            "<figure class=`"diagram $($m.Groups['class'].Value)`">$svg</figure>"
        })
    # A figure is a Claude Design artboard, kept verbatim: only the markup between its <helmet> and the end of
    # <x-dc> is drawing, and it is drawn at its slot's exact size, so it goes in unscaled.
    $inlined = [regex]::Replace($inlined, $figure, {
            param($m)
            $page = Get-Content -Raw -Encoding utf8 $m.Groups['path'].Value
            $body = [regex]::Match($page, '(?s)</helmet>(?<body>.*)</x-dc>')
            if (-not $body.Success) { throw "not an artboard: $($m.Groups['path'].Value)" }
            "<figure class=`"frame $($m.Groups['class'].Value)`">$($body.Groups['body'].Value.Trim())</figure>"
        })
    Set-Content -NoNewline -Encoding utf8 $build $inlined
    try {
        foreach ($f in $formats) {
            $out = "dist/$name.$f"
            $flag = if ($f -eq 'html') { @() } else { @("--$f") }
            $cmd = $marpBase + @($build) + $marpOptions + $flag + @('-o', $out)
            npx @cmd
            if ($LASTEXITCODE -ne 0) { throw "marp failed for $deck ($f)" }
            Write-Host "wrote $out"
        }
    }
    finally {
        Remove-Item $build -ErrorAction SilentlyContinue
    }
}
