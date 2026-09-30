[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $ReleaseValidation,

    [string] $ParserArtifact
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($Configuration -ne 'Release') {
    throw 'Documentation validation requires a Release build.'
}
if ($ReleaseValidation -and [string]::IsNullOrWhiteSpace($ParserArtifact)) {
    $ParserArtifact = $env:MOTIF_PANGLOSS_EXE
}

$env:MSBUILDDISABLENODEREUSE = '1'
$env:UseSharedCompilation = 'false'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:MOTIF_WALKTHROUGH_UPDATE_BASELINES = '0'
$runRoot = Join-Path $repoRoot ('bin/Release/documentation-validation/' + [Guid]::NewGuid().ToString('N'))
$walkthroughOutput = Join-Path $runRoot 'walkthroughs'
[System.IO.Directory]::CreateDirectory($walkthroughOutput) | Out-Null
$env:MOTIF_WALKTHROUGH_OUTPUT = $walkthroughOutput
$env:MOTIF_WALKTHROUGH_CLIPS = if ($ReleaseValidation) { '1' } else { '0' }
$env:MOTIF_WALKTHROUGH_REQUIRE_CLIPS = if ($ReleaseValidation) { '1' } else { '0' }

$pinModule = Join-Path $PSScriptRoot 'PanGlossRelease.psm1'
Import-Module $pinModule -Force
& (Join-Path $PSScriptRoot 'Test-PanGlossRelease.ps1') -RepositoryRoot $repoRoot `
    -ProbeRoot (Join-Path $runRoot 'pan-gloss-pin-probe')
if ($ReleaseValidation) {
    $pinnedParser = Get-PinnedPanGlossArtifact -RepositoryRoot $repoRoot -ArtifactPath $ParserArtifact
    $env:MOTIF_PANGLOSS_EXE = $pinnedParser.Path
}

Push-Location $repoRoot
try {
    & (Join-Path $repoRoot 'build.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'The repository build wrapper failed.' }

    & (Join-Path $repoRoot 'tools/Build-Samples.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'The sample build failed.' }

    $cliName = if ([OperatingSystem]::IsWindows()) { 'motif.exe' } else { 'motif' }
    $cliPath = Join-Path $repoRoot "bin/$Configuration/$cliName"
    if (-not (Test-Path -LiteralPath $cliPath -PathType Leaf)) {
        throw "Built CLI was not found at $cliPath."
    }
    $env:MOTIF_SITE_CLI_EXE = $cliPath

    & (Join-Path $repoRoot 'test.ps1') -Configuration $Configuration -SkipBuild
    if ($LASTEXITCODE -ne 0) { throw 'The repository test wrapper failed.' }
    if ($ReleaseValidation) {
        & (Join-Path $repoRoot 'tools/Assert-RequiredPanGloss.ps1') -ResultsDirectory (Join-Path $repoRoot "bin/$Configuration/test-results")
    }

    $nodeVersionText = (& node --version).Trim().TrimStart('v')
    if ([Version] $nodeVersionText -lt [Version] '22.12.0') {
        throw "Documentation site requires Node 22.12 or newer; found $nodeVersionText."
    }
    & npm ci --prefix (Join-Path $repoRoot 'site') --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'Could not install the documentation site dependencies.' }

    $helpProjectDirectory = Join-Path $repoRoot 'src/SIL.Motif.Help'
    $helpProjects = @(Get-ChildItem -LiteralPath $helpProjectDirectory -File -Filter '*.csproj')
    if ($helpProjects.Count -ne 1) { throw "Expected one Help project under $helpProjectDirectory." }
    [xml] $helpProject = Get-Content -LiteralPath $helpProjects[0].FullName -Raw
    $contentRoots = @()
    foreach ($resource in $helpProject.SelectNodes("//*[local-name()='EmbeddedResource']")) {
        $include = [string] $resource.GetAttribute('Include')
        $wildcard = $include.IndexOf('*')
        if ($wildcard -lt 0) { continue }
        $prefix = $include.Substring(0, $wildcard).TrimEnd([char[]] @('\', '/'))
        if ([string]::IsNullOrWhiteSpace($prefix)) { continue }
        $contentRoots += [System.IO.Path]::GetFullPath((Join-Path $helpProjects[0].DirectoryName $prefix))
    }
    $contentRoots = @($contentRoots | Select-Object -Unique | Where-Object {
        Test-Path -LiteralPath $_ -PathType Container
    })
    if ($contentRoots.Count -ne 1) {
        throw "Could not resolve one authored Help content root from $($helpProjects[0].FullName)."
    }
    $helpRoot = $contentRoots[0]

    $helpExportPath = Join-Path $runRoot 'help-export.json'
    $helpErrorPath = Join-Path $runRoot 'help-export.stderr.log'
    $helpEnvironment = @{
        MOTIF_WORKER_ROOT = (Join-Path $runRoot 'worker')
        MOTIF_RUNNER_NAMESPACE = ('documentation-' + [IO.Path]::GetFileName($runRoot))
    }
    $helpProcess = Start-Process -Environment $helpEnvironment -FilePath $cliPath -ArgumentList @('help', '--all', '--json') -RedirectStandardOutput $helpExportPath -RedirectStandardError $helpErrorPath -Wait -PassThru -NoNewWindow
    if ($helpProcess.ExitCode -ne 0) {
        $details = if (Test-Path -LiteralPath $helpErrorPath) {
            Get-Content -LiteralPath $helpErrorPath -Raw
        }
        else {
            ''
        }
        throw "Built CLI Help export failed with exit code $($helpProcess.ExitCode): $details"
    }
    if (-not (Test-Path -LiteralPath $helpExportPath -PathType Leaf) -or
        (Get-Item -LiteralPath $helpExportPath).Length -eq 0) {
        throw 'Built CLI Help export was empty.'
    }

    $samplesOutput = Join-Path $repoRoot "bin/$Configuration/samples"
    $apiXml = Join-Path $repoRoot "bin/$Configuration/SIL.Motif.Contract.xml"
    $env:MOTIF_SITE_SYNC_MODE = 'production'
    $env:MOTIF_HELP_EXPORT = $helpExportPath
    $env:MOTIF_HELP_CONTENT_ROOT = $helpRoot
    $env:MOTIF_WALKTHROUGH_OUTPUT = $walkthroughOutput
    $env:MOTIF_CONTRACT_XML = $apiXml
    $env:MOTIF_SAMPLES_ROOT = Join-Path $repoRoot 'samples'
    $env:MOTIF_SAMPLES_OUT = $samplesOutput
    $env:MOTIF_DOCS_ROOT = Join-Path $repoRoot 'docs'
    $env:MOTIF_BUILD_CONFIGURATION = $Configuration

    & npm run build --prefix (Join-Path $repoRoot 'site')
    if ($LASTEXITCODE -ne 0) { throw 'Production documentation sync or site build failed.' }

    $syncMode = $env:MOTIF_SITE_SYNC_MODE
    try {
        $env:MOTIF_SITE_SYNC_MODE = ''
        & npm test --prefix (Join-Path $repoRoot 'site')
        if ($LASTEXITCODE -ne 0) { throw 'Documentation site tests failed.' }
    }
    finally {
        $env:MOTIF_SITE_SYNC_MODE = $syncMode
    }

    if ($ReleaseValidation) {
        Write-Output ("Release documentation artifacts: " + (Join-Path $repoRoot 'site/dist'))
    }
    else {
        Write-Output ("Documentation artifacts: " + (Join-Path $repoRoot 'site/dist'))
    }
}
finally {
    Pop-Location
}
