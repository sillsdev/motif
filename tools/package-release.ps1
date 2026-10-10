[CmdletBinding()]
param(
    # Defaults to downloading the release pangloss-release.json pins; a supplied file must match it.
    [string] $ParserArtifact,

    [string] $RuntimeIdentifier,

    [ValidatePattern('^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string] $ProductVersion,

    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($ProductVersion)) {
    $ProductVersion = (& (Join-Path $PSScriptRoot 'Get-ProductVersion.ps1')).ProductVersion
}
Import-Module (Join-Path $PSScriptRoot 'PanGlossRelease.psm1') -Force
if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
    $RuntimeIdentifier = Get-CurrentPanGlossRuntimeIdentifier
}
$hostIsWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)
$targetIsWindows = $RuntimeIdentifier.StartsWith('win-', [System.StringComparison]::Ordinal)
if ($hostIsWindows -and -not $targetIsWindows) {
    throw 'Unix package targets must be built on Linux or macOS to preserve executable file modes.'
}
$pinnedParser = Get-PinnedPanGlossArtifact -RepositoryRoot $repoRoot -RuntimeIdentifier $RuntimeIdentifier -ArtifactPath $ParserArtifact
$RuntimeIdentifier = $pinnedParser.RuntimeIdentifier
$parserPin = [pscustomobject]@{ tag = $pinnedParser.Tag; version = $pinnedParser.Version }
$parserAsset = [pscustomobject]@{ url = $pinnedParser.Url; sha256 = $pinnedParser.Sha256 }
$parser = Get-Item -LiteralPath $pinnedParser.Path -ErrorAction Stop
$icuPayloadPath = Join-Path $repoRoot 'tools/icu-payload.json'
if (-not (Test-Path -LiteralPath $icuPayloadPath -PathType Leaf)) {
    throw "SIL ICU payload declaration is missing: $icuPayloadPath"
}
$icuPayload = Get-Content -LiteralPath $icuPayloadPath -Raw | ConvertFrom-Json
$icuRidProperty = $icuPayload.rids.PSObject.Properties[$RuntimeIdentifier]
if ($null -eq $icuRidProperty) {
    throw "no SIL ICU payload for $RuntimeIdentifier in tools/icu-payload.json"
}
$icuNativeOutputDirectory = [string] $icuRidProperty.Value.nativeOutputDirectory
$icuLibrariesProperty = $icuRidProperty.Value.PSObject.Properties['libraries']
if ([string]::IsNullOrWhiteSpace($icuNativeOutputDirectory) -or
    [System.IO.Path]::IsPathRooted($icuNativeOutputDirectory) -or
    @($icuNativeOutputDirectory -split '[\\/]') -contains '..') {
    throw "SIL ICU payload for $RuntimeIdentifier has an invalid nativeOutputDirectory."
}
if ($null -eq $icuLibrariesProperty -or $null -eq $icuLibrariesProperty.Value) {
    throw "SIL ICU payload for $RuntimeIdentifier has no libraries in tools/icu-payload.json"
}
$icuLibraries = @($icuLibrariesProperty.Value)
if ($icuLibraries.Count -eq 0) {
    throw "SIL ICU payload for $RuntimeIdentifier has no libraries in tools/icu-payload.json"
}
foreach ($icuLibrary in $icuLibraries) {
    if ([string]::IsNullOrWhiteSpace([string] $icuLibrary) -or [string] $icuLibrary -in @('.', '..') -or [string] $icuLibrary -match '[/\\]') {
        throw "SIL ICU library names for $RuntimeIdentifier must be file names."
    }
}
$icuBuildOutputRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'bin/Release'))
$icuBuildOutputDirectory = [System.IO.Path]::GetFullPath((Join-Path $icuBuildOutputRoot $icuNativeOutputDirectory))
$parserFileName = if ($targetIsWindows) { 'pangloss.exe' } else { 'pangloss' }
$entryPointSuffix = if ($targetIsWindows) { '.exe' } else { '' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot ".tmp/release-candidate/$ProductVersion/$RuntimeIdentifier"
}
$outputInfo = [System.IO.DirectoryInfo]::new($OutputDirectory)
$output = $outputInfo.FullName
$outputName = $outputInfo.Name
$outputParent = $outputInfo.Parent
if ($null -eq $outputParent) {
    throw "Output directory must have a parent: $output"
}
$outputParentPath = $outputParent.FullName
if (Test-Path -LiteralPath $output) {
    throw "Output directory already exists; choose a new path: $output"
}

$stageName = "$outputName.staging-$([Guid]::NewGuid().ToString('N'))"
$stage = [System.IO.Path]::GetFullPath((Join-Path $outputParentPath $stageName))
$temporaryRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { [System.IO.Path]::GetTempPath() } else { $env:RUNNER_TEMP }
$intermediateParentPath = Join-Path $temporaryRoot 'motif-package-build'
$intermediateRoot = [System.IO.Path]::GetFullPath((Join-Path $intermediateParentPath $stageName))
$previousIntermediateRoot = [Environment]::GetEnvironmentVariable('MOTIF_PACKAGE_INTERMEDIATE_ROOT', 'Process')
$stageCreated = $false

function Assert-NoReparsePointsInPath {
    param(
        [string] $Path
    )

    $current = [System.IO.Path]::GetFullPath($Path)
    while ($null -ne $current -and $current.Length -gt 0) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing a reparse point in package path: $current"
            }
        }

        $parent = [System.IO.Path]::GetDirectoryName($current)
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Assert-SafeStagePath {
    param(
        [string] $StagePath,
        [string] $ExpectedParent,
        [string] $ExpectedName
    )

    $resolvedStage = [System.IO.Path]::GetFullPath($StagePath)
    $resolvedParent = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::GetDirectoryName($resolvedStage))
    $resolvedExpectedParent = [System.IO.Path]::GetFullPath($ExpectedParent)
    $pathComparer = if ($targetIsWindows) { [System.StringComparer]::OrdinalIgnoreCase } else { [System.StringComparer]::Ordinal }
    if (-not $pathComparer.Equals(
            $resolvedParent, $resolvedExpectedParent)) {
        throw "Staging path is outside the output parent: $resolvedStage"
    }
    if (-not [System.StringComparer]::Ordinal.Equals(
            [System.IO.Path]::GetFileName($resolvedStage), $ExpectedName)) {
        throw "Staging path does not have the generated name: $resolvedStage"
    }

    Assert-NoReparsePointsInPath $resolvedExpectedParent
    if (Test-Path -LiteralPath $resolvedStage) {
        $stageItem = Get-Item -LiteralPath $resolvedStage -Force -ErrorAction Stop
        if (($stageItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing a reparse point at the staging path: $resolvedStage"
        }
    }
}

function Publish-MotifProject {
    param(
        [string] $ProjectPath,
        [string] $Destination,
        [string] $BuildOutputRoot
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $arguments = @(
        $ProjectPath,
        '--configuration', 'Release',
            '--runtime', $RuntimeIdentifier,
        '--self-contained', 'true',
        '--output', $Destination,
        "-p:Version=$ProductVersion",
        "-p:InformationalVersion=$ProductVersion",
        '-p:MotifPortablePackage=true',
        '--nologo'
    )
    if (-not [string]::IsNullOrEmpty($BuildOutputRoot)) {
        $arguments += "-p:MotifBinRoot=$BuildOutputRoot"
    }
    & dotnet publish @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $ProjectPath."
    }
}

function Get-RelativePackagePath {
    param(
        [string] $Root,
        [string] $Path
    )

    return [System.IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
}

try {
    Assert-NoReparsePointsInPath $intermediateParentPath
    $env:MOTIF_PACKAGE_INTERMEDIATE_ROOT = $intermediateRoot
    & (Join-Path $repoRoot 'build.ps1') -Configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw 'The release wrapper gate failed; no package was published.'
    }

    $icuRuntimeSource = [Environment]::GetEnvironmentVariable('MOTIF_SIL_ICU_STAGE')
    if (-not $targetIsWindows -and [string]::IsNullOrWhiteSpace($icuRuntimeSource)) {
        throw "MOTIF_SIL_ICU_STAGE is required to package custom SIL ICU for $RuntimeIdentifier."
    }
    if (-not [string]::IsNullOrWhiteSpace($icuRuntimeSource)) {
        $icuRuntimeSource = [System.IO.Path]::GetFullPath($icuRuntimeSource)
        if (-not (Test-Path -LiteralPath $icuRuntimeSource -PathType Container)) {
            throw "SIL ICU runtime staging directory does not exist: $icuRuntimeSource"
        }
        [System.IO.Directory]::CreateDirectory($icuBuildOutputDirectory) | Out-Null
        foreach ($icuLibrary in $icuLibraries) {
            $sourcePath = Join-Path $icuRuntimeSource $icuLibrary
            if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                throw "SIL ICU runtime library is missing for $RuntimeIdentifier`: $sourcePath"
            }
            Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $icuBuildOutputDirectory $icuLibrary) -Force
        }
    }
    [System.IO.Directory]::CreateDirectory($outputParentPath) | Out-Null
    Assert-SafeStagePath $stage $outputParentPath $stageName
    New-Item -ItemType Directory -Path $stage -ErrorAction Stop | Out-Null
    $stageCreated = $true
    Assert-SafeStagePath $stage $outputParentPath $stageName

    $appEntryPointName = "SIL.Motif.App$entryPointSuffix"
    $cliEntryPointName = "motif$entryPointSuffix"
    $workerEntryPointName = "SIL.Motif.Worker$entryPointSuffix"
    $workerPublishDirectory = Join-Path $intermediateRoot 'worker-publish'
    Publish-MotifProject (Join-Path $repoRoot 'src/SIL.Motif.App/SIL.Motif.App.csproj') $stage
    Publish-MotifProject (Join-Path $repoRoot 'src/SIL.Motif.Cli/SIL.Motif.Cli.csproj') $stage
    # App and CLI leave a framework-dependent Worker runtimeconfig in the shared bin that a publish there keeps.
    $workerBuildDirectory = (Join-Path $intermediateRoot 'worker-build') + [System.IO.Path]::DirectorySeparatorChar
    Publish-MotifProject (Join-Path $repoRoot 'src/SIL.Motif.Worker/SIL.Motif.Worker.csproj') $workerPublishDirectory $workerBuildDirectory

    $workerAssets = @(
        $workerEntryPointName,
        'SIL.Motif.Worker.dll',
        'SIL.Motif.Worker.deps.json',
        'SIL.Motif.Worker.runtimeconfig.json'
    )
    foreach ($workerAsset in $workerAssets) {
        $workerAssetSource = Join-Path $workerPublishDirectory $workerAsset
        if (-not (Test-Path -LiteralPath $workerAssetSource -PathType Leaf)) {
            throw "Self-contained Worker publish is missing: $workerAssetSource"
        }
        $workerAssetDestination = Join-Path $stage $workerAsset
        Copy-Item -LiteralPath $workerAssetSource -Destination $workerAssetDestination -Force
    }

    $appEntryPoint = Join-Path $stage $appEntryPointName
    $cliEntryPoint = Join-Path $stage $cliEntryPointName
    $workerEntryPoint = Join-Path $stage $workerEntryPointName
    foreach ($entryPoint in @($appEntryPoint, $cliEntryPoint, $workerEntryPoint)) {
        if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) {
            throw "Published entry point is missing: $entryPoint"
        }
    }
    if (-not $targetIsWindows) {
        $executableMode = [System.IO.UnixFileMode]::UserRead -bor
            [System.IO.UnixFileMode]::UserWrite -bor
            [System.IO.UnixFileMode]::UserExecute -bor
            [System.IO.UnixFileMode]::GroupRead -bor
            [System.IO.UnixFileMode]::GroupExecute -bor
            [System.IO.UnixFileMode]::OtherRead -bor
            [System.IO.UnixFileMode]::OtherExecute
        foreach ($entryPoint in @($appEntryPoint, $cliEntryPoint, $workerEntryPoint)) {
            [System.IO.File]::SetUnixFileMode($entryPoint, $executableMode)
        }
    }
    foreach ($workerAsset in @('SIL.Motif.Worker.dll', 'SIL.Motif.Worker.deps.json', 'SIL.Motif.Worker.runtimeconfig.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $stage $workerAsset) -PathType Leaf)) {
            throw "Published Worker asset is missing: $workerAsset"
        }
    }
    $appHostRuntimeConfigs = @(
        'SIL.Motif.App.runtimeconfig.json',
        'motif.runtimeconfig.json',
        'SIL.Motif.Worker.runtimeconfig.json'
    )
    foreach ($runtimeConfigName in $appHostRuntimeConfigs) {
        $runtimeConfigPath = Join-Path $stage $runtimeConfigName
        $runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
        $runtimeOptionsProperty = $runtimeConfig.PSObject.Properties['runtimeOptions']
        $includedFrameworksProperty = if ($null -eq $runtimeOptionsProperty) {
            $null
        }
        else {
            $runtimeOptionsProperty.Value.PSObject.Properties['includedFrameworks']
        }
        if ($null -eq $includedFrameworksProperty -or @($includedFrameworksProperty.Value).Count -eq 0) {
            throw "Apphost runtime config is not self-contained: $runtimeConfigName must declare runtimeOptions.includedFrameworks."
        }
    }

    $sourceParserHash = (Get-FileHash -LiteralPath $parser.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($sourceParserHash -ne $parserAsset.sha256) {
        throw 'PanGloss changed after it was verified; no package was published.'
    }
    Copy-Item -LiteralPath $parser.FullName -Destination (Join-Path $stage $parserFileName)
    if (-not $targetIsWindows) {
        [System.IO.File]::SetUnixFileMode(
            (Join-Path $stage $parserFileName),
            [System.IO.UnixFileMode]::UserRead -bor
                [System.IO.UnixFileMode]::UserWrite -bor
                [System.IO.UnixFileMode]::UserExecute -bor
                [System.IO.UnixFileMode]::GroupRead -bor
                [System.IO.UnixFileMode]::GroupExecute -bor
                [System.IO.UnixFileMode]::OtherRead -bor
                [System.IO.UnixFileMode]::OtherExecute)
    }

    $stagedParserPath = Join-Path $stage $parserFileName
    & (Join-Path $PSScriptRoot 'Test-PanGlossInterfaces.ps1') `
        -RepositoryRoot $repoRoot -ParserPath $stagedParserPath `
        -PinnedParserPath $stagedParserPath -Configuration Release -RequireParser
    if ($LASTEXITCODE -ne 0) { throw 'The bundled PanGloss interface gate failed; no package was published.' }

    $icuRecords = @()
    foreach ($icuLibrary in $icuLibraries) {
        $sourcePath = Join-Path $icuBuildOutputDirectory $icuLibrary
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "SIL ICU library is missing from the Release build output for $RuntimeIdentifier`: $sourcePath"
        }

        $icuRelativePath = if ($icuNativeOutputDirectory -eq '.') {
            $icuLibrary
        }
        else {
            Join-Path $icuNativeOutputDirectory $icuLibrary
        }
        $destinationPath = [System.IO.Path]::GetFullPath((Join-Path $stage $icuRelativePath))
        $stagePrefix = $stage.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) +
            [System.IO.Path]::DirectorySeparatorChar
        $pathComparison = if ($targetIsWindows) {
            [System.StringComparison]::OrdinalIgnoreCase
        }
        else {
            [System.StringComparison]::Ordinal
        }
        if (-not $destinationPath.StartsWith($stagePrefix, $pathComparison)) {
            throw "SIL ICU destination escapes the package root: $icuRelativePath"
        }
        if (Test-Path -LiteralPath $destinationPath) {
            $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
            $destinationHash = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($sourceHash -ne $destinationHash) {
                throw "SIL ICU destination differs from the Release build output: $icuRelativePath"
            }
        }
        else {
            $destinationDirectory = [System.IO.Path]::GetDirectoryName($destinationPath)
            New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
            Copy-Item -LiteralPath $sourcePath -Destination $destinationPath
        }
        $icuRecords += [ordered]@{
            path = Get-RelativePackagePath $stage $destinationPath
            sha256 = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }

    $stagedParserPath = Join-Path $stage $parserFileName
    $stagedParserHash = (Get-FileHash -LiteralPath $stagedParserPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($stagedParserHash -ne $sourceParserHash) {
        throw 'PanGloss changed while it was being copied; no package was published.'
    }

    $noticeAssets = @('SIL.Motif.App', 'SIL.Motif.Cli', 'SIL.Motif.Worker') | ForEach-Object {
        Join-Path $intermediateRoot "$_/project.assets.json"
    }
    & (Join-Path $PSScriptRoot 'Copy-ThirdPartyNotices.ps1') -StageDirectory $stage -AssetsFiles $noticeAssets

    $payloadFiles = @(Get-ChildItem -LiteralPath $stage -File -Recurse | Sort-Object FullName)
    $fileRecords = @($payloadFiles | ForEach-Object {
        [ordered]@{
            path = Get-RelativePackagePath $stage $_.FullName
            size = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
    $manifest = [ordered]@{
        manifestVersion = 1
        product = 'Motif'
        productVersion = $ProductVersion
        runtimeIdentifier = $RuntimeIdentifier
        distribution = 'velopack-payload'
        entryPoints = @(
            [ordered]@{ name = 'app'; path = $appEntryPointName },
            [ordered]@{ name = 'cli'; path = $cliEntryPointName },
            [ordered]@{ name = 'worker'; path = $workerEntryPointName },
            [ordered]@{ name = 'parser'; path = $parserFileName }
        )
        dependencies = @(
            [ordered]@{ name = 'PanGloss'; version = $parserPin.version; source = $parserAsset.url; path = $parserFileName; sha256 = $stagedParserHash },
            [ordered]@{ name = 'SIL ICU'; files = $icuRecords }
        )
        files = $fileRecords
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText(
        (Join-Path $stage 'release-manifest.json'),
        $manifestJson + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))

    & (Join-Path $PSScriptRoot 'test-package-stage.ps1') -StageDirectory $stage -RuntimeIdentifier $RuntimeIdentifier

    Assert-SafeStagePath $stage $outputParentPath $stageName
    if (Test-Path -LiteralPath $output) {
        throw "Output directory appeared during packaging; no package was published: $output"
    }
    [System.IO.Directory]::Move($stage, $output)
    Write-Host "Portable development candidate written to $output" -ForegroundColor Green
}
finally {
    if ($null -eq $previousIntermediateRoot) {
        Remove-Item Env:MOTIF_PACKAGE_INTERMEDIATE_ROOT -ErrorAction SilentlyContinue
    }
    else {
        $env:MOTIF_PACKAGE_INTERMEDIATE_ROOT = $previousIntermediateRoot
    }
    if ($stageCreated -and (Test-Path -LiteralPath $stage)) {
        Assert-SafeStagePath $stage $outputParentPath $stageName
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
    if (Test-Path -LiteralPath $intermediateRoot) {
        Assert-SafeStagePath $intermediateRoot $intermediateParentPath $stageName
        Remove-Item -LiteralPath $intermediateRoot -Recurse -Force
    }
}
