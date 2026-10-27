[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $RepositoryRoot,

    [string] $ParserPath,

    [string] $PinnedParserPath,

    [string] $ProbeRoot,

    [string] $Configuration = 'Debug',

    [switch] $RequireParser
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$pinPath = Join-Path $RepositoryRoot 'pangloss-release.json'
$pin = Get-Content -LiteralPath $pinPath -Raw | ConvertFrom-Json
$interfaces = $pin.interfaces
if ($null -eq $interfaces) { throw 'pangloss-release.json has no interfaces object.' }

if ([string]::IsNullOrWhiteSpace($PinnedParserPath)) {
    $PinnedParserPath = Join-Path $RepositoryRoot "bin/$Configuration/$(if ($IsWindows) { 'pangloss.exe' } else { 'pangloss' })"
}
if ([string]::IsNullOrWhiteSpace($ParserPath)) {
    $ParserPath = $env:MOTIF_PANGLOSS_EXE
}
if ([string]::IsNullOrWhiteSpace($ParserPath)) { $ParserPath = $PinnedParserPath }

$strict = $RequireParser -or $Configuration -eq 'Release' -or
    $env:CI -match '^(?i:true|1|yes)$'
$pinnedExists = Test-Path -LiteralPath $PinnedParserPath -PathType Leaf
$parserExists = Test-Path -LiteralPath $ParserPath -PathType Leaf
if (-not $pinnedExists) {
    $message = "The staged PanGloss pin is missing: $PinnedParserPath"
    if ($strict) { throw $message }
    Write-Warning "$message The artifact hash check is pending for this ordinary developer build."
}
else {
    Import-Module (Join-Path $RepositoryRoot 'tools/PanGlossRelease.psm1') -Force
    $verifiedPin = Get-PinnedPanGlossArtifact -RepositoryRoot $RepositoryRoot -ArtifactPath $PinnedParserPath
    Write-Host "Verified staged PanGloss $($verifiedPin.Tag) sha256 against pangloss-release.json."
}

if (-not $parserExists) {
    $message = "PanGloss is unavailable for interface checks: $ParserPath"
    if ($strict) { throw $message }
    Write-Warning "$message The interface smoke checks are pending for this ordinary developer build."
    return
}

$pathComparer = if ($IsWindows) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }
$isPinnedCandidate = $pinnedExists -and $pathComparer.Equals(
    [System.IO.Path]::GetFullPath($ParserPath), [System.IO.Path]::GetFullPath($PinnedParserPath))
$sourceTagMatch = [regex]::Match([string] $interfaces.sourceTag, '^v(\d+\.\d+\.\d+)$')
if (-not $sourceTagMatch.Success) {
    throw "PanGloss interface mismatch: interfaces.sourceTag expected 'v<major>.<minor>.<patch>', actual '$($interfaces.sourceTag)'."
}
$expectedParserVersion = if ($isPinnedCandidate) { [string] $pin.version } else { $sourceTagMatch.Groups[1].Value }

function Invoke-ChildProcess {
    param(
        [string] $ExecutablePath,
        [string[]] $Arguments,
        [string] $Purpose
    )

    $start = [System.Diagnostics.ProcessStartInfo]::new($ExecutablePath)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $start.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)
    [void] $start.Environment.Remove('ICU_DATA')
    foreach ($argument in $Arguments) { [void] $start.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $start
    $started = $false
    try {
        if (-not $process.Start()) { throw "Could not start ${Purpose}: $ExecutablePath" }
        $started = $true
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) {
            try { $process.Kill($true) } catch { }
            [void] $process.WaitForExit(10000)
            throw "$Purpose did not finish within 60 seconds: $($Arguments -join ' ')"
        }
        $process.WaitForExit()
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StandardOutput = $stdout.GetAwaiter().GetResult()
            StandardError = $stderr.GetAwaiter().GetResult()
        }
    }
    finally {
        if ($started -and -not $process.HasExited) {
            try { $process.Kill($true) } catch { }
        }
        $process.Dispose()
    }
}

function Invoke-PanGloss {
    param([string[]] $Arguments)
    Invoke-ChildProcess $ParserPath $Arguments 'PanGloss'
}

function Assert-Interface {
    param([string] $Name, [object] $Expected, [object] $Actual)
    if ([string] $Expected -cne [string] $Actual) {
        $renderedActual = if ($null -eq $Actual) { '<missing>' } else { [string] $Actual }
        throw "PanGloss interface mismatch: $Name expected '$Expected', actual '$renderedActual'."
    }
}

function Read-Json {
    param([string] $Json, [string] $Name)
    try { return ConvertFrom-Json -InputObject $Json -ErrorAction Stop }
    catch { throw "PanGloss interface mismatch: $Name expected JSON, actual invalid JSON ($($_.Exception.Message))." }
}

$versionResult = Invoke-PanGloss @('--version')
if ($versionResult.ExitCode -ne 0) {
    throw "PanGloss interface mismatch: --version expected exit 0, actual $($versionResult.ExitCode). $($versionResult.StandardError.Trim())"
}
$versionMatch = [regex]::Match($versionResult.StandardOutput.Trim(), '^pangloss\s+(\S+)$')
$actualVersion = if ($versionMatch.Success) { $versionMatch.Groups[1].Value } else { $null }
Assert-Interface 'parser version' $expectedParserVersion $actualVersion

$descriptionResult = Invoke-PanGloss @('--describe')
if ($descriptionResult.ExitCode -ne 0) {
    throw "PanGloss interface mismatch: --describe expected exit 0, actual $($descriptionResult.ExitCode). $($descriptionResult.StandardError.Trim())"
}
$description = Read-Json $descriptionResult.StandardOutput '--describe'
Assert-Interface '--describe schema_version' $interfaces.describeSchemaVersion $description.schema_version
Assert-Interface '--describe binary' 'pangloss' $description.binary
Assert-Interface '--describe facts.schemaVersion' $interfaces.facts.schemaVersion $description.facts.schemaVersion
Assert-Interface '--describe facts.contextVersion' $interfaces.facts.contextVersion $description.facts.contextVersion
Assert-Interface '--describe facts.statsManifestVersion' $interfaces.facts.statsManifestVersion $description.facts.statsManifestVersion
Assert-Interface '--describe interfaces.serveProtocol' $interfaces.serveProtocol $description.interfaces.serveProtocol

$commands = @{}
foreach ($command in @($description.commands)) {
    if (-not [string]::IsNullOrWhiteSpace([string] $command.name)) { $commands[[string] $command.name] = $command }
}
foreach ($requiredCommand in $interfaces.requests.PSObject.Properties) {
    $name = $requiredCommand.Name
    if (-not $commands.ContainsKey($name)) {
        Assert-Interface "--describe command $name" 'present' 'missing'
    }
    Assert-Interface "--describe command $name visible" $true (-not [bool] $commands[$name].hidden)
    $flags = @{}
    foreach ($flag in @($commands[$name].flags)) { $flags[[string] $flag.name] = $flag }
    foreach ($requiredFlag in $requiredCommand.Value.PSObject.Properties) {
        $flagName = $requiredFlag.Name
        if (-not $flags.ContainsKey($flagName)) {
            Assert-Interface "--describe $name flag $flagName" 'present' 'missing'
        }
        Assert-Interface "--describe $name $flagName takes_value" $requiredFlag.Value $flags[$flagName].takes_value
    }
}

if ([string]::IsNullOrWhiteSpace($ProbeRoot)) {
    $ProbeRoot = Join-Path $RepositoryRoot "bin/.cache/pangloss-interface-gate-$([guid]::NewGuid().ToString('N'))"
}
$ProbeRoot = [System.IO.Path]::GetFullPath($ProbeRoot)
if (Test-Path -LiteralPath $ProbeRoot) { throw "PanGloss interface probe directory already exists: $ProbeRoot" }
[System.IO.Directory]::CreateDirectory($ProbeRoot) | Out-Null
try {
    $sampleProjectsPath = Join-Path $RepositoryRoot "bin/$Configuration/SIL.Motif.SampleProjects.dll"
    if (-not (Test-Path -LiteralPath $sampleProjectsPath -PathType Leaf)) {
        throw "SampleProjects is missing; build it with ./build.ps1: $sampleProjectsPath"
    }
    $dotnetHost = Get-Command dotnet -CommandType Application -ErrorAction Stop
    $generatedRoot = Join-Path $ProbeRoot 'generated'
    $generation = Invoke-ChildProcess $dotnetHost.Source @(
        $sampleProjectsPath, 'interface-probe', $generatedRoot) 'SampleProjects'
    if ($generation.ExitCode -ne 0) {
        throw "PanGloss interface probe generation failed with exit $($generation.ExitCode). $($generation.StandardError.Trim())"
    }
    $probe = Read-Json $generation.StandardOutput 'generated LibLCM interface probe'
    $grammar = [System.IO.Path]::GetFullPath([string] $probe.projectPath)
    $brokenGrammar = [System.IO.Path]::GetFullPath([string] $probe.compileErrorProjectPath)
    $word = [string] $probe.word
    foreach ($path in @($grammar, $brokenGrammar)) {
        $relativePath = [System.IO.Path]::GetRelativePath($ProbeRoot, $path)
        if ($relativePath -eq '..' -or $relativePath.StartsWith("..$([System.IO.Path]::DirectorySeparatorChar)", [StringComparison]::Ordinal)) {
            throw "PanGloss interface probe escaped its private directory: $path"
        }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "PanGloss interface probe did not create a project file: $path"
        }
    }
    if ([string]::IsNullOrWhiteSpace($word) -or $pathComparer.Equals($grammar, $brokenGrammar)) {
        throw 'PanGloss interface probe returned an empty word or duplicate project paths.'
    }

    $health = Invoke-PanGloss @('grammar-health', $grammar, '--fw-project', 'interface smoke')
    if ($health.ExitCode -ne 0 -and [string]::IsNullOrWhiteSpace($health.StandardOutput)) {
        throw "PanGloss interface mismatch: grammar-health expected a JSON report, actual exit $($health.ExitCode). $($health.StandardError.Trim())"
    }
    $healthReport = Read-Json $health.StandardOutput 'grammar-health'
    Assert-Interface 'grammar-health schema_version' $interfaces.grammarHealthSchemaVersion $healthReport.schema_version

    $trace = Invoke-PanGloss @('parse', $grammar, $word, '--trace', '--trace-format', 'json', '--trace-details')
    if ($trace.ExitCode -ne 0) {
        throw "PanGloss interface mismatch: parse trace expected exit 0, actual $($trace.ExitCode). $($trace.StandardError.Trim())"
    }
    $traceReport = Read-Json $trace.StandardOutput 'parse --trace-details'
    Assert-Interface 'trace-details schemaVersion' $interfaces.traceDetailsSchemaId $traceReport.schemaVersion

    $words = Join-Path $ProbeRoot 'words.txt'
    $tsv = Join-Path $ProbeRoot 'batch.tsv'
    $statsCache = Join-Path $ProbeRoot 'stats.sqlite'
    [System.IO.File]::WriteAllText($words, "$word`n", [System.Text.UTF8Encoding]::new($false))
    $batch = Invoke-PanGloss @(
        'batch', $grammar, $words, $tsv,
        '--step-cap', '100', '--threads', '1', '--stats', '--cache', $statsCache)
    if ($batch.ExitCode -ne 0) {
        throw "PanGloss interface mismatch: batch --stats expected exit 0, actual $($batch.ExitCode). $($batch.StandardError.Trim())"
    }
    $completedRow = @(Get-Content -LiteralPath $tsv | Where-Object { $_ -and $_ -notmatch "`tSTARTED$" }) | Select-Object -Last 1
    $cells = if ($null -eq $completedRow) { @() } else { [regex]::Split([string] $completedRow, "`t") }
    Assert-Interface 'batch TSV completion columns' @($interfaces.batchTsvColumns).Count $cells.Length
    Assert-Interface "batch TSV column '$($interfaces.batchTsvColumns[0])'" '0' $cells[0]
    Assert-Interface "batch TSV column '$($interfaces.batchTsvColumns[1])'" $word $cells[1]
    if ($cells[2] -notmatch '^\d+(\.\d+)?$') {
        throw "PanGloss interface mismatch: batch TSV column '$($interfaces.batchTsvColumns[2])' expected a non-negative number, actual '$($cells[2])'."
    }
    $statuses = @('ok', 'SKIPPED', 'CAP', 'TIMEOUT')
    if ($cells[3] -cnotin $statuses) {
        $expectedStatuses = $statuses -join ', '
        throw "PanGloss interface mismatch: batch TSV column '$($interfaces.batchTsvColumns[3])' expected one of '$expectedStatuses', actual '$($cells[3])'."
    }
    Assert-Interface "batch TSV column '$($interfaces.batchTsvColumns[4])'" '-' $cells[4]

    $sqliteDirectory = Join-Path $RepositoryRoot "bin/$Configuration"
    $nativeName = if ($IsWindows) { 'e_sqlite3.dll' } elseif ($IsMacOS) { 'libe_sqlite3.dylib' } else { 'libe_sqlite3.so' }
    # The reader runs in a child process from a probe copy: Windows cannot delete a DLL this process has loaded.
    $sqliteProbe = Join-Path $ProbeRoot 'sqlite'
    New-Item -ItemType Directory -Force -Path $sqliteProbe | Out-Null
    Copy-Item -LiteralPath (Join-Path $sqliteDirectory $nativeName) -Destination $sqliteProbe
    foreach ($assemblyName in @('SQLitePCLRaw.core.dll', 'SQLitePCLRaw.provider.e_sqlite3.dll',
            'SQLitePCLRaw.batteries_v2.dll', 'Microsoft.Data.Sqlite.dll')) {
        $assemblyPath = Join-Path $sqliteDirectory $assemblyName
        if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
            throw "PanGloss interface check cannot read the generated stats cache; missing $assemblyPath."
        }
        Copy-Item -LiteralPath $assemblyPath -Destination $sqliteProbe
    }
    $reader = @'
param([string] $Directory, [string] $Cache)
$ErrorActionPreference = 'Stop'
foreach ($name in @('SQLitePCLRaw.core', 'SQLitePCLRaw.provider.e_sqlite3', 'SQLitePCLRaw.batteries_v2', 'Microsoft.Data.Sqlite')) {
    [void] [System.Reflection.Assembly]::LoadFrom((Join-Path $Directory "$name.dll"))
}
[void] [System.Reflection.Assembly]::Load('SQLitePCLRaw.batteries_v2').GetType('SQLitePCL.Batteries_V2').GetMethod('Init').Invoke($null, @())
$type = [System.Reflection.Assembly]::Load('Microsoft.Data.Sqlite').GetType('Microsoft.Data.Sqlite.SqliteConnection')
$connection = [Activator]::CreateInstance($type, @("Data Source=$Cache;Mode=ReadOnly;Pooling=False"))
try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = 'SELECT schema_version FROM cache_identity WHERE cache_id = 1'
    [Console]::Out.Write([string] $command.ExecuteScalar())
}
finally { $connection.Dispose() }
'@
    $readerPath = Join-Path $sqliteProbe 'read-stats-version.ps1'
    [System.IO.File]::WriteAllText($readerPath, $reader, [System.Text.UTF8Encoding]::new($false))
    $pwsh = (Get-Process -Id $PID).Path
    $statsReader = Invoke-ChildProcess $pwsh @(
        '-NoProfile', '-NonInteractive', '-File', $readerPath,
        '-Directory', $sqliteProbe, '-Cache', $statsCache) 'stats-cache reader'
    if ($statsReader.ExitCode -ne 0) {
        throw "PanGloss interface check could not read the stats cache version; the reader exited $($statsReader.ExitCode). $($statsReader.StandardError.Trim())"
    }
    Assert-Interface 'stats cache schema_version' $interfaces.statsCacheVersion $statsReader.StandardOutput.Trim()

    $compile = Invoke-PanGloss @(
        'parse', $brokenGrammar, $word, '--trace', '--trace-format', 'json', '--trace-details')
    $compileReport = $null
    foreach ($line in $compile.StandardError -split "`r?`n") {
        if ($line.TrimStart().StartsWith('{')) {
            try {
                $candidate = ConvertFrom-Json -InputObject $line -ErrorAction Stop
                if ($candidate.status -eq 'compile_error') { $compileReport = $candidate; break }
            }
            catch { }
        }
    }
    if ($null -eq $compileReport) {
        throw "PanGloss interface mismatch: deliberate compile error expected structured stderr, actual exit $($compile.ExitCode). $($compile.StandardError.Trim())"
    }
    Assert-Interface 'compile-error status' 'compile_error' $compileReport.status
    Assert-Interface 'compile-error schema_version' $interfaces.compileErrorSchemaVersion $compileReport.schema_version

    Write-Host "PanGloss interfaces match $($interfaces.sourceTag) using $ParserPath." -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $ProbeRoot) { Remove-Item -LiteralPath $ProbeRoot -Recurse -Force }
}
