param([string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path)
$ErrorActionPreference = 'Stop'
$errors = [System.Collections.Generic.List[string]]::new()
$adrDir = Join-Path $Root 'docs/adr'
$definitions = @{}
foreach ($file in Get-ChildItem $adrDir -Filter '*.md' | Where-Object Name -match '^\d{4}-') {
    $number = $file.Name.Substring(0, 4)
    if ($definitions.ContainsKey($number)) { $errors.Add("Duplicate ADR number: $number") }
    $decisions = @{}
    foreach ($m in [regex]::Matches([IO.File]::ReadAllText($file.FullName), '(?m)^### D(\d+)\b')) {
        $decisions[$m.Groups[1].Value] = $true
    }
    $definitions[$number] = $decisions
}
if ($definitions.Count -eq 0) { $errors.Add('No ADR definitions found.') }
$paths = @(git -C $Root ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate repository files.' }
$paths = $paths | Sort-Object -Unique
$headings = @{}
foreach ($name in $paths) {
    if ($name -match '^(textediting/|src/SIL\.Motif\.Help/|tests/SIL\.Motif\.Tests\.Cli/Golden/help/|\.github/workflows/textediting\.yml$)') { continue }
    $path = Join-Path $Root $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
    $extension = [IO.Path]::GetExtension($name)
    if ($extension -notin @('.md', '.cs', '.ps1', '.json', '.mjs', '.ts', '.yml', '.yaml', '.props', '.targets', '.csproj', '.tsv', '.txt', '.sh', '.xml', '.axaml', '.astro', '.toml')) { continue }
    $data = [IO.File]::ReadAllText($path)
    if ($name -ne 'docs/adr/README.md') {
        foreach ($m in [regex]::Matches($data, '\bADRs?\s+(\d{1,4})\b(?:,?\s*(?:decisions?|§|#|item|point|rule|D)\s*(\d+(?:\s*(?:,|/|and)\s*\d+)*))?')) {
            $before = $data.Substring([Math]::Max(0, $m.Index - 45), [Math]::Min(45, $m.Index))
            if ($before -match '(?i)TextEditing|textediting[ /]') { continue }
            $num = $m.Groups[1].Value; $decision = $m.Groups[2].Value
            if ($num.Length -ne 4 -or -not $definitions.ContainsKey($num)) { $errors.Add("${name}: unknown ADR $num") }
            elseif ($decision) {
                foreach ($item in [regex]::Matches($decision, '\d+')) {
                    if (-not $definitions[$num].ContainsKey($item.Value)) { $errors.Add("${name}: ADR $num has no decision $($item.Value)") }
                }
            }
        }
    }
    if ($extension -eq '.md') {
        foreach ($m in [regex]::Matches($data, '\[[^\]]+\]\(([^\s)]+)')) {
            $uri = $m.Groups[1].Value.Trim('<','>')
            if ($uri -match '^(?:[a-zA-Z][\w+.-]*:|/)') { continue }
            $parts = $uri.Split('#', 2); $target = [uri]::UnescapeDataString(($parts[0] -split '\?', 2)[0])
            $destination = if ($target) { [IO.Path]::GetFullPath((Join-Path (Split-Path $path) $target)) } else { $path }
            if (-not (Test-Path -LiteralPath $destination)) { $errors.Add("${name}: missing link $uri"); continue }
            if ($parts.Count -eq 2 -and $parts[1] -and [IO.Path]::GetExtension($destination) -eq '.md') {
                if (-not $headings.ContainsKey($destination)) {
                    $slugs = @{}; $counts = @{}
                    foreach ($heading in [regex]::Matches([IO.File]::ReadAllText($destination), '(?m)^#{1,6}\s+(.+?)\s*#*\s*$')) {
                        $slug = $heading.Groups[1].Value.ToLowerInvariant() -replace '<[^>]*>', '' -replace '[^\p{L}\p{N}_\-\s]', '' -replace '\s', '-'
                        if ($counts.ContainsKey($slug)) { $counts[$slug]++; $slug += '-' + $counts[$slug] } else { $counts[$slug] = 0 }
                        $slugs[$slug] = $true
                    }
                    $headings[$destination] = $slugs
                }
                $anchor = [uri]::UnescapeDataString($parts[1]).ToLowerInvariant()
                if (-not $headings[$destination].ContainsKey($anchor)) { $errors.Add("${name}: missing anchor $uri") }
            }
        }
    }
    if ($extension -in @('.cs', '.ps1')) {
        foreach ($line in $data -split '\r?\n') {
            if ($line -notmatch '^\s*(?://|#)') { continue }
            foreach ($m in [regex]::Matches($line, 'docs/[\w./-]+\.md')) {
                if (-not (Test-Path -LiteralPath (Join-Path $Root $m.Value) -PathType Leaf)) { $errors.Add("${name}: missing comment path $($m.Value)") }
            }
        }
    }
}
$errors | ForEach-Object { Write-Host $_ -ForegroundColor Red }
Write-Host "Docs hygiene: $($errors.Count) errors"
if ($errors.Count) { exit 1 }

exit 0
