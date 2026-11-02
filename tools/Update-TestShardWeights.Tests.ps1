Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The weights tool merges a run into the file, on a throwaway results folder and weights file.
$root = Join-Path ([IO.Path]::GetTempPath()) "motif-shard-weights-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path (Join-Path $root 'results/run') | Out-Null
try {
    function Assert-Equal {
        param($Actual, $Expected, [string] $What)
        if ($Actual -ne $Expected) { throw "$What was '$Actual', expected '$Expected'." }
    }
    function Write-Trx([string] $name, [hashtable] $classSeconds) {
        $definitions = ''
        $results = ''
        $index = 0
        foreach ($class in $classSeconds.Keys) {
            $id = "00000000-0000-0000-0000-{0:D12}" -f $index++
            $definitions += "<UnitTest id=`"$id`"><TestMethod className=`"$class`" name=`"M`" /></UnitTest>"
            $span = [TimeSpan]::FromSeconds($classSeconds[$class]).ToString('c')
            $results += "<UnitTestResult testId=`"$id`" duration=`"$span`" outcome=`"Passed`" />"
        }
        Set-Content -LiteralPath (Join-Path $root "results/run/$name.trx") -Value (
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
    Write-Trx 'one' @{ 'This.Project.Old' = 2.5; 'This.Project.New' = 1.0 }

    # A run of one project keeps the other projects' timings and refreshes its own.
    Set-Content -LiteralPath $target -Value $existing
    & $tool -ResultsDirectory (Join-Path $root 'results') -Target $target | Out-Null
    $merged = Read-Weights $target
    Assert-Equal $merged.Count 4 'merged entries'
    Assert-Equal $merged['Other.Project.Kept'] 7.5 'another project keeps its timing'
    Assert-Equal $merged['Other.Project.Gone'] 3.0 'a class not in the run keeps its timing'
    Assert-Equal $merged['This.Project.Old'] 2.5 'a class in the run takes the new timing'
    Assert-Equal $merged['This.Project.New'] 1.0 'a new class is added'
    $names = @((Get-Content -LiteralPath $target -Raw | ConvertFrom-Json).PSObject.Properties.Name)
    Assert-Equal ($names -join ',') (($names | Sort-Object) -join ',') 'entries stay sorted'

    # A second run changes nothing it did not measure.
    Write-Trx 'one' @{ 'Other.Project.Kept' = 8.0 }
    & $tool -ResultsDirectory (Join-Path $root 'results') -Target $target | Out-Null
    $merged = Read-Weights $target
    Assert-Equal $merged.Count 4 'entries after the second run'
    Assert-Equal $merged['Other.Project.Kept'] 8.0 'the second run refreshes its class'
    Assert-Equal $merged['This.Project.Old'] 2.5 'the first run survives the second'

    # -Replace is the whole-suite rewrite.
    & $tool -ResultsDirectory (Join-Path $root 'results') -Target $target -Replace | Out-Null
    $replaced = Read-Weights $target
    Assert-Equal $replaced.Count 1 'a replaced file holds only the run'
    Assert-Equal $replaced['Other.Project.Kept'] 8.0 'the replaced entry'

    # No file yet: the run alone is written.
    & $tool -ResultsDirectory (Join-Path $root 'results') -Target $other | Out-Null
    Assert-Equal (Read-Weights $other).Count 1 'a missing file starts from the run'

    # Load normalisation still scales what the run measured, and only that.
    Set-Content -LiteralPath $target -Value $existing
    Write-Trx 'one' @{ 'This.Project.Old' = 4.0 }
    & $tool -ResultsDirectory (Join-Path $root 'results') -Target $target -RunLoad ([Environment]::ProcessorCount * 2) | Out-Null
    $scaled = Read-Weights $target
    Assert-Equal $scaled['This.Project.Old'] 2.0 'a run at twice the processors is halved'
    Assert-Equal $scaled['Other.Project.Kept'] 7.5 'normalisation leaves the kept entries alone'
}
finally { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host 'Shard weight merge checks passed.' -ForegroundColor Green
