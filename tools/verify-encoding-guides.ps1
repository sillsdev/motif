[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $EvalSetsPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$setsPath = (Resolve-Path -LiteralPath $EvalSetsPath).Path
$wordFiles = @(Get-ChildItem -LiteralPath $setsPath -Directory | ForEach-Object {
    Get-ChildItem -LiteralPath (Join-Path $_.FullName 'words') -Filter '*.txt' -File -ErrorAction SilentlyContinue
})
if ($wordFiles.Count -eq 0) { throw 'No eval-set word lists were found.' }

$guidePath = Join-Path $PSScriptRoot '../src/SIL.Motif.Mcp/Resources/encoding-guides'
$guides = @(Get-ChildItem -LiteralPath $guidePath -Filter '*.md' -File)
if ($guides.Count -eq 0) { throw 'No encoding guides were found.' }

$forms = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($wordFile in $wordFiles) {
    foreach ($line in [IO.File]::ReadLines($wordFile.FullName)) {
        $form = $line.Trim().Normalize([Text.NormalizationForm]::FormC)
        if ($form.Length -gt 0) { [void]$forms.Add($form) }
    }
}

$matches = 0
foreach ($guide in $guides) {
    $text = [IO.File]::ReadAllText($guide.FullName).Normalize([Text.NormalizationForm]::FormC)
    foreach ($form in $forms) {
        $pattern = '(?<![\p{L}\p{M}\p{N}])' + [regex]::Escape($form) + '(?![\p{L}\p{M}\p{N}])'
        if ([regex]::IsMatch($text, $pattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            $matches++
        }
    }
}

if ($matches -gt 0) { throw "Found $matches eval-set word-form match(es) in encoding guides." }
Write-Host "Checked $($guides.Count) guides against $($forms.Count) eval-set word forms from $($wordFiles.Count) lists; no matches."
