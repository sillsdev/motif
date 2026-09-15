[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ParserArtifact,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string] $ProductVersion,

    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Windows)) {
    throw 'The portable package is Windows x64 only.'
}

$parser = Get-Item -LiteralPath $ParserArtifact -ErrorAction Stop
if (-not $parser.PSIsContainer -and $parser.Length -eq 0) {
    throw "PanGloss artifact is empty: $($parser.FullName)"
}
if ($parser.PSIsContainer) {
    throw "PanGloss artifact is not a file: $($parser.FullName)"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot ".tmp\release-candidate\$ProductVersion"
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
    if (-not [System.StringComparer]::OrdinalIgnoreCase.Equals(
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
        '--runtime', 'win-x64',
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
    Publish-MotifProject (Join-Path $repoRoot 'src\SIL.Motif.App\SIL.Motif.App.csproj') $appDirectory
    Publish-MotifProject (Join-Path $repoRoot 'src\SIL.Motif.Cli\SIL.Motif.Cli.csproj') $cliDirectory

    $appEntryPoint = Join-Path $appDirectory 'SIL.Motif.App.exe'
    $cliEntryPoint = Join-Path $cliDirectory 'motif.exe'
    foreach ($entryPoint in @($appEntryPoint, $cliEntryPoint)) {
        if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) {
            throw "Published entry point is missing: $entryPoint"
        }
    }

    $sourceParserHash = (Get-FileHash -LiteralPath $parser.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $forbiddenWorkerAssets = @(
        'SIL.Motif.Worker.exe',
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
        Copy-Item -LiteralPath $parser.FullName -Destination (Join-Path $directory 'pangloss.exe')
    }

    $appParserHash = (Get-FileHash -LiteralPath (Join-Path $appDirectory 'pangloss.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
    $cliParserHash = (Get-FileHash -LiteralPath (Join-Path $cliDirectory 'pangloss.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
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
        runtimeIdentifier = 'win-x64'
        distribution = 'portable-development-candidate'
        entryPoints = @(
            [ordered]@{ name = 'app'; path = 'app/SIL.Motif.App.exe' },
            [ordered]@{ name = 'cli'; path = 'cli/motif.exe' }
        )
        dependencies = @(
            [ordered]@{ name = 'PanGloss'; path = 'app/pangloss.exe'; sha256 = $appParserHash },
            [ordered]@{ name = 'PanGloss'; path = 'cli/pangloss.exe'; sha256 = $cliParserHash }
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
