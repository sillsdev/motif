[CmdletBinding()]
param([Parameter(Mandatory)][string] $RunDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'tools/ABHarness.psm1') -Force
$setRoot = Get-ABSetRoot $repoRoot
& python3 (Join-Path $PSScriptRoot 'tools/CalibrateMeaning.py') --run-directory $RunDirectory --set-root $setRoot
if ($LASTEXITCODE) { throw 'Calibration controls failed or judging was incomplete. Inspect the calibration artifacts.' }
