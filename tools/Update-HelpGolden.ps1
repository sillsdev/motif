<#
  .SYNOPSIS
  Rewrites the Help golden from the built CLI, run in off mode as the golden test runs it.

  .DESCRIPTION
  Build first (./build.ps1), then run this after an intended change to Help content, and commit the diff.

  .PARAMETER Configuration
  The configuration whose CLI is run. Defaults to Debug.
#>
[CmdletBinding()]
param([string] $Configuration = 'Debug')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'MotifHelpGolden.psm1') -Force
$repoRoot = Split-Path -Parent $PSScriptRoot
$text = Invoke-MotifOffModeHelp -RepoRoot $repoRoot -Configuration $Configuration
$path = Get-MotifHelpGoldenPath $repoRoot
[IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
Write-Host "Wrote $path"
