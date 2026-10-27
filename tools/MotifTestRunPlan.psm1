Set-StrictMode -Version Latest

function Get-MotifTestArtifactRequirements {
    [CmdletBinding()]
    param(
        [switch] $All,
        [switch] $System,
        [Parameter(Mandatory)][string[]] $SelectedProjects,
        [string] $Filter
    )

    $fullyQualifiedNameOnly = $false
    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $remaining = [regex]::Replace($Filter, '(?i)FullyQualifiedName\s*(?:~|=)\s*[^&|()]*', '')
        $remaining = $remaining -replace '[&|()\s]', ''
        $fullyQualifiedNameOnly = $remaining.Length -eq 0 -and
            $Filter -match '(?i)FullyQualifiedName\s*(?:~|=)'
    }

    $prepareAllSelectedTests = [string]::IsNullOrWhiteSpace($Filter) -or -not $fullyQualifiedNameOnly
    $portableSelected = $SelectedProjects -contains 'SIL.Motif.Tests.Cli' -and
        ($prepareAllSelectedTests -or $Filter -match 'PortableWorkerPackageTests')
    $explainedWordCardSelected = $SelectedProjects -contains 'SIL.Motif.Tests.App' -and
        ($prepareAllSelectedTests -or $Filter -match 'ExplainedWordCard')

    [pscustomobject]@{
        PortableWorkerPackage = ($All -or $System) -and $portableSelected
        ExplainedWordCardFixture = ($All -or $System) -and $explainedWordCardSelected
    }
}

<#
  .SYNOPSIS
  How many test processes run at once.

  .DESCRIPTION
  MOTIF_TEST_PROCESSES, a positive whole number, wins. Otherwise a developer machine, which shares its processors
  with other worktrees, gets a third of them, rounded down, never fewer than two and never more than eight. CI owns
  its runner and App shards are mostly one UI thread each, so it gets one fewer than the processors (at least two,
  at most eight) and no more than one per 2 GiB of memory. The gate is bound by total process time, and each App
  shard process has its own UI dispatcher, so more concurrent processes is what makes App tests run side by side.
#>
function Get-MotifTestProcessConcurrency {
    [CmdletBinding()]
    param(
        [int] $ProcessorCount = [Environment]::ProcessorCount,
        [long] $MemoryBytes = [GC]::GetGCMemoryInfo().TotalAvailableMemoryBytes
    )

    $processes = 0
    if ([int]::TryParse([string] $env:MOTIF_TEST_PROCESSES, [ref] $processes) -and $processes -gt 0) { return $processes }
    if ($env:CI -match '^(?i:true|1|yes)$') {
        $byMemory = [int][Math]::Floor($MemoryBytes / 2GB)
        return [Math]::Min(8, [Math]::Max(2, [Math]::Min($ProcessorCount - 1, $byMemory)))
    }
    return [Math]::Min(8, [Math]::Max(2, [int][Math]::Floor($ProcessorCount / 3)))
}

<#
  .SYNOPSIS
  The xUnit MaxParallelThreads for one test process; 0 means leave it to xUnit.

  .DESCRIPTION
  xUnit defaults to one thread per processor in every host, so three hosts at once on 20 processors would
  ask for 60. MOTIF_TEST_THREADS, a positive whole number, wins; otherwise CI gets 0, meaning xUnit's own
  default, and each developer-machine host gets its share, the
  processors divided by how many hosts run at once, and never fewer than two.
#>
function Get-MotifTestThreadCount {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int] $ProjectConcurrency,
        [int] $ProcessorCount = [Environment]::ProcessorCount
    )

    $threads = 0
    if ([int]::TryParse([string] $env:MOTIF_TEST_THREADS, [ref] $threads) -and $threads -gt 0) { return $threads }
    if ($env:CI -match '^(?i:true|1|yes)$') { return 0 }
    return [Math]::Max(2, [int][Math]::Floor($ProcessorCount / [Math]::Max(1, $ProjectConcurrency)))
}

Export-ModuleMember -Function Get-MotifTestArtifactRequirements, Get-MotifTestProcessConcurrency, Get-MotifTestThreadCount
