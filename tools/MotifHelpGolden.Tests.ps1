# Tests for MotifHelpGolden.psm1; throws on failure.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$module = Import-Module (Join-Path $PSScriptRoot 'MotifHelpGolden.psm1') -Force -PassThru

function Assert-Equal {
    param($Expected, $Actual, [string] $Message)
    if ($Expected -ne $Actual) { throw "$Message Expected '$Expected', got '$Actual'." }
}

$split = Split-MotifHelpExport '{"locale":"en","entries":[{"kind":"command","code":"a b","x":{"y":1}},{"kind":"command","code":"A"}]}'
Assert-Equal 3 $split.Count 'The envelope and each entry are separate files.'
Assert-Equal "{`n  `"locale`": `"en`"`n}`n" $split['_envelope.json'] 'The envelope holds every field but entries.'
Assert-Equal "{`"kind`":`"command`",`"code`":`"a b`",`"x`":{`"y`":1}}`n" $split['command/a_b.json'] 'A space in a code becomes an underscore in the file name.'
Assert-Equal 'True' ([string] $split.ContainsKey('command/A.json')) 'Codes that differ only in case keep their own entry.'

$paged = Split-MotifHelpExport "{`"entries`":[`n    {`n      `"kind`": `"command`",`n      `"code`": `"p`",`n      `"helpPage`": `"# P`",`n      `"url`": `"u`"`n    }`n  ]}"
Assert-Equal "{`n  `"kind`": `"command`",`n  `"code`": `"p`",`n  `"url`": `"u`"`n}`n" $paged['command/p.json'] 'An entry''s helpPage is left out of the golden.'

$threw = $false
try { Split-MotifHelpExport '{"entries":[{"kind":"k","code":"c"},{"kind":"k","code":"C"}]}' | Out-Null }
catch { $threw = $_.Exception.Message -like '*twice*' }
if (-not $threw) { throw 'Two entries whose files collide ignoring case must be rejected.' }

$collide = Split-MotifHelpExport '{"entries":[{"kind":"k","code":"c","Note":1,"note":2}]}'
Assert-Equal 2 $collide.Count 'Object keys that differ only in case are accepted.'

# End to end against a fake shell-script CLI; skipped on Windows, where the CLI is an exe.
if (-not $IsWindows) {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('motif-help-golden-test-' + [Guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Path (Join-Path $root 'bin/Debug') | Out-Null
        $golden = '{"entries":[{"kind":"command","code":"one","text":"x"},{"kind":"command","code":"two","text":"y"}]}' + "`n"
        [void] (Write-MotifHelpGolden -RepoRoot $root -Json $golden)
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
        Assert-Equal 'command/two.json' ($diff -join ',') 'The changed entry is named.'
    }
    finally { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host 'MotifHelpGolden tests OK' -ForegroundColor Green
