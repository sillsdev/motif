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

Export-ModuleMember -Function Get-MotifTestArtifactRequirements
