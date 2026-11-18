Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot 'package-smoke.ps1'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref] $tokens, [ref] $errors)
if ($errors.Count -ne 0) { throw "Cannot parse ${scriptPath}: $errors" }
$function = $ast.Find({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-CliProductVersion'
}, $true)
. ([scriptblock]::Create($function.Extent.Text))

$pwshPath = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
$silentOutput = & $pwshPath -NoProfile -Command 'exit 0'
if ((Get-CliProductVersion $silentOutput) -cne '') {
    throw 'A silent CLI during update must yield an empty version so polling can continue.'
}
foreach ($text in @('0.2.0-beta003', "0.2.0-beta003 (source SHA $('a' * 40))", ' 0.2.0-beta003 ')) {
    if ((Get-CliProductVersion $text) -cne '0.2.0-beta003') {
        throw "Unexpected CLI product version for '$text'."
    }
}
Write-Host 'Package smoke version polling checks passed.' -ForegroundColor Green
