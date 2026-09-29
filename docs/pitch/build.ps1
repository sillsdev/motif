<#
.SYNOPSIS
  Builds the pitch from pitch.md into dist/ as PDF, HTML or PPTX.
.EXAMPLE
  ./build.ps1                # dist/A-Working-Grammar-for-Every-Language.pdf and .html
  ./build.ps1 -Format pptx   # one format: pdf, html or pptx
  ./build.ps1 -Watch         # rebuild HTML on every save
#>
param(
    [ValidateSet('all', 'pdf', 'html', 'pptx')] [string] $Format = 'all',
    [switch] $Watch
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$name = 'A-Working-Grammar-for-Every-Language'
$marp = @('--yes', '@marp-team/marp-cli@4', '.pitch.build.md', '--theme', 'theme/pitch.css', '--html', '--allow-local-files')
New-Item -ItemType Directory -Force dist | Out-Null
# The HTML build references art/ relatively, so it travels with the output.
Copy-Item -Recurse -Force art dist/

if ($Watch) {
    # Watch mode reads pitch.md directly, so diagrams show as images in fallback fonts.
    $cmd = @($marp[0], $marp[1], 'pitch.md') + $marp[3..($marp.Count - 1)] + @('--watch', '-o', "dist/$name.html")
    npx @cmd
    return
}

# A diagram is inlined rather than linked: an SVG loaded as an image cannot use the page's web fonts.
$diagram = '(?m)^!\[(?<class>[^\]]*)\]\((?<path>diagrams/[^)]+\.svg)\)[ \t]*\r?$'
$source = Get-Content -Raw -Encoding utf8 pitch.md
$inlined = [regex]::Replace($source, $diagram, {
        param($m)
        $svg = (Get-Content -Raw -Encoding utf8 $m.Groups['path'].Value).Trim()
        "<figure class=`"diagram $($m.Groups['class'].Value)`">$svg</figure>"
    })
Set-Content -NoNewline -Encoding utf8 .pitch.build.md $inlined

$formats = if ($Format -eq 'all') { @('pdf', 'html') } else { @($Format) }
foreach ($f in $formats) {
    $out = "dist/$name.$f"
    $flag = if ($f -eq 'html') { @() } else { @("--$f") }
    $cmd = $marp + $flag + @('-o', $out)
    npx @cmd
    if ($LASTEXITCODE -ne 0) { throw "marp failed for $f" }
    Write-Host "wrote $out"
}
Remove-Item .pitch.build.md
