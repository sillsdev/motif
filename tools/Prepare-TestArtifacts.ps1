[CmdletBinding()]
param(
    [string] $Configuration = 'Debug',
    [switch] $PortableWorkerPackage,
    [switch] $ExplainedWordCardFixture
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$artifactSelectionWasExplicit = $PSBoundParameters.ContainsKey('PortableWorkerPackage') -or
    $PSBoundParameters.ContainsKey('ExplainedWordCardFixture')
$preparePortableWorkerPackage = -not $artifactSelectionWasExplicit -or $PortableWorkerPackage
$prepareExplainedWordCardFixture = -not $artifactSelectionWasExplicit -or $ExplainedWordCardFixture

$repositoryRoot = [System.IO.DirectoryInfo]::new($PSScriptRoot).Parent.FullName
$configurationRoot = Join-Path $repositoryRoot "bin/$Configuration"
$testOutputRoot = Join-Path $configurationRoot 'tests'
$preparedRoot = Join-Path $testOutputRoot 'prepared'
$stageRoot = Join-Path $testOutputRoot ('.prepared-' + [Guid]::NewGuid().ToString('N'))
$workRoot = Join-Path $stageRoot 'work'
$packageRoot = Join-Path $stageRoot 'portable-worker-package'
$walkthroughRoot = Join-Path $stageRoot 'walkthrough-fixtures/explained-word-card'
$previousIntermediateRoot = [Environment]::GetEnvironmentVariable('MOTIF_PACKAGE_INTERMEDIATE_ROOT', 'Process')
$previousSldrOffline = [Environment]::GetEnvironmentVariable('MOTIF_TEST_SLDR_OFFLINE', 'Process')
$previousSldrCachePath = [Environment]::GetEnvironmentVariable('MOTIF_TEST_SLDR_CACHE_PATH', 'Process')
$previousWritingSystemRepositoryPath = [Environment]::GetEnvironmentVariable(
    'MOTIF_WRITING_SYSTEM_REPOSITORY_PATH', 'Process')
$targetIsWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)
$targetIsLinux = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Linux)
$targetIsMacOS = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::OSX)
$architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
$runtimeIdentifier = if ($targetIsWindows -and $architecture -eq 'x64') { 'win-x64' }
elseif ($targetIsLinux -and $architecture -eq 'x64') { 'linux-x64' }
elseif ($targetIsMacOS -and $architecture -eq 'arm64') { 'osx-arm64' }
elseif ($targetIsMacOS -and $architecture -eq 'x64') { 'osx-x64' }
else { throw 'No portable package RID is defined for this platform.' }
$entryPointSuffix = if ($targetIsWindows) { '.exe' } else { '' }
$workerAssets = @(
    "SIL.Motif.Worker$entryPointSuffix",
    'SIL.Motif.Worker.dll',
    'SIL.Motif.Worker.deps.json',
    'SIL.Motif.Worker.runtimeconfig.json'
)
$offlineRestoreConfig = Join-Path $workRoot 'offline-nuget.config'

function Get-WorkerAssetHashes {
    param([string[]] $Assets)

    $hashes = [ordered]@{}
    foreach ($asset in $Assets) {
        $path = Join-Path $configurationRoot $asset
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "The shared build is missing Worker asset $asset."
        }
        $hashes[$asset] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return $hashes
}

function Assert-SharedWorkerAssetsUnchanged {
    param(
        [System.Collections.IDictionary] $Before,
        [string] $PublishName
    )

    $after = Get-WorkerAssetHashes $workerAssets
    foreach ($asset in $Before.Keys) {
        if ($Before[$asset] -cne $after[$asset]) {
            throw "$PublishName publish changed the shared Worker asset $asset."
        }
    }
}

function Publish-Project {
    param(
        [string] $ProjectPath,
        [string] $Destination
    )

    [System.IO.Directory]::CreateDirectory($Destination) | Out-Null
    $motifBinRoot = [System.IO.Path]::GetFullPath((Join-Path $workRoot 'isolated-bin')) +
        [System.IO.Path]::DirectorySeparatorChar
    $publishArguments = @(
        'publish', $ProjectPath,
        '--configuration', $Configuration,
        '--runtime', $runtimeIdentifier,
        '--self-contained', 'true',
        '--output', $Destination,
        '-p:MotifPortablePackage=true',
        "-p:MotifBinRoot=$motifBinRoot",
        "-p:RestoreConfigFile=$offlineRestoreConfig",
        '--nologo'
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $ProjectPath."
    }
}

function Assert-FrontEndPublish {
    param([string] $Name)

    if (-not (Test-Path -LiteralPath (Join-Path $packageRoot 'SIL.Motif.Worker.Runtime.dll') -PathType Leaf)) {
        throw "$Name publish did not include the reusable Worker runtime library."
    }
    foreach ($asset in $workerAssets) {
        if (Test-Path -LiteralPath (Join-Path $packageRoot $asset)) {
            throw "$Name publish unexpectedly included excluded Worker asset $asset."
        }
    }
}

function Assert-SelfContained {
    param([string] $RuntimeConfigName)

    $path = Join-Path $packageRoot $RuntimeConfigName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Portable publish is missing runtime configuration $RuntimeConfigName."
    }
    $runtimeConfig = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if (@($runtimeConfig.runtimeOptions.includedFrameworks).Count -eq 0) {
        throw "Portable runtime configuration has no included frameworks: $RuntimeConfigName."
    }
}

function Copy-IcuPayload {
    param(
        [string] $Package,
        [string] $Rid
    )

    $manifestPath = Join-Path $repositoryRoot 'tools/icu-payload.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $ridProperty = $manifest.rids.PSObject.Properties[$Rid]
    if ($null -eq $ridProperty) { throw "The ICU manifest has no payload for $Rid." }
    $nativeDirectory = [string] $ridProperty.Value.nativeOutputDirectory
    $libraries = @($ridProperty.Value.libraries | ForEach-Object { [string] $_ })
    if ([string]::IsNullOrWhiteSpace($nativeDirectory) -or
        [System.IO.Path]::IsPathRooted($nativeDirectory) -or
        @($nativeDirectory -split '[\\/]') -contains '..' -or
        $libraries.Count -eq 0) {
        throw "The ICU manifest payload is invalid for $Rid."
    }
    foreach ($library in $libraries) {
        if ([string]::IsNullOrWhiteSpace($library) -or $library -in @('.', '..') -or $library -match '[/\\]') {
            throw "The ICU library names must be file names for $Rid."
        }
    }

    if (-not $targetIsWindows) {
        $sourceDirectories = [System.Collections.Generic.List[string]]::new()
        if (-not [string]::IsNullOrWhiteSpace($env:MOTIF_SIL_ICU_STAGE)) {
            $sourceDirectories.Add([System.IO.Path]::GetFullPath($env:MOTIF_SIL_ICU_STAGE))
        }
        $sourceDirectories.Add((Join-Path $configurationRoot $nativeDirectory))
        $sourceDirectories.Add((Join-Path $testOutputRoot $nativeDirectory))
        $source = $null
        foreach ($candidate in $sourceDirectories) {
            $complete = $true
            foreach ($library in $libraries) {
                if (-not (Test-Path -LiteralPath (Join-Path $candidate $library) -PathType Leaf)) {
                    $complete = $false
                    break
                }
            }
            if ($complete) {
                $source = $candidate
                break
            }
        }
        if ($null -eq $source) {
            throw "No staged SIL ICU payload contains every manifest library: $($sourceDirectories -join ', ')"
        }
        $destination = Join-Path $Package $nativeDirectory
        [System.IO.Directory]::CreateDirectory($destination) | Out-Null
        foreach ($library in $libraries) {
            Copy-Item -LiteralPath (Join-Path $source $library) -Destination (Join-Path $destination $library) -Force
        }
    }

    $payloadDirectory = Join-Path $Package $nativeDirectory
    foreach ($library in $libraries) {
        if (-not (Test-Path -LiteralPath (Join-Path $payloadDirectory $library) -PathType Leaf)) {
            throw "The portable package is missing manifest SIL ICU library $library."
        }
    }
    return [ordered]@{ nativeOutputDirectory = $nativeDirectory; libraries = $libraries }
}

function Assert-ArtifactPath {
    param(
        [string] $Parent,
        [string] $Path
    )

    $parentPath = [System.IO.Path]::GetFullPath($Parent).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $targetPath = [System.IO.Path]::GetFullPath($Path)
    $prefix = $parentPath + [System.IO.Path]::DirectorySeparatorChar
    $comparison = if ($targetIsWindows) {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else {
        [System.StringComparison]::Ordinal
    }
    if (-not $targetPath.StartsWith($prefix, $comparison)) {
        throw "Refusing to modify an artifact path outside $parentPath`: $targetPath"
    }
}

function Remove-ArtifactDirectory {
    param([string] $Path)

    Assert-ArtifactPath $testOutputRoot $Path
    if (Test-Path -LiteralPath $Path) {
        $item = Get-Item -LiteralPath $Path -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to remove a reparse point at $Path."
        }
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Write-JsonFile {
    param(
        [string] $Path,
        [object] $Value
    )

    $json = ConvertTo-Json -InputObject $Value -Depth 100
    [System.IO.File]::WriteAllText($Path, $json, [System.Text.UTF8Encoding]::new($false))
}

Push-Location $repositoryRoot
try {
    [System.IO.Directory]::CreateDirectory($testOutputRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($workRoot) | Out-Null
    if ($preparePortableWorkerPackage) { [System.IO.Directory]::CreateDirectory($packageRoot) | Out-Null }
    if ($prepareExplainedWordCardFixture) { [System.IO.Directory]::CreateDirectory($walkthroughRoot) | Out-Null }
    $env:MOTIF_PACKAGE_INTERMEDIATE_ROOT = Join-Path $workRoot 'intermediate'
    $env:MOTIF_TEST_SLDR_OFFLINE = '1'
    $env:MOTIF_TEST_SLDR_CACHE_PATH = Join-Path $workRoot 'sldr-cache'
    $env:MOTIF_WRITING_SYSTEM_REPOSITORY_PATH = Join-Path $workRoot 'writing-systems'
    $localPackageSource = ''
    if (-not [string]::IsNullOrWhiteSpace($env:LOCAL_NUGET_REPO) -and
        (Test-Path -LiteralPath $env:LOCAL_NUGET_REPO -PathType Container)) {
        $escapedSource = [System.Security.SecurityElement]::Escape($env:LOCAL_NUGET_REPO)
        $localPackageSource = "<add key=`"local-override`" value=`"$escapedSource`" />"
    }
    $nugetConfig = "<?xml version=`"1.0`" encoding=`"utf-8`"?><configuration><packageSources><clear />$localPackageSource</packageSources></configuration>"
    [System.IO.File]::WriteAllText($offlineRestoreConfig, $nugetConfig, [System.Text.UTF8Encoding]::new($false))

    if ($preparePortableWorkerPackage) {
        $sharedWorkerAssetsBefore = Get-WorkerAssetHashes $workerAssets
        $frontEndStages = [System.Collections.Generic.List[object]]::new()
        foreach ($frontEnd in @(
        [ordered]@{ name = 'App'; project = 'src/SIL.Motif.App/SIL.Motif.App.csproj' },
        [ordered]@{ name = 'CLI'; project = 'src/SIL.Motif.Cli/SIL.Motif.Cli.csproj' }
        )) {
            Publish-Project (Join-Path $repositoryRoot $frontEnd.project) $packageRoot
            Assert-SharedWorkerAssetsUnchanged $sharedWorkerAssetsBefore $frontEnd.name
            Assert-FrontEndPublish $frontEnd.name
            $frontEndStages.Add([ordered]@{
                name = $frontEnd.name
                workerRuntimeLibraryIncluded = $true
                workerHostAssetsExcluded = $true
                sharedWorkerAssetsUnchanged = $true
            })
        }

        $workerPublishRoot = Join-Path $workRoot 'worker-publish'
        Publish-Project (Join-Path $repositoryRoot 'src/SIL.Motif.Worker/SIL.Motif.Worker.csproj') $workerPublishRoot
        Assert-SharedWorkerAssetsUnchanged $sharedWorkerAssetsBefore 'Worker'
        if (-not (Test-Path -LiteralPath (Join-Path $workerPublishRoot 'SIL.Motif.Worker.Runtime.dll') -PathType Leaf)) {
            throw 'Worker publish did not include its runtime library dependency.'
        }
        foreach ($asset in $workerAssets) {
            $source = Join-Path $workerPublishRoot $asset
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
                throw "Worker publish is missing $asset."
            }
            $destination = Join-Path $packageRoot $asset
            Copy-Item -LiteralPath $source -Destination $destination -Force
            if ($asset -eq "SIL.Motif.Worker$entryPointSuffix" -and -not $targetIsWindows) {
                $executableMode = [System.IO.UnixFileMode]::UserRead -bor
                    [System.IO.UnixFileMode]::UserWrite -bor
                    [System.IO.UnixFileMode]::UserExecute -bor
                    [System.IO.UnixFileMode]::GroupRead -bor
                    [System.IO.UnixFileMode]::GroupExecute -bor
                    [System.IO.UnixFileMode]::OtherRead -bor
                    [System.IO.UnixFileMode]::OtherExecute
                [System.IO.File]::SetUnixFileMode($destination, $executableMode)
            }
        }

        $appHost = Join-Path $packageRoot "SIL.Motif.App$entryPointSuffix"
        $cliHost = Join-Path $packageRoot "motif$entryPointSuffix"
        $workerHost = Join-Path $packageRoot "SIL.Motif.Worker$entryPointSuffix"
        foreach ($hostPath in @($appHost, $cliHost, $workerHost)) {
            if (-not (Test-Path -LiteralPath $hostPath -PathType Leaf)) {
                throw "The portable package is missing apphost $([System.IO.Path]::GetFileName($hostPath))."
            }
        }
        if ([System.IO.Path]::GetFullPath([System.IO.Path]::GetDirectoryName($cliHost)) -cne
            [System.IO.Path]::GetFullPath([System.IO.Path]::GetDirectoryName($workerHost))) {
            throw 'The CLI and Worker apphosts must be siblings in the portable package.'
        }
        Assert-SelfContained "SIL.Motif.App.runtimeconfig.json"
        Assert-SelfContained 'motif.runtimeconfig.json'
        Assert-SelfContained 'SIL.Motif.Worker.runtimeconfig.json'

        $icuPayload = Copy-IcuPayload $packageRoot $runtimeIdentifier
        $sharedWorkerAssetsAfter = Get-WorkerAssetHashes $workerAssets
        foreach ($asset in $sharedWorkerAssetsBefore.Keys) {
            if ($sharedWorkerAssetsBefore[$asset] -cne $sharedWorkerAssetsAfter[$asset]) {
                throw "Portable publishing changed the shared Worker asset $asset."
            }
        }
        Write-JsonFile (Join-Path $stageRoot 'portable-worker-package-validation.json') ([ordered]@{
            runtimeIdentifier = $runtimeIdentifier
            frontEndPublishes = $frontEndStages.ToArray()
            workerPublish = [ordered]@{
                workerRuntimeLibraryIncluded = $true
                workerHostAssetsIncluded = $true
                sharedWorkerAssetsUnchanged = $true
            }
            sharedWorkerAssetsBefore = $sharedWorkerAssetsBefore
            sharedWorkerAssetsAfter = $sharedWorkerAssetsAfter
            icuPayload = $icuPayload
        })
    }

    if ($prepareExplainedWordCardFixture) {
        $sampleSpecPath = Join-Path $workRoot 'explained-word-card.sample.json'
        $sampleSpec = Get-Content -LiteralPath (Join-Path $repositoryRoot 'samples/synthetic-turkic/sample.json') -Raw |
            ConvertFrom-Json -AsHashtable
        $stems = [System.Collections.Generic.List[object]]::new()
        foreach ($stem in $sampleSpec['stems']) { $stems.Add($stem) }
        $stems.Add([ordered]@{ id = 'explained-ev'; form = 'ev'; partOfSpeech = 'noun'; gloss = 'house' })
        $sampleSpec['stems'] = $stems.ToArray()
        $sampleSpec['texts'] = @([ordered]@{
            id = 'explained-word-card'
            title = 'Round 3 word examples'
            sentences = @('geldi', 'evler', 'kediye', 'adamlarında', 'günler', 'okullarında')
        })
        Write-JsonFile $sampleSpecPath $sampleSpec

        $sampleBuilderName = if ($targetIsWindows) { 'SIL.Motif.SampleProjects.exe' } else { 'SIL.Motif.SampleProjects' }
        $sampleBuilder = Join-Path $configurationRoot $sampleBuilderName
        if (-not (Test-Path -LiteralPath $sampleBuilder -PathType Leaf)) {
            throw "The SampleProjects apphost is missing: $sampleBuilder"
        }
        $sampleOutputRoot = Join-Path $walkthroughRoot 'project-output'
        $sampleBuildOutput = & $sampleBuilder build $sampleSpecPath $sampleOutputRoot 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "The Explained Word Card project build failed: $($sampleBuildOutput -join [Environment]::NewLine)"
        }
        $sampleBuild = [string]::Join([Environment]::NewLine, [string[]] $sampleBuildOutput) | ConvertFrom-Json
        $builtProjectPath = [System.IO.Path]::GetFullPath([string] $sampleBuild.projectPath)
        $projectPathRelative = [System.IO.Path]::GetRelativePath($walkthroughRoot, $builtProjectPath)
        Assert-ArtifactPath $walkthroughRoot $builtProjectPath
        if (-not (Test-Path -LiteralPath $builtProjectPath -PathType Leaf)) {
            throw "The Explained Word Card project file is missing: $builtProjectPath"
        }
        Write-JsonFile (Join-Path $walkthroughRoot 'fixture.json') ([ordered]@{
            projectPath = $projectPathRelative.Replace('\', '/')
            textId = [string] $sampleBuild.texts[0].guid
        })
    }

    Remove-ArtifactDirectory $workRoot
    if ($preparePortableWorkerPackage -and $prepareExplainedWordCardFixture) {
        Remove-ArtifactDirectory $preparedRoot
        Move-Item -LiteralPath $stageRoot -Destination $preparedRoot
    }
    else {
        [System.IO.Directory]::CreateDirectory($preparedRoot) | Out-Null
        if ($preparePortableWorkerPackage) {
            $preparedPackageRoot = Join-Path $preparedRoot 'portable-worker-package'
            $preparedValidation = Join-Path $preparedRoot 'portable-worker-package-validation.json'
            Remove-ArtifactDirectory $preparedPackageRoot
            if (Test-Path -LiteralPath $preparedValidation) { Remove-Item -LiteralPath $preparedValidation -Force }
            Move-Item -LiteralPath $packageRoot -Destination $preparedPackageRoot
            Move-Item -LiteralPath (Join-Path $stageRoot 'portable-worker-package-validation.json') `
                -Destination $preparedValidation
        }
        if ($prepareExplainedWordCardFixture) {
            $preparedWalkthroughRoot = Join-Path $preparedRoot 'walkthrough-fixtures/explained-word-card'
            [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($preparedWalkthroughRoot)) | Out-Null
            Remove-ArtifactDirectory $preparedWalkthroughRoot
            Move-Item -LiteralPath $walkthroughRoot -Destination $preparedWalkthroughRoot
        }
    }
}
finally {
    if ($null -eq $previousIntermediateRoot) {
        Remove-Item Env:MOTIF_PACKAGE_INTERMEDIATE_ROOT -ErrorAction SilentlyContinue
    }
    else {
        $env:MOTIF_PACKAGE_INTERMEDIATE_ROOT = $previousIntermediateRoot
    }
    if ($null -eq $previousSldrOffline) {
        Remove-Item Env:MOTIF_TEST_SLDR_OFFLINE -ErrorAction SilentlyContinue
    }
    else {
        $env:MOTIF_TEST_SLDR_OFFLINE = $previousSldrOffline
    }
    if ($null -eq $previousSldrCachePath) {
        Remove-Item Env:MOTIF_TEST_SLDR_CACHE_PATH -ErrorAction SilentlyContinue
    }
    else {
        $env:MOTIF_TEST_SLDR_CACHE_PATH = $previousSldrCachePath
    }
    if ($null -eq $previousWritingSystemRepositoryPath) {
        Remove-Item Env:MOTIF_WRITING_SYSTEM_REPOSITORY_PATH -ErrorAction SilentlyContinue
    }
    else {
        $env:MOTIF_WRITING_SYSTEM_REPOSITORY_PATH = $previousWritingSystemRepositoryPath
    }
    if (Test-Path -LiteralPath $stageRoot) { Remove-ArtifactDirectory $stageRoot }
    Pop-Location
}
