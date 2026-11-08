[CmdletBinding()]
param(
    [string] $Project = 'SIL.TextEditing.sln',
    [string] $Configuration = 'Debug',
    [string] $Framework,
    [string] $Filter,
    [switch] $NoBuild,
    [string] $ResultsDirectory
)

$ErrorActionPreference = 'Stop'
# Linux tests must not connect to a desktop.
if ($IsLinux) {
    Remove-Item Env:DISPLAY -ErrorAction SilentlyContinue
    Remove-Item Env:WAYLAND_DISPLAY -ErrorAction SilentlyContinue
}

$projectPath = Join-Path $PSScriptRoot $Project
$testArguments = @('test', $projectPath, '--configuration', $Configuration)
if ($Framework) { $testArguments += @('--framework', $Framework) }
if ($Filter) { $testArguments += @('--filter', $Filter) }
if ($NoBuild) { $testArguments += '--no-build' }
if ($ResultsDirectory) { $testArguments += @('--logger', 'trx', '--results-directory', $ResultsDirectory) }
& dotnet @testArguments
exit $LASTEXITCODE
