<#
.SYNOPSIS
  Builds and starts one of the sample hosts.
.EXAMPLE
  .\run.ps1 desktop10                       # plain Avalonia desktop app, net10.0, Avalonia 12
  .\run.ps1 desktop48                       # plain Avalonia desktop app, net48, Avalonia 11.3.17
  .\run.ps1 winforms48                      # WinForms form on net48 hosting the playground between two TextBoxes
  .\run.ps1 desktop10 --scenario "Inline picture (ORC)"
  .\run.ps1 winforms48 --minimized          # start minimized without taking focus (smoke run)
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('desktop10', 'desktop48', 'winforms48')]
    [string]$Target,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$HostArgs
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
switch ($Target) {
    'desktop10'  { $project = 'samples\Playground.Desktop\Playground.Desktop.csproj'; $framework = 'net10.0'; $exe = 'bin\Playground.Desktop\Debug\net10.0\Playground.Desktop.exe' }
    'desktop48'  { $project = 'samples\Playground.Desktop\Playground.Desktop.csproj'; $framework = 'net48';    $exe = 'bin\Playground.Desktop\Debug\net48\Playground.Desktop.exe' }
    'winforms48' { $project = 'samples\Host48\Host48.csproj';                          $framework = 'net48';    $exe = 'bin\Host48\Debug\net48\Host48.exe' }
}

dotnet build (Join-Path $root $project) -f $framework --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Build failed for $Target" }

$path = Join-Path $root $exe
$minimized = $HostArgs -contains '--minimized'
$startArgs = @{ FilePath = $path; PassThru = $true }
if ($HostArgs) { $startArgs.ArgumentList = ($HostArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' ' }
if ($minimized) { $startArgs.WindowStyle = 'Minimized' }
$process = Start-Process @startArgs
Write-Host "Started $Target as process $($process.Id)"
