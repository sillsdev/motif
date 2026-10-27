Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'MotifToolEnvironment.psm1') -Force

function Assert-Equal {
    param($Expected, $Actual, [string] $Message)
    if ($Expected -ne $Actual) { throw "$Message Expected '$Expected', got '$Actual'." }
}

$saved = @{ TEMP = $env:TEMP; TMP = $env:TMP; MOTIF_TEST_TEMP = $env:MOTIF_TEST_TEMP;
    MSBUILDTELEMETRYOPTOUT = $env:MSBUILDTELEMETRYOPTOUT }
$root = Join-Path ([IO.Path]::GetTempPath()) ('motif-tool-environment-' + [guid]::NewGuid().ToString('N'))
try {
    $env:MSBUILDTELEMETRYOPTOUT = $null
    Initialize-MotifToolEnvironment -RepoRoot $root
    Assert-Equal '1' $env:MSBUILDTELEMETRYOPTOUT 'MSBuild telemetry is off by default.'
    $env:MSBUILDTELEMETRYOPTOUT = 'caller-choice'
    Initialize-MotifToolEnvironment -RepoRoot $root
    Assert-Equal 'caller-choice' $env:MSBUILDTELEMETRYOPTOUT 'The caller keeps its MSBuild telemetry choice.'
    $env:MOTIF_TEST_TEMP = $null
    $env:TEMP = 'unchanged-temp'
    $env:TMP = 'unchanged-tmp'
    $kept = Initialize-MotifTestTemporaryDirectory -RepoRoot $root -WindowsHost $false
    Assert-Equal $null $kept 'Off Windows the system temporary folder is kept.'
    Assert-Equal 'unchanged-temp' $env:TEMP 'Off Windows TEMP is untouched.'
    Assert-Equal 'unchanged-tmp' $env:TMP 'Off Windows TMP is untouched.'

    # macOS keeps the system folder but names it by its real path, because /var/folders is behind a link.
    if (-not $IsWindows) {
        $real = Join-Path $root 'real-tmp'
        [IO.Directory]::CreateDirectory($real) | Out-Null
        $link = Join-Path $root 'link-tmp'
        [IO.Directory]::CreateSymbolicLink($link, $real) | Out-Null
        $savedTmpDir = $env:TMPDIR
        try {
            $env:TMPDIR = $link
            $kept = Initialize-MotifTestTemporaryDirectory -RepoRoot $root -WindowsHost $false -MacHost $true
            Assert-Equal $null $kept 'macOS keeps the system temporary folder.'
            Assert-Equal ((& /bin/sh -c 'cd "$1" && pwd -P' sh $real)) $env:TMPDIR 'macOS TMPDIR names the real folder.'
            $env:TMPDIR = $link
            Initialize-MotifTestTemporaryDirectory -RepoRoot $root -WindowsHost $false -MacHost $false | Out-Null
            Assert-Equal $link $env:TMPDIR 'Linux TMPDIR is untouched.'
        }
        finally { $env:TMPDIR = $savedTmpDir }
    }

    $expected = [IO.Path]::GetFullPath((Join-Path $root 'bin/t'))
    $moved = Initialize-MotifTestTemporaryDirectory -RepoRoot $root -WindowsHost $true
    Assert-Equal $expected $moved 'On Windows the folder is bin/t in the checkout.'
    Assert-Equal $expected $env:TEMP 'On Windows TEMP names the checkout folder.'
    Assert-Equal $expected $env:TMP 'On Windows TMP names the checkout folder.'
    if (-not (Test-Path -LiteralPath $expected -PathType Container)) { throw 'The folder must exist before tests start.' }

    $chosen = [IO.Path]::GetFullPath((Join-Path $root 'chosen'))
    $env:MOTIF_TEST_TEMP = $chosen
    $explicit = Initialize-MotifTestTemporaryDirectory -RepoRoot $root -WindowsHost $true
    Assert-Equal $chosen $explicit 'MOTIF_TEST_TEMP names the folder when set.'
    Assert-Equal $chosen $env:TEMP 'TEMP follows MOTIF_TEST_TEMP.'
    Write-Host 'Tool environment checks passed.'
}
finally {
    $env:TEMP = $saved.TEMP
    $env:TMP = $saved.TMP
    $env:MOTIF_TEST_TEMP = $saved.MOTIF_TEST_TEMP
    $env:MSBUILDTELEMETRYOPTOUT = $saved.MSBUILDTELEMETRYOPTOUT
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
