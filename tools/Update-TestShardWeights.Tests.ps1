Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The weights tool merges a run into the file, on a throwaway results folder and weights file.
$root = Join-Path ([IO.Path]::GetTempPath()) "motif-shard-weights-$([guid]::NewGuid().ToString('N'))"
# Two test projects; a run folder is named for the project and shard that wrote it, as test.ps1 names them.
foreach ($name in 'T.A', 'T.B') {
    New-Item -ItemType Directory -Path (Join-Path $root "tests/$name"), (Join-Path $root 'src'), (Join-Path $root "results/$name.shard0") -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $root "tests/$name/$name.csproj") -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>'
}
try {
    function Assert-Equal {
        param($Actual, $Expected, [string] $What)
        if ($Actual -ne $Expected) { throw "$What was '$Actual', expected '$Expected'." }
    }
    function Write-Trx([string] $project, [hashtable] $classSeconds) {
        $definitions = ''
        $results = ''
        $index = 0
        foreach ($class in $classSeconds.Keys) {
            $id = "00000000-0000-0000-0000-{0:D12}" -f $index++
            $definitions += "<UnitTest id=`"$id`"><TestMethod className=`"$class`" name=`"M`" /></UnitTest>"
            $span = [TimeSpan]::FromSeconds($classSeconds[$class]).ToString('c')
            $results += "<UnitTestResult testId=`"$id`" duration=`"$span`" outcome=`"Passed`" />"
        }
        Set-Content -LiteralPath (Join-Path $root "results/$project.shard0/one.trx") -Value (
            "<?xml version=`"1.0`"?><TestRun xmlns=`"http://microsoft.com/schemas/VisualStudio/TeamTest/2010`">" +
            "<Results>$results</Results><TestDefinitions>$definitions</TestDefinitions></TestRun>")
    }
    function Read-Weights([string] $path) {
        $weights = @{}
        foreach ($property in (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).PSObject.Properties) { $weights[$property.Name] = [double] $property.Value }
        return $weights
    }
    $tool = Join-Path $PSScriptRoot 'Update-TestShardWeights.ps1'
    $target = Join-Path $root 'weights.json'
    $other = Join-Path $root 'other-weights.json'
    $existing = @'
{
  "Other.Project.Kept": 7.5,
  "Other.Project.Gone": 3.0,
  "This.Project.Old": 9.0
}
'@
    Write-Trx 'T.A' @{ 'This.Project.Old' = 2.5; 'This.Project.New' = 1.0 }

    # A run of one project keeps the other projects' timings and refreshes its own.
    Set-Content -LiteralPath $target -Value $existing
    & $tool -RepoRoot $root -ResultsDirectory (Join-Path $root 'results') -Target $target | Out-Null
    $merged = Read-Weights $target
    Assert-Equal $merged.Count 4 'merged entries'
    Assert-Equal $merged['Other.Project.Kept'] 7.5 'another project keeps its timing'
    Assert-Equal $merged['Other.Project.Gone'] 3.0 'a class not in the run keeps its timing'
    Assert-Equal $merged['This.Project.Old'] 2.5 'a class in the run takes the new timing'
    Assert-Equal $merged['This.Project.New'] 1.0 'a new class is added'
    $names = @((Get-Content -LiteralPath $target -Raw | ConvertFrom-Json).PSObject.Properties.Name)
    Assert-Equal ($names -join ',') (($names | Sort-Object) -join ',') 'entries stay sorted'

    # A second run changes nothing it did not measure.
    Write-Trx 'T.A' @{ 'Other.Project.Kept' = 8.0 }
    & $tool -RepoRoot $root -ResultsDirectory (Join-Path $root 'results') -Target $target | Out-Null
    $merged = Read-Weights $target
    Assert-Equal $merged.Count 4 'entries after the second run'
    Assert-Equal $merged['Other.Project.Kept'] 8.0 'the second run refreshes its class'
    Assert-Equal $merged['This.Project.Old'] 2.5 'the first run survives the second'

    # With T.B missing from the results, -Replace is refused and the file stays as it was.
    $before = Get-Content -LiteralPath $target -Raw
    $refused = $false
    try { & $tool -RepoRoot $root -ResultsDirectory (Join-Path $root 'results') -Target $target -Replace | Out-Null }
    catch { $refused = $_.Exception.Message -match 'T\.B' }
    Assert-Equal $refused $true '-Replace refuses a run that lacks a test project, naming it'
    Assert-Equal (Get-Content -LiteralPath $target -Raw) $before 'a refused -Replace leaves the file alone'
    Write-Trx 'T.B' @{ 'B.Class' = 1.0 }
    & $tool -RepoRoot $root -ResultsDirectory (Join-Path $root 'results') -Target $target -Replace | Out-Null
    $replaced = Read-Weights $target
    Assert-Equal $replaced.Count 2 'a replaced file holds only the run'
    Assert-Equal $replaced['Other.Project.Kept'] 8.0 'the replaced entry'

    # No file yet: the run alone is written.
    & $tool -RepoRoot $root -ResultsDirectory (Join-Path $root 'results') -Target $other | Out-Null
    Assert-Equal (Read-Weights $other).Count 2 'a missing file starts from the run'

    # Load normalisation still scales what the run measured, and only that.
    Set-Content -LiteralPath $target -Value $existing
    Write-Trx 'T.A' @{ 'This.Project.Old' = 4.0 }
    & $tool -RepoRoot $root -ResultsDirectory (Join-Path $root 'results') -Target $target -RunLoad ([Environment]::ProcessorCount * 2) | Out-Null
    $scaled = Read-Weights $target
    Assert-Equal $scaled['This.Project.Old'] 2.0 'a run at twice the processors is halved'
    Assert-Equal $scaled['Other.Project.Kept'] 7.5 'normalisation leaves the kept entries alone'
}
finally { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host 'Shard weight merge checks passed.' -ForegroundColor Green
