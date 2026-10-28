Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The selection rules, on a throwaway repository and a hand-written coverage map.
Import-Module (Join-Path $PSScriptRoot 'MotifChangedTests.psm1') -Force

$root = Join-Path ([IO.Path]::GetTempPath()) "motif-changed-tests-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $root | Out-Null
try {
    function Invoke-Git { & git -C $root @args | Out-Null; if ($LASTEXITCODE -ne 0) { throw "git $args failed" } }
    function Write-RepoFile([string] $relative, [string] $text) {
        $path = Join-Path $root $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
        Set-Content -LiteralPath $path -Value $text
    }
    Invoke-Git init -q -b main
    Invoke-Git config user.email t@example.org
    Invoke-Git config user.name t
    Write-RepoFile 'src/SIL.Motif.A/One.cs' 'class One {}'
    Write-RepoFile 'src/SIL.Motif.A/Two.cs' 'class Two {}'
    Write-RepoFile 'src/SIL.Motif.B/Three.cs' 'class Three {}'
    Write-RepoFile 'tests/SIL.Motif.Tests.A/OneTests.cs' "namespace T;`nclass OneTests {}"
    Write-RepoFile 'tests/SIL.Motif.Tests.Support/Helper.cs' 'class Helper {}'
    Write-RepoFile 'docs/notes.md' 'notes'
    Write-RepoFile 'samples/data.json' '{}'
    Write-RepoFile 'tests/SIL.Motif.Tests.A/ReadsData.cs' 'class ReadsData { string p = "samples/data.json"; }'
    Invoke-Git add -A
    Invoke-Git commit -q -m base
    $commit = (& git -C $root rev-parse HEAD).Trim()

    $files = @('src/SIL.Motif.A/One.cs', 'src/SIL.Motif.A/Two.cs', 'src/SIL.Motif.B/Three.cs', 'tests/SIL.Motif.Tests.A/OneTests.cs', 'tests/SIL.Motif.Tests.A/ReadsData.cs')
    $map = [ordered]@{
        version = 1; commit = $commit; files = $files
        classes = [ordered]@{
            'T.OneTests' = [ordered]@{ p = 'SIL.Motif.Tests.A'; s = 1.5; t = 2; k = 0; x = 0; f = @(0, 3) }
            'T.TwoTests' = [ordered]@{ p = 'SIL.Motif.Tests.A'; s = 2.0; t = 2; k = 0; x = 0; f = @(1, 2) }
            'T.ReadsData' = [ordered]@{ p = 'SIL.Motif.Tests.A'; s = 1.0; t = 1; k = 0; x = 0; f = @(4) }
            'T.Smoke' = [ordered]@{ p = 'SIL.Motif.Tests.A'; s = 0.1; t = 1; k = 0; x = 0; f = @(2) }
        }
    }
    $outside = Join-Path ([IO.Path]::GetTempPath()) "motif-changed-tests-data-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $outside | Out-Null
$mapPath = Join-Path $outside 'map.json'
    $map | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $mapPath
    $smoke = Join-Path $outside 'smoke.txt'
    Set-Content -LiteralPath $smoke -Value @('# smoke', 'T.Smoke')

    function Select-Now([hashtable] $extra = @{}) {
        Select-MotifChangedTests -RepoRoot $root -MapPath $mapPath -Base $commit -SmokePath $smoke @extra
    }
    function Assert-True($condition, [string] $message) { if (-not $condition) { throw "FAILED: $message" } }
    function Assert-Classes($selection, [string[]] $expected, [string] $message) {
        $actual = @($selection.Classes.Keys | Sort-Object)
        Assert-True (-not $selection.Full) "${message}: unexpected full run: $($selection.FullReasons -join '; ')"
        Assert-True (($actual -join ',') -eq (($expected | Sort-Object) -join ',')) "${message}: got $($actual -join ',') expected $($expected -join ',')"
    }

    # A changed source file selects the classes that cover it, plus the smoke set.
    Add-Content -LiteralPath (Join-Path $root 'src/SIL.Motif.A/One.cs') -Value '// edit'
    Assert-Classes (Select-Now) @('T.OneTests', 'T.Smoke') 'one source file'
    Invoke-Git checkout -q -- .

    # A file two classes cover selects both.
    Add-Content -LiteralPath (Join-Path $root 'src/SIL.Motif.A/Two.cs') -Value '// edit'
    Assert-Classes (Select-Now) @('T.TwoTests', 'T.Smoke') 'shared file'
    Invoke-Git checkout -q -- .

    # A new test file selects the classes it declares, even though the map has never seen them.
    Write-RepoFile 'tests/SIL.Motif.Tests.A/NewTests.cs' "namespace T;`npublic sealed class NewTests { }"
    $new = Select-Now
    Assert-True (-not $new.Full) "new test file: $($new.FullReasons -join '; ')"
    Assert-True (@($new.DeclaredEntries.Entry) -contains 'T.NewTests') 'a new test class is declared'
    $plan = Get-MotifChangedPlan -Selection $new
    Assert-True ($plan.SelectionEntries -contains 'T.NewTests+*') 'the plan keeps the nested-class prefix'
    Assert-True ($plan.Projects.ContainsKey('SIL.Motif.Tests.A')) 'the new class puts its project in the plan'
    Remove-Item (Join-Path $root 'tests/SIL.Motif.Tests.A/NewTests.cs')

    # A new source file runs every class that covers anything in its project.
    Write-RepoFile 'src/SIL.Motif.A/Fresh.cs' 'class Fresh {}'
    Assert-Classes (Select-Now) @('T.OneTests', 'T.TwoTests', 'T.Smoke') 'new file in a covered project'
    Remove-Item (Join-Path $root 'src/SIL.Motif.A/Fresh.cs')

    # A new source file in a project nothing covers, and shared test code that is not in the map, run everything.
    Write-RepoFile 'src/SIL.Motif.C/Lost.cs' 'class Lost {}'
    Assert-True (Select-Now).Full 'a file in an uncovered project must fall back'
    Remove-Item -Recurse (Join-Path $root 'src/SIL.Motif.C')
    Write-RepoFile 'tests/SIL.Motif.Tests.Support/NewHelper.cs' 'class NewHelper {}'
    Assert-True (Select-Now).Full 'a new shared test helper must fall back'
    Remove-Item (Join-Path $root 'tests/SIL.Motif.Tests.Support/NewHelper.cs')

    # Build files, project files and tooling scripts run everything, and say why.
    foreach ($path in @('Directory.Build.props', 'src/SIL.Motif.A/SIL.Motif.A.csproj', 'build.ps1', 'tools/Some.ps1')) {
        Write-RepoFile $path 'x'
        $full = Select-Now
        Assert-True ($full.Full -and ($full.FullReasons -join ' ') -match [regex]::Escape($path)) "$path must fall back with its name in the reason"
        Remove-Item (Join-Path $root $path)
    }

    # Non-code inputs: a named file selects its readers, documentation is ignored, a stranger runs everything.
    Add-Content -LiteralPath (Join-Path $root 'samples/data.json') -Value ' '
    Assert-Classes (Select-Now) @('T.ReadsData', 'T.Smoke') 'a data file a test names'
    Invoke-Git checkout -q -- .
    Add-Content -LiteralPath (Join-Path $root 'docs/notes.md') -Value 'more'
    Assert-Classes (Select-Now) @('T.Smoke') 'documentation'
    Invoke-Git checkout -q -- .
    Write-RepoFile 'samples/other.json' '{}'
    Assert-Classes (Select-Now) @('T.ReadsData', 'T.Smoke') 'a new file in a directory a test names'
    Remove-Item (Join-Path $root 'samples/other.json')
    Write-RepoFile 'strangers/other.json' '{}'
    Assert-True (Select-Now).Full 'an unnamed data file must fall back'
    Remove-Item -Recurse (Join-Path $root 'strangers')
    # A data file named only by code no mapped class executes cannot be shown inert, so it runs everything.
    Write-RepoFile 'extras/unmapped.json' '{}'
    Write-RepoFile 'src/SIL.Motif.A/Reader.cs' 'class Reader { string p = "unmapped.json"; }'
    Invoke-Git add -A
    Invoke-Git commit -q -m reader
    $readerBase = (& git -C $root rev-parse HEAD).Trim()
    Add-Content -LiteralPath (Join-Path $root 'extras/unmapped.json') -Value ' '
    $unmapped = Select-MotifChangedTests -RepoRoot $root -MapPath $mapPath -Base $readerBase -SmokePath $smoke -IgnoreMapAge
    Assert-True ($unmapped.Full -and ($unmapped.FullReasons -join ' ') -match 'unmapped\.json') 'a data file read only by unmapped code must fall back'
    Invoke-Git reset -q --hard $commit

    # A missing, foreign or old map runs everything.
    Assert-True (Select-MotifChangedTests -RepoRoot $root -MapPath (Join-Path $outside 'none.json') -Base $commit).Full 'no map'
    Add-Content -LiteralPath (Join-Path $root 'src/SIL.Motif.A/One.cs') -Value '// edit'
    Assert-True (Select-Now @{ MaxMapAge = -1 }).Full 'a map older than the limit'
    Invoke-Git checkout -q -- .
    $stale = $map | ConvertTo-Json -Depth 5 | ConvertFrom-Json -AsHashtable
    $stale.commit = '0' * 40
    $stalePath = Join-Path $outside 'stale.json'
    $stale | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $stalePath
    Assert-True (Select-MotifChangedTests -RepoRoot $root -MapPath $stalePath -Base $commit).Full 'a map from another history'

    # A class whose measured run failed or was all skipped has partial coverage: any code change selects it.
    $partial = $map | ConvertTo-Json -Depth 5 | ConvertFrom-Json -AsHashtable
    $partial.classes['T.Failed'] = @{ p = 'SIL.Motif.Tests.A'; s = 5.0; t = 1; k = 0; x = 0; u = 1; f = @(4) }
    $partial.classes['T.Skipped'] = @{ p = 'SIL.Motif.Tests.A'; s = 0.0; t = 2; k = 2; x = 0; f = @(4) }
    $partialPath = Join-Path $outside 'partial.json'
    $partial | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $partialPath
    Add-Content -LiteralPath (Join-Path $root 'src/SIL.Motif.A/One.cs') -Value '// edit'
    Assert-Classes (Select-MotifChangedTests -RepoRoot $root -MapPath $partialPath -Base $commit -SmokePath $smoke) @('T.OneTests', 'T.Smoke', 'T.Failed', 'T.Skipped') 'partial classes on a code change'
    Invoke-Git checkout -q -- .
    Add-Content -LiteralPath (Join-Path $root 'docs/notes.md') -Value 'more'
    Assert-Classes (Select-MotifChangedTests -RepoRoot $root -MapPath $partialPath -Base $commit -SmokePath $smoke) @('T.Smoke') 'partial classes on a docs change'
    Invoke-Git checkout -q -- .

    # A file that changed after the map was built has unreliable coverage and widens to its project.
    Add-Content -LiteralPath (Join-Path $root 'src/SIL.Motif.A/One.cs') -Value '// landed after the map'
    Invoke-Git commit -q -am landed
    $baseNow = (& git -C $root rev-parse HEAD).Trim()
    Add-Content -LiteralPath (Join-Path $root 'src/SIL.Motif.A/One.cs') -Value '// my edit'
    $drift = Select-MotifChangedTests -RepoRoot $root -MapPath $mapPath -Base $baseNow -SmokePath $smoke
    Assert-Classes $drift @('T.OneTests', 'T.TwoTests', 'T.Smoke') 'a drifted file'
    Invoke-Git checkout -q -- .

    Write-Host 'Changed-files selection checks passed.' -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path variable:outside) { Remove-Item -LiteralPath $outside -Recurse -Force -ErrorAction SilentlyContinue }
}
