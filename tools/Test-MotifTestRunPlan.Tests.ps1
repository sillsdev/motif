Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'MotifTestRunPlan.psm1') -Force

function Assert-Requirements {
    param(
        [string[]] $Projects,
        [string] $Filter,
        [bool] $ExpectedPortable,
        [bool] $ExpectedExplainedWordCard,
        [switch] $System
    )

    $requirements = Get-MotifTestArtifactRequirements -All:$(-not $System) -System:$System `
        -SelectedProjects $Projects -Filter $Filter
    if ($requirements.PortableWorkerPackage -ne $ExpectedPortable -or
        $requirements.ExplainedWordCardFixture -ne $ExpectedExplainedWordCard) {
        throw "Unexpected artifact requirements for projects '$($Projects -join ',')' and filter '$Filter'."
    }
}

Assert-Requirements @('SIL.Motif.Tests.Cli') '' $true $false
Assert-Requirements @('SIL.Motif.Tests.App') '' $false $true
Assert-Requirements @('SIL.Motif.Tests.App', 'SIL.Motif.Tests.Cli') '' $true $true
Assert-Requirements @('SIL.Motif.Tests.Cli') 'FullyQualifiedName~HelpCatalogTests' $false $false
Assert-Requirements @('SIL.Motif.Tests.App') 'FullyQualifiedName~KeyboardInteractionContractTests' $false $false
Assert-Requirements @('SIL.Motif.Tests.Cli') 'FullyQualifiedName~PortableWorkerPackageTests' $true $false
Assert-Requirements @('SIL.Motif.Tests.App') 'FullyQualifiedName~ExplainedWordCard' $false $true
Assert-Requirements @('SIL.Motif.Tests.App', 'SIL.Motif.Tests.Cli') 'Trait=System' $true $true
Assert-Requirements @('SIL.Motif.Tests.App') 'FullyQualifiedName~UnrelatedTest' $false $false -System

Write-Host 'Test artifact selection checks passed.' -ForegroundColor Green
