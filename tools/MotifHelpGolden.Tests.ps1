# Tests for MotifHelpGolden.psm1; throws on failure.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$module = Import-Module (Join-Path $PSScriptRoot 'MotifHelpGolden.psm1') -Force -PassThru

function Assert-Equal {
    param($Expected, $Actual, [string] $Message)
    if ($Expected -ne $Actual) { throw "$Message Expected '$Expected', got '$Actual'." }
}

$map = & $module { Get-MotifHelpEntryJson '{"entries":[{"kind":"command","code":"a"},{"kind":"command","code":"A"}]}' }
Assert-Equal 2 $map.Count 'Entries whose codes differ only in case stay distinct.'
Assert-Equal '{"kind":"command","code":"a"}' $map['command:a'] 'An entry maps to its raw JSON.'

$collide = & $module { Get-MotifHelpEntryJson '{"entries":[{"kind":"k","code":"c","Note":1,"note":2}]}' }
Assert-Equal 1 $collide.Count 'Object keys that differ only in case are accepted.'

$threw = $false
try { & $module { Get-MotifHelpEntryJson '{"entries":[{"kind":"k","code":"c"},{"kind":"k","code":"c"}]}' } }
catch { $threw = $_.Exception.Message -like '*twice*' }
if (-not $threw) { throw 'A duplicated kind and code must be rejected.' }

# End to end against a fake shell-script CLI; skipped on Windows, where the CLI is an exe.
if (-not $IsWindows) {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('motif-help-golden-test-' + [Guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Path (Join-Path $root 'tests/SIL.Motif.Tests.Cli/Golden'), (Join-Path $root 'bin/Debug') | Out-Null
        $golden = '{"entries":[{"kind":"command","code":"one","text":"x"},{"kind":"command","code":"two","text":"y"}]}' + "`n"
        $goldenPath = Get-MotifHelpGoldenPath $root
        [IO.File]::WriteAllText($goldenPath, $golden)
        $cli = Join-Path $root 'bin/Debug/motif'
        $answer = Join-Path $root 'answer.json'
        # The fake also records the redirected state variables, proving Help runs away from per-user state.
        [IO.File]::WriteAllText($cli, "#!/bin/sh`necho `"`$MOTIF_WORKER_ROOT`" > '$root/worker-root.txt'`necho `"`$MOTIF_ADVANCED_AI_MODE_PATH`" > '$root/ai-path.txt'`ncat '$answer'`n")
        & chmod 0755 $cli

        [IO.File]::WriteAllText($answer, $golden)
        $same = Compare-MotifHelpGolden -RepoRoot $root -Configuration Debug
        if ($null -ne $same) { throw "A matching golden must return null, got '$same'." }
        $tmp = [IO.Path]::GetTempPath()
        foreach ($name in 'worker-root.txt', 'ai-path.txt') {
            $value = (Get-Content -LiteralPath (Join-Path $root $name) -Raw).Trim()
            if (-not $value.StartsWith($tmp)) { throw "$name points at '$value', outside the temporary folder." }
        }

        [IO.File]::WriteAllText($answer, $golden.Replace('"y"', '"CHANGED"'))
        $diff = @(Compare-MotifHelpGolden -RepoRoot $root -Configuration Debug)
        Assert-Equal 'command:two' ($diff -join ',') 'The changed entry is named.'
    }
    finally { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host 'MotifHelpGolden tests OK' -ForegroundColor Green
