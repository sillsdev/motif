[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$binRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'bin')) + [IO.Path]::DirectorySeparatorChar
$testRoot = [IO.Path]::GetFullPath((Join-Path $binRoot "offline-restore-$([guid]::NewGuid().ToString('N'))"))
if (-not $testRoot.StartsWith($binRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Offline restore test path must stay under bin: $testRoot"
}

$packageRoot = $env:NUGET_PACKAGES
if ([string]::IsNullOrWhiteSpace($packageRoot)) {
    $profileRoot = $env:USERPROFILE
    if ([string]::IsNullOrWhiteSpace($profileRoot)) { $profileRoot = $env:HOME }
    if ([string]::IsNullOrWhiteSpace($profileRoot)) {
        $profileRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    }
    $packageRoot = Join-Path $profileRoot '.nuget/packages'
}
if (-not (Test-Path -LiteralPath (Join-Path $packageRoot 'icu.net/3.0.2'))) {
    throw "Offline restore test requires cached package icu.net 3.0.2 at $packageRoot."
}

New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
try {
    $projectPath = Join-Path $testRoot 'OfflineRestore.csproj'
    $configPath = Join-Path $testRoot 'NuGet.Config'
    New-Item -ItemType Directory -Force -Path (Join-Path $testRoot 'empty-feed') | Out-Null
    [IO.File]::WriteAllText($configPath, @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="offline" value="empty-feed" />
  </packageSources>
</configuration>
'@, [Text.UTF8Encoding]::new($false))

    [IO.File]::WriteAllText($projectPath, @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
</Project>
'@, [Text.UTF8Encoding]::new($false))
    & dotnet restore $projectPath --configfile $configPath --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'The initial offline restore failed.' }

    [IO.File]::WriteAllText($projectPath, @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="icu.net" Version="3.0.2" />
  </ItemGroup>
</Project>
'@, [Text.UTF8Encoding]::new($false))
    & dotnet restore $projectPath --configfile $configPath --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Offline restore failed after the project file changed.' }

    [IO.File]::WriteAllText($projectPath, @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Motif.OfflineRestore.Missing" Version="99.0.0" />
  </ItemGroup>
</Project>
'@, [Text.UTF8Encoding]::new($false))
    Import-Module (Join-Path $repoRoot 'tools/MotifRestore.psm1') -Force
    $missingPackage = Invoke-MotifRestore -Target $projectPath -ConfigFile $configPath
    $expectedMessage = 'Package Motif.OfflineRestore.Missing 99.0.0 is not in the local cache; restore with network once.'
    if ($missingPackage.ExitCode -eq 0) { throw 'The missing package restore unexpectedly succeeded.' }
    if ($missingPackage.Output -notcontains $expectedMessage) {
        $missingPackage.Output | ForEach-Object { Write-Host $_ }
        throw 'The missing package error did not explain how to restore it.'
    }

    Write-Host 'Offline restore regression passed.' -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
