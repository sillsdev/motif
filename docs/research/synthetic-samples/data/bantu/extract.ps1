[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $RawRoot,
    [Parameter(Mandatory = $true)][string] $SelectionManifest,
    [string] $OutputRoot
)

& (Join-Path $PSScriptRoot '../Extract-SelectedRows.ps1') @PSBoundParameters
