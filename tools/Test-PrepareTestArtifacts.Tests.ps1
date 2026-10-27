[CmdletBinding()]
param(
    [string] $Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.DirectoryInfo]::new($PSScriptRoot).Parent.FullName
$configurationRoot = Join-Path $repositoryRoot "bin/$Configuration"
$prepareScript = Join-Path $PSScriptRoot 'Prepare-TestArtifacts.ps1'
$sampleProjectsAssembly = Join-Path $configurationRoot 'SIL.Motif.SampleProjects.dll'
if (-not (Test-Path -LiteralPath $sampleProjectsAssembly -PathType Leaf)) {
    throw "The SampleProjects assembly is missing; build the $Configuration configuration first: $sampleProjectsAssembly"
}
if (-not (Test-Path -LiteralPath $prepareScript -PathType Leaf)) {
    throw "Test artifact preparation script is missing: $prepareScript"
}

$regressionBase = Join-Path $repositoryRoot 'bin/.cache/test-artifact-preparation'
$regressionRoot = Join-Path $regressionBase ([Guid]::NewGuid().ToString('N'))
$artifactOutputRoot = Join-Path $regressionRoot 'artifacts'
$walkthroughRoot = Join-Path $artifactOutputRoot 'prepared/walkthrough-fixtures/explained-word-card'
$defaultPreparedRoot = Join-Path $configurationRoot 'tests/prepared'
$environmentNames = @(
    'DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_ROOT_X86', 'DOTNET_ROOT_ARM64',
    'DOTNET_CLI_HOME', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE',
    'XDG_DATA_HOME', 'XDG_CACHE_HOME', 'TMPDIR', 'TEMP', 'TMP',
    'MOTIF_TEST_SLDR_OFFLINE', 'MOTIF_TEST_SLDR_CACHE_PATH',
    'MOTIF_WRITING_SYSTEM_REPOSITORY_PATH', 'MOTIF_PACKAGE_INTERMEDIATE_ROOT'
)
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

function Get-DirectoryFingerprint {
    param([string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return 'missing' }
    $root = [System.IO.Path]::GetFullPath($Path)
    $entries = @(
        Get-ChildItem -LiteralPath $root -File -Recurse -Force |
            Sort-Object { [System.IO.Path]::GetRelativePath($root, $_.FullName) } |
            ForEach-Object {
                $relative = [System.IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/')
                $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                "$relative`t$($_.Length)`t$hash"
            }
    )
    $text = "present`n$($entries -join "`n")"
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
    $digest = [System.Security.Cryptography.SHA256]::HashData($bytes)
    return [Convert]::ToHexString($digest).ToLowerInvariant()
}

$regressionItem = $null
try {
    [System.IO.Directory]::CreateDirectory($regressionBase) | Out-Null
    [System.IO.Directory]::CreateDirectory($regressionRoot) | Out-Null
    $regressionItem = Get-Item -LiteralPath $regressionRoot -Force
    if (($regressionItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Regression output root may not be a reparse point: $regressionRoot"
    }

    $env:DOTNET_ROOT = $configurationRoot
    $env:DOTNET_ROOT_X64 = $configurationRoot
    foreach ($name in @('DOTNET_ROOT_X86', 'DOTNET_ROOT_ARM64')) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
    $env:DOTNET_CLI_HOME = Join-Path $regressionRoot 'dotnet-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:XDG_DATA_HOME = Join-Path $regressionRoot 'xdg-data'
    $env:XDG_CACHE_HOME = Join-Path $regressionRoot 'xdg-cache'
    $env:TMPDIR = Join-Path $regressionRoot 'temp'
    $env:TEMP = Join-Path $regressionRoot 'temp'
    $env:TMP = Join-Path $regressionRoot 'temp'
    foreach ($directory in @($env:DOTNET_CLI_HOME, $env:XDG_DATA_HOME, $env:XDG_CACHE_HOME, $env:TMPDIR)) {
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    }

    $defaultBefore = Get-DirectoryFingerprint $defaultPreparedRoot
    & $prepareScript -Configuration $Configuration -ExplainedWordCardFixture `
        -ArtifactOutputRoot $artifactOutputRoot

    $fixturePath = Join-Path $walkthroughRoot 'fixture.json'
    if (-not (Test-Path -LiteralPath $fixturePath -PathType Leaf)) {
        throw "Artifact preparation did not write fixture metadata beneath its private output root: $fixturePath"
    }
    $fixture = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string] $fixture.projectPath) -or
        [System.IO.Path]::IsPathRooted([string] $fixture.projectPath)) {
        throw 'The generated fixture metadata must contain a relative project path.'
    }
    $projectPath = [System.IO.Path]::GetFullPath((Join-Path $walkthroughRoot ([string] $fixture.projectPath)))
    $walkthroughPrefix = [System.IO.Path]::GetFullPath($walkthroughRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    $comparison = if ($IsWindows) { [System.StringComparison]::OrdinalIgnoreCase } else {
        [System.StringComparison]::Ordinal
    }
    if (-not $projectPath.StartsWith($walkthroughPrefix, $comparison) -or
        [System.IO.Path]::GetExtension($projectPath) -cne '.fwdata') {
        throw "The generated project path is not a private .fwdata artifact: $projectPath"
    }
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "The generated FieldWorks project is missing: $projectPath"
    }
    $projectInfo = Get-Item -LiteralPath $projectPath
    if ($projectInfo.Length -le 0) { throw 'The generated FieldWorks project is empty.' }
    $textGuid = [Guid]::Empty
    if (-not [Guid]::TryParse([string] $fixture.textId, [ref] $textGuid) -or $textGuid -eq [Guid]::Empty) {
        throw 'The generated fixture metadata must contain a valid nonempty text GUID.'
    }

    $defaultAfter = Get-DirectoryFingerprint $defaultPreparedRoot
    if ($defaultBefore -cne $defaultAfter) {
        throw 'The isolated artifact preparation regression changed the shared prepared-artifact directory.'
    }
    $projectHash = (Get-FileHash -LiteralPath $projectPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "Artifact preparation generated a private project ($($projectInfo.Length) bytes, SHA-256 $projectHash) and valid fixture metadata." -ForegroundColor Green
}
finally {
    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
    if (Test-Path -LiteralPath $regressionRoot) {
        $item = Get-Item -LiteralPath $regressionRoot -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to remove a regression reparse point: $regressionRoot"
        }
        Remove-Item -LiteralPath $regressionRoot -Recurse -Force
    }
}
