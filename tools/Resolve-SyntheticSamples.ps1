[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'SyntheticSamples.psm1') -Force
Write-Output (Resolve-SyntheticSampleRoot $repositoryRoot)
