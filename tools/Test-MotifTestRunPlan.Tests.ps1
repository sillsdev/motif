Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'MotifTestRunPlan.psm1') -Force

function Assert-Requirements {
    param(
        [string[]] $Projects,
        [string] $Filter,
        [bool] $ExpectedPortable,
        [bool] $ExpectedExplainedWordCard,
        [switch] $All,
        [switch] $System
    )

    $requirements = Get-MotifTestArtifactRequirements -All:$All -System:$System `
        -SelectedProjects $Projects -Filter $Filter
    if ($requirements.PortableWorkerPackage -ne $ExpectedPortable -or
        $requirements.ExplainedWordCardFixture -ne $ExpectedExplainedWordCard) {
        throw "Unexpected artifact requirements for projects '$($Projects -join ',')' and filter '$Filter'."
    }
}

Assert-Requirements @('SIL.Motif.Tests.Cli') '' $false $false
Assert-Requirements @('SIL.Motif.Tests.App') '' $false $false
Assert-Requirements @('SIL.Motif.Tests.App', 'SIL.Motif.Tests.Cli') '' $false $false
Assert-Requirements @('SIL.Motif.Tests.Cli') 'FullyQualifiedName~HelpCatalogTests' $false $false
Assert-Requirements @('SIL.Motif.Tests.App') 'FullyQualifiedName~KeyboardInteractionContractTests' $false $false
Assert-Requirements @('SIL.Motif.Tests.Cli') 'FullyQualifiedName~PortableWorkerPackageTests' $false $false
Assert-Requirements @('SIL.Motif.Tests.Cli') '' $true $false -All
Assert-Requirements @('SIL.Motif.Tests.App') '' $false $true -All
Assert-Requirements @('SIL.Motif.Tests.App', 'SIL.Motif.Tests.Cli') '' $true $true -All
Assert-Requirements @('SIL.Motif.Tests.App') 'FullyQualifiedName~ExplainedWordCard' $false $false
Assert-Requirements @('SIL.Motif.Tests.App', 'SIL.Motif.Tests.Cli') 'Trait=System' $true $true -System
Assert-Requirements @('SIL.Motif.Tests.App') 'FullyQualifiedName~UnrelatedTest' $false $false -System

Assert-Requirements @('SIL.Motif.Tests.Cli') '' $true $false -System
Assert-Requirements @('SIL.Motif.Tests.Cli') 'FullyQualifiedName~PortableWorkerPackageTests' $true $false -All
Assert-Requirements @('SIL.Motif.Tests.Cli') 'FullyQualifiedName~PortableWorkerPackageTests' $true $false -System
Assert-Requirements @('SIL.Motif.Tests.Cli') 'FullyQualifiedName~HelpCatalogTests' $false $false -All
Assert-Requirements @('SIL.Motif.Tests.Cli') 'FullyQualifiedName~HelpCatalogTests' $false $false -System
Assert-Requirements @('SIL.Motif.Tests.Cli') 'Name~PortablePackage' $true $false -All
Assert-Requirements @('SIL.Motif.Tests.Cli') 'Name~PortablePackage' $false $false

Write-Host 'Test artifact selection checks passed.' -ForegroundColor Green
