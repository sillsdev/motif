[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $AppExecutable,
    [Parameter(Mandatory = $true)] [string] $CliExecutable,
    [Parameter(Mandatory = $true)] [string] $SampleBuilder,
    [Parameter(Mandatory = $true)] [string] $SampleSpec,
    [Parameter(Mandatory = $true)] [string] $WalkthroughDirectory,
    [Parameter(Mandatory = $true)] [string] $WorkDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Process {
    param(
        [Parameter(Mandatory = $true)] [string] $Executable,
        [Parameter(Mandatory = $true)] [string[]] $Arguments,
        [switch] $AllowFailure
    )

    $start = [System.Diagnostics.ProcessStartInfo]::new([System.IO.Path]::GetFullPath($Executable))
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw "Could not start $Executable." }
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(600000)) {
        $process.Kill($true)
        throw "$Executable did not finish within ten minutes."
    }
    $result = [pscustomobject]@{
        ExitCode = $process.ExitCode
        Output = $stdout.GetAwaiter().GetResult()
        Error = $stderr.GetAwaiter().GetResult()
    }
    $process.Dispose()
    if (-not $AllowFailure -and $result.ExitCode -ne 0) {
        throw "$Executable exited $($result.ExitCode). $($result.Error) $($result.Output)"
    }
    return $result
}

function Invoke-CliJson {
    param([Parameter(Mandatory = $true)] [string[]] $Arguments)
    $result = Invoke-Process -Executable $script:CliPath -Arguments (@($Arguments) + '--json')
    try { return $result.Output | ConvertFrom-Json }
    catch { throw "The installed CLI did not return JSON for '$($Arguments -join ' ')': $($result.Output)" }
}

function Invoke-Cli {
    param([Parameter(Mandatory = $true)] [string[]] $Arguments)
    return Invoke-Process -Executable $script:CliPath -Arguments $Arguments
}

function Build-Sample {
    param([Parameter(Mandatory = $true)] [string] $OutputRoot)
    $build = Invoke-Process -Executable $script:BuilderPath -Arguments @('build', $script:SampleSpec, $OutputRoot)
    try { return $build.Output | ConvertFrom-Json }
    catch { throw "SampleProjects did not return its build record: $($build.Output)" }
}

function Seed-Selection {
    param([Parameter(Mandatory = $true)] [string] $Project, [Parameter(Mandatory = $true)] [string] $TextId)
    [void] (Invoke-CliJson @('baseline', 'capture', $Project))
    [void] (Invoke-CliJson @('texts', 'list', '--project', $Project))
    [void] (Invoke-CliJson @('selection', 'set-default', '--project', $Project,
        '--name', 'Default', '--texts', $TextId))
    [void] (Invoke-CliJson @('setup', 'skip', '--project', $Project))
}

function Seed-IncorrectSpelling {
    param([Parameter(Mandatory = $true)] [string] $Project)
    # Staging has no Released verb (testers stage in the window), so only this fixture setup may use one.
    $previous = $env:MOTIF_DEVELOPER_COMMANDS
    $env:MOTIF_DEVELOPER_COMMANDS = '1'
    try {
        $pending = Invoke-CliJson @('pending-changes', '--project', $Project)
        [void] (Invoke-CliJson @('put-pending-change', '--project', $Project,
            '--expected-revision', [string] $pending.revision,
            '--change-id', 'release-check-review', '--kind', 'incorrect-spelling', '--word', 'geldi'))
        # Apply needs the change measured; testers press Check these changes, which runs this trial.
        $staged = Invoke-CliJson @('pending-changes', '--project', $Project)
        [void] (Invoke-CliJson @('trial', '--pending', '--project', $Project, '--draft', [string] $staged.draftId,
            '--revision', [string] $staged.revision, '--words', 'geldi', '--wait'))
    }
    finally { $env:MOTIF_DEVELOPER_COMMANDS = $previous }
}

function Invoke-InstalledApp {
    param([Parameter(Mandatory = $true)] [string[]] $Arguments)
    $wrapper = $env:MOTIF_RELEASE_APP_WRAPPER
    if ([string]::IsNullOrWhiteSpace($wrapper)) {
        return Invoke-Process -Executable $script:AppPath -Arguments (@($script:AppPrefix) + $Arguments)
    }
    $wrapperCommand = Get-Command $wrapper -ErrorAction Stop
    $wrapperArguments = @()
    if (-not [string]::IsNullOrWhiteSpace($env:MOTIF_RELEASE_APP_WRAPPER_ARGUMENTS)) {
        $wrapperArguments = @($env:MOTIF_RELEASE_APP_WRAPPER_ARGUMENTS | ConvertFrom-Json)
    }
    return Invoke-Process -Executable $wrapperCommand.Source -Arguments (@($wrapperArguments) + $script:AppPath +
        @($script:AppPrefix) + $Arguments)
}

$script:AppPath = [System.IO.Path]::GetFullPath($AppExecutable)
$script:CliPath = [System.IO.Path]::GetFullPath($CliExecutable)
$script:BuilderPath = [System.IO.Path]::GetFullPath($SampleBuilder)
$script:SampleSpec = [System.IO.Path]::GetFullPath($SampleSpec)
$walkthroughs = [System.IO.Path]::GetFullPath($WalkthroughDirectory)
$work = [System.IO.Path]::GetFullPath($WorkDirectory)
foreach ($path in @($script:AppPath, $script:CliPath, $script:BuilderPath, $script:SampleSpec)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required release-check input is missing: $path" }
}
if (-not (Test-Path -LiteralPath $walkthroughs -PathType Container)) {
    throw "Walkthrough directory is missing: $walkthroughs"
}
New-Item -ItemType Directory -Path $work -Force | Out-Null

$appPrefix = @()
if (-not [string]::IsNullOrWhiteSpace($env:MOTIF_RELEASE_APP_PREFIX_ARGUMENTS)) {
    $appPrefix = @($env:MOTIF_RELEASE_APP_PREFIX_ARGUMENTS | ConvertFrom-Json)
}
$script:AppPrefix = $appPrefix

$releaseRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('MotifReleaseCheck-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $releaseRoot | Out-Null
$projectPaths = [ordered]@{}
$cliProjects = [ordered]@{}
$cliProjectPaths = [ordered]@{}
$extraProjectPaths = [ordered]@{}
$fixtureMap = [ordered]@{
    'first-run-ready' = 'first-run-setup-parse'
    'explained-word-card' = 'explained-word-card'
    'try-word-ready' = 'try-word-typing'
    'apply-refresh-ready' = 'review-apply-refresh-parse'
    'handoff-cancel-ready' = 'handoff-cancel-retry'
}
$sampleBuilds = [ordered]@{}
foreach ($fixture in $fixtureMap.Keys) {
    $built = Build-Sample (Join-Path $releaseRoot ('app-' + $fixture))
    $text = @($built.texts | Where-Object { $_.id -eq 'plural-harmony' })
    if ($text.Count -ne 1) { throw "The synthetic sample has no unique plural-harmony Text for $fixture." }
    $projectPaths[$fixture] = [System.IO.Path]::GetFullPath([string] $built.projectPath)
    $sampleBuilds[$fixture] = [pscustomobject]@{ ProjectPath = $projectPaths[$fixture]; TextId = [string] $text[0].guid }
}

foreach ($fixture in $fixtureMap.Keys) {
    if ($fixture -eq 'first-run-ready') { continue }
    $build = $sampleBuilds[$fixture]
    Seed-Selection $build.ProjectPath $build.TextId
    if ($fixture -in @('explained-word-card', 'apply-refresh-ready', 'handoff-cancel-ready')) {
        [void] (Invoke-Cli @('assess', $build.ProjectPath, '--texts', $build.TextId))
    }
    if ($fixture -eq 'apply-refresh-ready') { Seed-IncorrectSpelling $build.ProjectPath }
}

$cliProjects = [ordered]@{}
foreach ($fixture in $fixtureMap.Keys) {
    $built = Build-Sample (Join-Path $releaseRoot ('cli-' + $fixture))
    $text = @($built.texts | Where-Object { $_.id -eq 'plural-harmony' })
    if ($text.Count -ne 1) { throw "The synthetic sample has no unique plural-harmony Text for CLI $fixture." }
    $cliProjects[$fixture] = [pscustomobject]@{
        ProjectPath = [System.IO.Path]::GetFullPath([string] $built.projectPath)
        TextId = [string] $text[0].guid
    }
    $cliProjectPaths[$fixture] = $cliProjects[$fixture].ProjectPath
}

if ([OperatingSystem]::IsWindows()) {
    $locked = Build-Sample (Join-Path $releaseRoot 'windows-apply-lock')
    $lockedText = @($locked.texts | Where-Object { $_.id -eq 'plural-harmony' })
    if ($lockedText.Count -ne 1) { throw 'The Windows lock sample has no unique Text.' }
    $lockedProject = [System.IO.Path]::GetFullPath([string] $locked.projectPath)
    $extraProjectPaths['windows-apply-lock'] = $lockedProject
    Seed-Selection $lockedProject ([string] $lockedText[0].guid)
    Seed-IncorrectSpelling $lockedProject
}

$seedManifestPath = Join-Path $releaseRoot 'seed-manifest.json'
@{ kind = 'synthetic-motif-sample-v1'; projects = $projectPaths; cliProjects = $cliProjectPaths;
    extraProjects = $extraProjectPaths } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $seedManifestPath -Encoding utf8
Set-Content -LiteralPath (Join-Path $work 'release-pathways-root.txt') -Value $releaseRoot -Encoding utf8

# Hosted Unix runners also link dotnet from /usr/bin or /usr/local/bin, which can't leave PATH whole.
$shadowRoot = Join-Path $releaseRoot 'path-without-dotnet'
$pathEntries = @(foreach ($entry in @($env:PATH -split [System.IO.Path]::PathSeparator)) {
    if (-not $entry -or $entry -match '(?i)(^|[\\/])dotnet([\\/]|$)|(^|[\\/])\.dotnet([\\/]|$)') { continue }
    if ([OperatingSystem]::IsWindows() -or -not (Test-Path -LiteralPath (Join-Path $entry 'dotnet'))) { $entry; continue }
    $shadow = Join-Path $shadowRoot ([guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory -Path $shadow -Force | Out-Null
    foreach ($item in @(Get-ChildItem -LiteralPath $entry -Force | Where-Object Name -ne 'dotnet')) {
        New-Item -ItemType SymbolicLink -Path (Join-Path $shadow $item.Name) -Target $item.FullName | Out-Null
    }
    $shadow
})
$env:PATH = [string]::Join([System.IO.Path]::PathSeparator, $pathEntries)
$env:DOTNET_ROOT = Join-Path $releaseRoot 'sdk-hidden'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
foreach ($variable in @(Get-ChildItem Env: | Where-Object Name -Like 'DOTNET_ROOT_*')) {
    Remove-Item ("Env:" + $variable.Name) -ErrorAction SilentlyContinue
}
if (Get-Command dotnet -ErrorAction SilentlyContinue) { throw 'The .NET SDK is still visible on PATH.' }

$env:MOTIF_RELEASE_PATHWAYS = $walkthroughs
$env:MOTIF_RELEASE_SEED_MANIFEST = $seedManifestPath
$env:MOTIF_RELEASE_RESULT = Join-Path $releaseRoot 'app-pathways.json'
$env:MOTIF_RELEASE_LAUNCH_RESULT = Join-Path $releaseRoot 'app-launch.json'
$env:MOTIF_WORKER_ROOT = Join-Path $releaseRoot 'launch-worker'
$env:MOTIF_RUNNER_NAMESPACE = 'release-launch-check'
$env:MOTIF_RUNNER_IDLE_SECONDS = '2'
$env:MOTIF_WRITING_SYSTEM_REPOSITORY_PATH = Join-Path $releaseRoot 'writing-systems'
$env:MOTIF_TEST_SLDR_CACHE_PATH = Join-Path $releaseRoot 'sldr-cache'

$launch = Invoke-InstalledApp @('--release-launch-check')
if (-not (Test-Path -LiteralPath $env:MOTIF_RELEASE_LAUNCH_RESULT -PathType Leaf)) {
    throw "The installed App exited $($launch.ExitCode) without showing its window. $($launch.Error)"
}
$launchResult = Get-Content -LiteralPath $env:MOTIF_RELEASE_LAUNCH_RESULT -Raw | ConvertFrom-Json
if (-not $launchResult.opened -or $launchResult.width -le 0 -or $launchResult.height -le 0) {
    throw 'The installed App launch check did not report a visible window.'
}

$replay = Invoke-InstalledApp @('--release-pathways')
if ($replay.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $env:MOTIF_RELEASE_RESULT -PathType Leaf)) {
    throw "The installed App replay failed. $($replay.Error) $($replay.Output)"
}
$appResult = Get-Content -LiteralPath $env:MOTIF_RELEASE_RESULT -Raw | ConvertFrom-Json
if (-not $appResult.succeeded -or $appResult.pathways.Count -ne 5) {
    throw "The installed App replay did not pass all five paths: $($appResult.error)"
}
foreach ($pathway in $appResult.pathways) {
    if (-not $pathway.succeeded -or $pathway.screenshots.Count -eq 0) {
        throw "The installed App replay did not capture pathway '$($pathway.id)'. $($pathway.error)"
    }
    foreach ($capture in $pathway.screenshots) {
        if (-not (Test-Path -LiteralPath $capture -PathType Leaf)) { throw "Missing App screenshot: $capture" }
    }
}

$first = $cliProjects['first-run-ready']
Seed-Selection $first.ProjectPath $first.TextId
[void] (Invoke-Cli @('assess', $first.ProjectPath, '--texts', $first.TextId))
[void] (Invoke-CliJson @('overview', '--project', $first.ProjectPath))

$reading = $cliProjects['explained-word-card']
Seed-Selection $reading.ProjectPath $reading.TextId
$readingAssessment = Invoke-CliJson @('assess', $reading.ProjectPath, '--texts', $reading.TextId)
if ($readingAssessment.words.Count -eq 0) { throw 'The installed CLI Assessment returned no Text results.' }
[void] (Invoke-CliJson @('overview', '--project', $reading.ProjectPath))
$textInventory = Invoke-CliJson @('texts', 'list', '--project', $reading.ProjectPath)
if (-not @($textInventory.texts | Where-Object { $_.id -eq $reading.TextId })) {
    throw 'The installed CLI could not read the selected Text from its Baseline.'
}

$tryWord = $cliProjects['try-word-ready']
Seed-Selection $tryWord.ProjectPath $tryWord.TextId
$trace = Invoke-CliJson @('trace', '--project', $tryWord.ProjectPath, '--word', 'geldi')
if (-not $trace.parsed -or -not $trace.complete) { throw 'The installed CLI could not trace the seeded word.' }

$review = $cliProjects['apply-refresh-ready']
Seed-Selection $review.ProjectPath $review.TextId
[void] (Invoke-Cli @('assess', $review.ProjectPath, '--texts', $review.TextId))
Seed-IncorrectSpelling $review.ProjectPath
$applied = Invoke-CliJson @('apply', '--all-pending', '--project', $review.ProjectPath)
if (-not $applied.ok -or -not $applied.applied) { throw 'The installed CLI did not apply the staged change.' }
[void] (Invoke-CliJson @('baseline', 'capture', $review.ProjectPath))
[void] (Invoke-Cli @('assess', $review.ProjectPath, '--texts', $review.TextId))
[void] (Invoke-CliJson @('overview', '--project', $review.ProjectPath))

$handoff = $cliProjects['handoff-cancel-ready']
Seed-Selection $handoff.ProjectPath $handoff.TextId
[void] (Invoke-Cli @('assess', $handoff.ProjectPath, '--texts', $handoff.TextId))
$handoffOutput = Join-Path $releaseRoot 'cli-handoff'
[void] (Invoke-CliJson @('handoff', $handoff.ProjectPath, '--out', $handoffOutput, '--no-assess'))
if (-not (Get-ChildItem -LiteralPath $handoffOutput -File -Recurse)) {
    throw 'The installed CLI Handoff did not write any files.'
}

if ([OperatingSystem]::IsWindows()) {
    $lock = [System.IO.FileStream]::new($lockedProject + '.lock', [System.IO.FileMode]::OpenOrCreate,
        [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    try {
        $refused = Invoke-Process -Executable $script:CliPath -Arguments @(
            'apply', '--all-pending', '--project', $lockedProject, '--json') -AllowFailure
        $detail = $refused.Output + ' ' + $refused.Error
        if ($refused.ExitCode -eq 0 -or $detail -notmatch '(?i)project\.in-use|in use|another program|locked') {
            throw "Apply was not refused while the Windows project was locked: $detail"
        }
    }
    finally {
        $lock.Dispose()
        Remove-Item -LiteralPath ($lockedProject + '.lock') -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Installed release pathways passed with .NET SDK hidden. Evidence: $releaseRoot"
