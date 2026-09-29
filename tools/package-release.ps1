[CmdletBinding()]
param(
    # Defaults to downloading the release pangloss-release.json pins; a supplied file must match it.
    [string] $ParserArtifact,

    [string] $RuntimeIdentifier,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string] $ProductVersion,

    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    if ([OperatingSystem]::IsWindows() -and $architecture -eq [System.Runtime.InteropServices.Architecture]::X64) {
        $RuntimeIdentifier = 'win-x64'
    }
    elseif ([OperatingSystem]::IsLinux() -and $architecture -eq [System.Runtime.InteropServices.Architecture]::X64) {
        $RuntimeIdentifier = 'linux-x64'
    }
    elseif ([OperatingSystem]::IsMacOS() -and $architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
        $RuntimeIdentifier = 'osx-arm64'
    }
    elseif ([OperatingSystem]::IsMacOS() -and $architecture -eq [System.Runtime.InteropServices.Architecture]::X64) {
        $RuntimeIdentifier = 'osx-x64'
    }
    else {
        throw "Cannot select a supported RID for $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription) $architecture."
    }
}

$parserPin = Get-Content -LiteralPath (Join-Path $repoRoot 'pangloss-release.json') -Raw | ConvertFrom-Json
$supportedRids = @('win-x64', 'linux-x64', 'osx-arm64', 'osx-x64')
if ($RuntimeIdentifier -notin $supportedRids) {
    throw "Unsupported runtime identifier '$RuntimeIdentifier'; expected one of: $($supportedRids -join ', ')."
}
$parserAssetProperty = $parserPin.assets.PSObject.Properties[$RuntimeIdentifier]
if ($null -eq $parserAssetProperty) {
    throw "no PanGloss build for $RuntimeIdentifier"
}
$parserAsset = $parserAssetProperty.Value
if ($parserAsset.sha256 -notmatch '^[0-9a-f]{64}$') {
    throw 'pangloss-release.json does not carry a lowercase SHA-256.'
}
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
$isWindows = $RuntimeIdentifier.StartsWith('win-', [System.StringComparison]::Ordinal)
$icuBuildOutputRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'bin/Release'))
$icuBuildOutputDirectory = [System.IO.Path]::GetFullPath((Join-Path $icuBuildOutputRoot $icuNativeOutputDirectory))
$parserFileName = if ($isWindows) { 'pangloss.exe' } else { 'pangloss' }
$entryPointSuffix = if ($isWindows) { '.exe' } else { '' }
if ([string]::IsNullOrWhiteSpace($ParserArtifact)) {
    $downloadDirectory = Join-Path $repoRoot ".tmp/pangloss/$($parserPin.tag)/$RuntimeIdentifier"
    $downloadFileName = [System.IO.Path]::GetFileName([Uri] $parserAsset.url)
    $ParserArtifact = Join-Path $downloadDirectory $downloadFileName
    if (-not (Test-Path -LiteralPath $ParserArtifact -PathType Leaf)) {
        New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null
        Invoke-WebRequest -Uri $parserAsset.url -OutFile $ParserArtifact
    }
}

$parser = Get-Item -LiteralPath $ParserArtifact -ErrorAction Stop
if (-not $parser.PSIsContainer -and $parser.Length -eq 0) {
    throw "PanGloss artifact is empty: $($parser.FullName)"
}
if ($parser.PSIsContainer) {
    throw "PanGloss artifact is not a file: $($parser.FullName)"
}

$pinnedParserHash = (Get-FileHash -LiteralPath $parser.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
if ($pinnedParserHash -ne $parserAsset.sha256) {
    throw "PanGloss artifact is not the pinned $($parserPin.tag) for $RuntimeIdentifier (sha256 $pinnedParserHash): $($parser.FullName)"
}

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
    $pathComparer = if ($isWindows) { [System.StringComparer]::OrdinalIgnoreCase } else { [System.StringComparer]::Ordinal }
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
        [string] $Destination
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
    & (Join-Path $repoRoot 'build.ps1') -Configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw 'The release wrapper gate failed; no package was published.'
    }

    $icuRuntimeSource = [Environment]::GetEnvironmentVariable('MOTIF_SIL_ICU_STAGE')
    if (-not $isWindows -and [string]::IsNullOrWhiteSpace($icuRuntimeSource)) {
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

    Publish-MotifProject (Join-Path $repoRoot 'src/SIL.Motif.App/SIL.Motif.App.csproj') $stage
    Publish-MotifProject (Join-Path $repoRoot 'src/SIL.Motif.Cli/SIL.Motif.Cli.csproj') $stage
    Publish-MotifProject (Join-Path $repoRoot 'src/SIL.Motif.Worker/SIL.Motif.Worker.csproj') $stage

    $appEntryPointName = "SIL.Motif.App$entryPointSuffix"
    $cliEntryPointName = "motif$entryPointSuffix"
    $workerEntryPointName = "SIL.Motif.Worker$entryPointSuffix"
    $appEntryPoint = Join-Path $stage $appEntryPointName
    $cliEntryPoint = Join-Path $stage $cliEntryPointName
    $workerEntryPoint = Join-Path $stage $workerEntryPointName
    foreach ($entryPoint in @($appEntryPoint, $cliEntryPoint, $workerEntryPoint)) {
        if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) {
            throw "Published entry point is missing: $entryPoint"
        }
    }
    if (-not $isWindows) {
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

    $sourceParserHash = (Get-FileHash -LiteralPath $parser.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($sourceParserHash -ne $parserAsset.sha256) {
        throw 'PanGloss changed after it was verified; no package was published.'
    }
    Copy-Item -LiteralPath $parser.FullName -Destination (Join-Path $stage $parserFileName)
    if (-not $isWindows) {
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
        $pathComparison = if ($isWindows) {
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
    if ($stageCreated -and (Test-Path -LiteralPath $stage)) {
        Assert-SafeStagePath $stage $outputParentPath $stageName
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
