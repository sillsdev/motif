[CmdletBinding()]
param(
    [string] $Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$fakeSource = Join-Path $repoRoot "bin/$Configuration/tests/fake-pangloss"
$fakeName = if ($IsWindows) { 'pangloss.exe' } else { 'pangloss' }
$fakePath = Join-Path $fakeSource $fakeName
$pinnedPath = Join-Path $repoRoot "bin/$Configuration/$(if ($IsWindows) { 'pangloss.exe' } else { 'pangloss' })"
if (-not (Test-Path -LiteralPath $fakePath -PathType Leaf)) {
    throw "Fake PanGloss is missing; run ./build.ps1 first: $fakePath"
}
if (-not (Test-Path -LiteralPath $pinnedPath -PathType Leaf)) {
    throw "Staged pinned PanGloss is missing; run ./build.ps1 first: $pinnedPath"
}

$testRoot = Join-Path $repoRoot "bin/.cache/pangloss-interface-test-$([guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($testRoot) | Out-Null
$behaviorPath = Join-Path $testRoot 'fake-behavior.json'
$previousBehavior = $env:FAKE_PANGLOSS_BEHAVIOUR_PATH
$gate = Join-Path $PSScriptRoot 'Test-PanGlossInterfaces.ps1'

function New-FakeParser {
    param([string] $Name)
    $target = Join-Path $testRoot $Name
    Copy-Item -LiteralPath $fakeSource -Destination $target -Recurse
    return (Join-Path $target $fakeName)
}

function Invoke-Gate {
    param([string] $Candidate, [string] $Probe)
    & $gate -RepositoryRoot $repoRoot -ParserPath $Candidate -PinnedParserPath $pinnedPath `
        -ProbeRoot $Probe -Configuration $Configuration -RequireParser
}

try {
    $env:FAKE_PANGLOSS_BEHAVIOUR_PATH = $behaviorPath
    $wrongParser = New-FakeParser 'wrong-schema'
    $wrongProbe = Join-Path $testRoot 'wrong-probe'
    $wrongBehavior = @{
        subcommands = @{
            'grammar-health' = @{
                grammarHealthReportJson = '{"schema_version":99,"locale":"en","fieldworks_project":{},"summary":[],"diagnostics":[]}'
            }
        }
    } | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText($behaviorPath, $wrongBehavior, [System.Text.UTF8Encoding]::new($false))
    $failure = $null
    try { Invoke-Gate $wrongParser $wrongProbe } catch { $failure = $_.Exception.Message }
    if ($null -eq $failure -or
        -not $failure.Contains('grammar-health schema_version expected', [StringComparison]::Ordinal) -or
        -not $failure.Contains('actual ''99''', [StringComparison]::Ordinal)) {
        throw "The gate did not name the wrong grammar-health version; got '$failure'."
    }

    $validParser = New-FakeParser 'valid-schema'
    $validProbe = Join-Path $testRoot 'valid-probe'
    $validBehavior = @{
        phases = @{
            parse = @(
                @{},
                @{ mode = 'fail'; exitCode = 1; standardError = '{"schema_version":1,"status":"compile_error","issues":[]}' }
            )
        }
    } | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText($behaviorPath, $validBehavior, [System.Text.UTF8Encoding]::new($false))
    Invoke-Gate $validParser $validProbe
    Write-Host 'PanGloss interface gate rejects a wrong grammar-health schema and accepts the pinned interface.' -ForegroundColor Green
}
finally {
    $env:FAKE_PANGLOSS_BEHAVIOUR_PATH = $previousBehavior
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
