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

Export-ModuleMember -Function Get-MotifTestArtifactRequirements, Get-MotifTestThreadCount
