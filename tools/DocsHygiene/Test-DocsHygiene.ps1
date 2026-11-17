$ErrorActionPreference = 'Stop'
$checker = Join-Path $PSScriptRoot 'Check-Docs.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('motif-docs-hygiene-' + [guid]::NewGuid())
try {
    New-Item -ItemType Directory -Path (Join-Path $root 'docs/adr') -Force | Out-Null
    & git -C $root init --quiet
    if ($LASTEXITCODE) { throw 'Could not initialize fixture repository.' }
    $adr = Join-Path $root 'docs/adr/0001-first.md'
    Set-Content -LiteralPath $adr -Value "# First`n`n### D1 One`n" -Encoding utf8
    Set-Content -LiteralPath (Join-Path $root 'docs/adr/README.md') -Value '# Crosswalk' -Encoding utf8
    $page = Join-Path $root 'page.md'
    function Check([bool]$shouldPass, [string]$case) {
        & pwsh -NoProfile -File $checker -Root $root | Out-Null
        if (($LASTEXITCODE -eq 0) -ne $shouldPass) { throw "Unexpected result: $case" }
        Write-Host "PASS $case"
    }
    Set-Content $page '[first](docs/adr/0001-first.md#d1-one) and ADR 0001 decision 1' -Encoding utf8
    Check $true 'valid link, anchor and decision'
    Set-Content $page '[missing](missing.md)' -Encoding utf8
    Check $false 'missing file'
    Set-Content $page '![missing](missing.png)' -Encoding utf8
    Check $false 'missing image'
    Set-Content $page '[missing](docs/adr/0001-first.md#absent)' -Encoding utf8
    Check $false 'missing anchor'
    Set-Content $page ('ADR ' + '9999') -Encoding utf8
    Check $false 'unknown ADR'
    Set-Content $page ('ADR ' + '0001 decision 2') -Encoding utf8
    Check $false 'unknown decision'
    Set-Content $page ('ADR ' + '0001 decisions 1 and 2') -Encoding utf8
    Check $false 'unknown subsequent decision'
    Set-Content $page 'ADR 0001' -Encoding utf8
    Set-Content (Join-Path $root 'docs/adr/0001-duplicate.md') '# Duplicate' -Encoding utf8
    Check $false 'duplicate ADR number'
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
