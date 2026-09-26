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
$isWindows = $RuntimeIdentifier.StartsWith('win-', [System.StringComparison]::Ordinal)
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
    $OutputDirectory = Join-Path $repoRoot ".tmp/release-candidate/$ProductVersion"
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

    [System.IO.Directory]::CreateDirectory($outputParentPath) | Out-Null
    Assert-SafeStagePath $stage $outputParentPath $stageName
    New-Item -ItemType Directory -Path $stage -ErrorAction Stop | Out-Null
    $stageCreated = $true
    Assert-SafeStagePath $stage $outputParentPath $stageName

    $appDirectory = Join-Path $stage 'app'
    $cliDirectory = Join-Path $stage 'cli'
    Publish-MotifProject (Join-Path $repoRoot 'src/SIL.Motif.App/SIL.Motif.App.csproj') $appDirectory
    Publish-MotifProject (Join-Path $repoRoot 'src/SIL.Motif.Cli/SIL.Motif.Cli.csproj') $cliDirectory

    $appEntryPointName = "SIL.Motif.App$entryPointSuffix"
    $cliEntryPointName = "motif$entryPointSuffix"
    $workerEntryPointName = "SIL.Motif.Worker$entryPointSuffix"
    $appEntryPoint = Join-Path $appDirectory $appEntryPointName
    $cliEntryPoint = Join-Path $cliDirectory $cliEntryPointName
    foreach ($entryPoint in @($appEntryPoint, $cliEntryPoint)) {
        if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) {
            throw "Published entry point is missing: $entryPoint"
        }
    }

    $sourceParserHash = (Get-FileHash -LiteralPath $parser.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($sourceParserHash -ne $parserAsset.sha256) {
        throw 'PanGloss changed after it was verified; no package was published.'
    }
    $forbiddenWorkerAssets = @(
        $workerEntryPointName,
        'SIL.Motif.Worker.deps.json',
        'SIL.Motif.Worker.runtimeconfig.json'
    )
    foreach ($directory in @($appDirectory, $cliDirectory)) {
        foreach ($asset in $forbiddenWorkerAssets) {
            if (Get-ChildItem -LiteralPath $directory -File -Recurse |
                    Where-Object { $_.Name -eq $asset }) {
                throw "Portable package contains forbidden Worker asset: $asset"
            }
        }
        if (-not (Test-Path -LiteralPath (Join-Path $directory 'SIL.Motif.Worker.dll') -PathType Leaf)) {
            throw "Portable package is missing required Worker library: $directory"
        }
        Copy-Item -LiteralPath $parser.FullName -Destination (Join-Path $directory $parserFileName)
    }

    $appParserPath = Join-Path $appDirectory $parserFileName
    $cliParserPath = Join-Path $cliDirectory $parserFileName
    $appParserHash = (Get-FileHash -LiteralPath $appParserPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $cliParserHash = (Get-FileHash -LiteralPath $cliParserPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($appParserHash -ne $sourceParserHash -or $cliParserHash -ne $sourceParserHash) {
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
        distribution = 'portable-development-candidate'
        entryPoints = @(
            [ordered]@{ name = 'app'; path = "app/$appEntryPointName" },
            [ordered]@{ name = 'cli'; path = "cli/$cliEntryPointName" }
        )
        dependencies = @(
            [ordered]@{ name = 'PanGloss'; version = $parserPin.version; source = $parserAsset.url; path = "app/$parserFileName"; sha256 = $appParserHash },
            [ordered]@{ name = 'PanGloss'; version = $parserPin.version; source = $parserAsset.url; path = "cli/$parserFileName"; sha256 = $cliParserHash }
        )
        files = $fileRecords
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText(
        (Join-Path $stage 'release-manifest.json'),
        $manifestJson + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))

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
