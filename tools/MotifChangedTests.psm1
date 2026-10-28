Set-StrictMode -Version Latest

<#
  .SYNOPSIS
  Changed-files test selection for test.ps1 -Changed, from a measured per-class coverage map.

  .DESCRIPTION
  test.ps1 -BuildMap runs the full suite once under coverlet with CoverageMapRecorder, which attributes executed
  source files to the test class that ran. Merge-MotifCoverageMap folds the per-process files into one JSON map
  keyed by the commit it was built at. Select-MotifChangedTests then reads `git diff` and returns the classes to
  run, with the reason for each, or Full when anything is unknown. See docs/testing-changed-files.md.
#>

# Files whose change can alter how every test builds or runs: the selection gives up and runs everything.
$script:FullRunPatterns = @(
    '^(build\.ps1|test\.ps1|Motif\.sln|nuget\.config|SilVersions\.props|global\.json|\.editorconfig)$'
    '^Directory\.(Build|Packages)\.[^/]+$'
    '(^|/)Directory\.(Build|Packages)\.[^/]+$'
    '\.(csproj|props|targets|slnx?|runsettings)$'
    '^\.config/'
    '^\.github/workflows/'
    '^tools/.*\.(ps1|psm1|psd1|json|py|sh|txt|toml|xml)$'
)
# Changes no test reads, unless a test names the file in a string literal.
$script:InertPatterns = @('^docs/', '^site/', '^plugin/', '^licenses/', '\.md$', '^LICENSE$', '^\.github/', '^\.gitignore$', '^\.gitattributes$')

function Invoke-MotifGit {
    param([Parameter(Mandatory)][string] $RepoRoot, [Parameter(Mandatory)][string[]] $Arguments)
    $output = & git -C $RepoRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed: $output" }
    return @($output | ForEach-Object { [string] $_ })
}

# Class names declared in each test file that starts a process, and the shared helpers that start one.
function Get-MotifProcessStarters {
    param([Parameter(Mandatory)][string] $RepoRoot)
    $found = & git -C $RepoRoot grep -l -E 'Process\.Start|ProcessStartInfo' -- 'tests/*.cs' 2>$null
    $declared = @{}
    $helpers = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($path in $found) {
        $text = Get-Content -LiteralPath (Join-Path $RepoRoot $path) -Raw
        $declared[$path] = @([regex]::Matches($text, '\bclass\s+(\w+)') | ForEach-Object { $_.Groups[1].Value })
        # Support's helpers are where child processes start; python detection only probes the interpreter.
        if ($path -like 'tests/SIL.Motif.Tests.Support/*' -and $path -notlike '*RequiresPython*') { [void] $helpers.Add($path) }
    }
    return [pscustomobject]@{ Declared = $declared; Helpers = $helpers }
}

# Classes with a test that failed in the run: their measured coverage stopped short.
function Get-MotifFailedClasses {
    param([Parameter(Mandatory)][string] $ResultsDirectory)
    $failed = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($trx in Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -Recurse -File) {
        [xml] $document = Get-Content -LiteralPath $trx.FullName -Raw
        $classOf = @{}
        foreach ($test in $document.SelectNodes("//*[local-name()='UnitTest']")) {
            $method = $test.SelectSingleNode("*[local-name()='TestMethod']")
            if ($method) { $classOf[[string] $test.id] = [string] $method.className }
        }
        foreach ($result in $document.SelectNodes("//*[local-name()='UnitTestResult']")) {
            if ($result.outcome -in @('Passed', 'NotExecuted')) { continue }
            $name = $classOf[[string] $result.testId]
            if ($name) { [void] $failed.Add($name) }
        }
    }
    return $failed
}

<#
  .SYNOPSIS
  Folds the per-process JSON files CoverageMapRecorder wrote into one map file.
#>
function Merge-MotifCoverageMap {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $InputDirectory,
        [Parameter(Mandatory)][string] $OutputPath,
        [Parameter(Mandatory)][string] $RepoRoot,
        [Parameter(Mandatory)][string] $Commit,
        [string] $ResultsDirectory
    )

    $merged = @{}
    foreach ($file in Get-ChildItem -LiteralPath $InputDirectory -Filter '*.json' -File) {
        $document = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json -AsHashtable
        $project = ([string] $document.assembly).Split(',')[0]
        foreach ($name in $document.classes.Keys) {
            $entry = $document.classes[$name]
            if (-not $merged.ContainsKey($name)) {
                $merged[$name] = @{ Project = $project; Seconds = 0.0; Tests = 0; Skipped = 0; Files = [System.Collections.Generic.HashSet[string]]::new() }
            }
            $target = $merged[$name]
            $target.Seconds += [double] $entry.seconds
            $target.Tests += [int] $entry.tests
            $target.Skipped += [int] $entry.skipped
            foreach ($path in $entry.files) {
                # Generated sources live under obj/ and change only when a real source does.
                $relative = $path
                if ($relative -notmatch '(^|/)obj/') { [void] $target.Files.Add($relative) }
            }
        }
    }
    if ($merged.Count -eq 0) { throw "No coverage map files were found in $InputDirectory." }

    $starters = Get-MotifProcessStarters -RepoRoot $RepoRoot
    $failedClasses = if ($ResultsDirectory) { Get-MotifFailedClasses -ResultsDirectory $ResultsDirectory } else { @() }
    $fileIndex = @{}
    $fileList = [System.Collections.Generic.List[string]]::new()
    $classes = [ordered]@{}
    foreach ($name in ($merged.Keys | Sort-Object)) {
        $entry = $merged[$name]
        $indices = foreach ($path in ($entry.Files | Sort-Object)) {
            if (-not $fileIndex.ContainsKey($path)) { $fileIndex[$path] = $fileList.Count; $fileList.Add($path) }
            $fileIndex[$path]
        }
        # A class starts a process when it runs a Support helper that does, or is declared in a file that does.
        $simple = ($name -split '[.+]')[-1]
        $outer = ($name -split '\+')[0] -split '\.' | Select-Object -Last 1
        $spawns = @($entry.Files | Where-Object {
            $starters.Helpers.Contains($_) -or ($starters.Declared.ContainsKey($_) -and ($starters.Declared[$_] -contains $simple -or $starters.Declared[$_] -contains $outer))
        }).Count -gt 0
        $classes[$name] = [ordered]@{
            p = $entry.Project; s = [Math]::Round($entry.Seconds, 2); t = $entry.Tests; k = $entry.Skipped
            x = [int] $spawns; u = [int] ($failedClasses -contains $name); f = @($indices)
        }
    }
    $map = [ordered]@{
        version = 1; commit = $Commit; builtUtc = [datetime]::UtcNow.ToString('o'); files = $fileList; classes = $classes
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
    $map | ConvertTo-Json -Depth 5 -Compress | Set-Content -LiteralPath $OutputPath -Encoding utf8
    return [pscustomobject]@{ Classes = $classes.Count; Files = $fileList.Count; Path = $OutputPath }
}

function Read-MotifCoverageMap {
    param([Parameter(Mandatory)][string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable
}

# Class full names declared in one C# file, as test-class selection entries (a class and its nested classes).
function Get-MotifDeclaredClassEntries {
    param([string] $Text)
    $namespace = if ($Text -match '(?m)^\s*namespace\s+([\w.]+)\s*[;{]') { $Matches[1] } else { '' }
    $entries = foreach ($match in [regex]::Matches($Text, '(?m)^\s*(?:(?:public|internal|private|protected|sealed|static|abstract|partial)\s+)*class\s+(\w+)')) {
        $full = if ($namespace) { "$namespace.$($match.Groups[1].Value)" } else { $match.Groups[1].Value }
        $full
        "$full+*"
    }
    return @($entries)
}

function Find-MotifQuotedMentions {
    param([string] $RepoRoot, [string] $Name)
    $pattern = '"[^"\r\n]*' + [regex]::Escape($Name).Replace('\ ', ' ')
    $found = & git -C $RepoRoot grep -l -E -e $pattern -- '*.cs' 2>$null
    return @($found | ForEach-Object { [string] $_ })
}

function Find-MotifWordMentions {
    param([string] $RepoRoot, [string] $Name)
    $found = & git -C $RepoRoot grep -l -w -F -e $Name -- 'src/*.cs' 'tests/*.cs' 2>$null
    return @($found | ForEach-Object { [string] $_ })
}

<#
  .SYNOPSIS
  Chooses the test classes a diff needs, or says why the whole suite is needed.

  .OUTPUTS
  An object with Full (bool), FullReasons, Classes (name -> Project, Seconds, Reasons), Ignored, Changes, MapCommit,
  MapAge and Base.
#>
function Select-MotifChangedTests {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $RepoRoot,
        [Parameter(Mandatory)][string] $MapPath,
        [string] $Base,
        [string] $Head,
        [int] $MaxMapAge = 40,
        [switch] $IgnoreMapAge,
        [string] $SmokePath
    )

    $selection = [pscustomobject]@{
        Full = $false; FullReasons = [System.Collections.Generic.List[string]]::new()
        Classes = [ordered]@{}; Ignored = [System.Collections.Generic.List[string]]::new()
        Changes = @(); MapCommit = ''; MapAge = -1; Base = ''
    }
    function Add-Full([string] $reason) { $selection.Full = $true; $selection.FullReasons.Add($reason) }

    $map = Read-MotifCoverageMap -Path $MapPath
    if ($null -eq $map) { Add-Full "no coverage map at $MapPath (build one with ./test.ps1 -BuildMap)"; return $selection }
    $selection.MapCommit = [string] $map.commit

    if (-not $Base) {
        foreach ($candidate in @('origin/main', 'main')) {
            $found = & git -C $RepoRoot merge-base ($(if ($Head) { $Head } else { 'HEAD' })) $candidate 2>$null
            if ($LASTEXITCODE -eq 0 -and $found) { $Base = [string] $found; break }
        }
    }
    if (-not $Base) { Add-Full 'no base: there is no origin/main or main to diff against'; return $selection }
    $selection.Base = $Base

    # The map must be an ancestor of what is being tested and not too many commits behind it.
    $target = if ($Head) { $Head } else { 'HEAD' }
    if (-not $IgnoreMapAge) {
        & git -C $RepoRoot merge-base --is-ancestor $map.commit $target 2>$null
        if ($LASTEXITCODE -ne 0) {
            Add-Full "the map's commit $($map.commit.Substring(0, 9)) is not an ancestor of $target"
        }
        else {
            $selection.MapAge = [int] (@(Invoke-MotifGit $RepoRoot @('rev-list', '--count', "$($map.commit)..$target"))[0])
            if ($selection.MapAge -gt $MaxMapAge) {
                Add-Full "the map is $($selection.MapAge) commits old (limit $MaxMapAge); rebuild it with ./test.ps1 -BuildMap"
            }
        }
    }

    $diffArguments = if ($Head) { @('diff', '--name-status', '--no-renames', $Base, $Head) } else { @('diff', '--name-status', '--no-renames', $Base) }
    $changes = [System.Collections.Generic.List[object]]::new()
    foreach ($line in Invoke-MotifGit $RepoRoot $diffArguments) {
        if ($line -match '^(\w)\s+(.+)$') { $changes.Add([pscustomobject]@{ Status = $Matches[1]; Path = $Matches[2] }) }
    }
    if (-not $Head) {
        foreach ($path in Invoke-MotifGit $RepoRoot @('ls-files', '--others', '--exclude-standard')) {
            $changes.Add([pscustomobject]@{ Status = 'A'; Path = $path })
        }
    }
    $selection.Changes = @($changes)

    # Files that changed after the map was built have unreliable coverage, so they are treated as unmapped.
    $drifted = [System.Collections.Generic.HashSet[string]]::new()
    if (-not $IgnoreMapAge -and $selection.MapAge -ge 0 -and $selection.MapAge -le $MaxMapAge) {
        foreach ($path in Invoke-MotifGit $RepoRoot @('diff', '--name-only', '--no-renames', $map.commit, $Base)) { [void] $drifted.Add($path) }
    }

    $fileIndex = @{}
    for ($i = 0; $i -lt $map.files.Count; $i++) { $fileIndex[[string] $map.files[$i]] = $i }
    $classes = $map.classes
    $coverers = @{}                    # file index -> class names, built on demand
    function Get-Coverers([int] $index) {
        if ($coverers.Count -eq 0) {
            foreach ($name in $classes.Keys) {
                foreach ($f in $classes[$name].f) {
                    if (-not $coverers.ContainsKey([int] $f)) { $coverers[[int] $f] = [System.Collections.Generic.List[string]]::new() }
                    $coverers[[int] $f].Add($name)
                }
            }
        }
        if ($coverers.ContainsKey($index)) { return $coverers[$index] }
        return @()
    }
    function Add-Class([string] $name, [string] $reason) {
        if (-not $selection.Classes.Contains($name)) {
            $info = $classes[$name]
            $selection.Classes[$name] = [pscustomobject]@{
                Project = [string] $info.p; Seconds = [double] $info.s; Reasons = [System.Collections.Generic.List[string]]::new()
            }
        }
        $reasons = $selection.Classes[$name].Reasons
        if ($reasons.Count -lt 3 -and -not $reasons.Contains($reason)) { $reasons.Add($reason) }
    }
    function Add-ByPrefix([string] $prefix, [string] $reason) {
        $any = $false
        foreach ($f in $fileIndex.Keys) {
            if (-not $f.StartsWith($prefix, [StringComparison]::Ordinal)) { continue }
            foreach ($name in (Get-Coverers $fileIndex[$f])) { Add-Class $name $reason; $any = $true }
        }
        return $any
    }
    # Entries for classes that are new or edited in a changed test file; they may not be in the map at all.
    $declared = [System.Collections.Generic.List[object]]::new()
    $anyCodeChange = $false

    foreach ($change in $changes) {
        $path = $change.Path
        if ($script:FullRunPatterns | Where-Object { $path -match $_ }) {
            if ($path -notmatch '\.cs$') { Add-Full "$path changes how every test builds or runs"; continue }
        }
        if ($path -match '\.cs$') {
            $anyCodeChange = $true
            $known = $fileIndex.ContainsKey($path) -and -not $drifted.Contains($path)
            if ($known) {
                foreach ($name in (Get-Coverers $fileIndex[$path])) { Add-Class $name "covers $path" }
            }
            if ($path -match '^tests/' -and $change.Status -ne 'D') {
                $text = $null
                $source = if ($Head) { & git -C $RepoRoot show "${Head}:$path" 2>$null } else { Get-Content -LiteralPath (Join-Path $RepoRoot $path) -Raw -ErrorAction SilentlyContinue }
                if ($source) { $text = $source -join "`n" }
                $entries = if ($text) { Get-MotifDeclaredClassEntries $text } else { @() }
                foreach ($entry in $entries) { $declared.Add([pscustomobject]@{ Entry = $entry; Project = $(if ($path -match '^tests/([^/]+)/') { $Matches[1] } else { '' }) }) }
            }
            if ($known) { continue }
            $why = if ($drifted.Contains($path)) { 'changed since the map was built' } else { 'not in the map' }
            if ($path -match '^tests/(SIL\.Motif\.Tests\.Support|Shared)/') {
                Add-Full "$path is shared test infrastructure and $why"
            }
            elseif ($path -match '^tests/(SIL\.Motif\.Tests\.[^/]+)/') {
                if (-not $declared.Count) { Add-Full "$path ($why) declares no test class" }
                else { $selection.Ignored.Add("$path ($why): selected by the classes it declares") }
            }
            elseif ($path -match '^((src|spikes)/[^/]+)/') {
                $project = $Matches[1] + '/'
                if (-not (Add-ByPrefix $project "$path ($why): every class that covers $project")) {
                    Add-Full "$path ($why) and no class covers anything in $project"
                }
            }
            else { Add-Full "$path ($why) is a source file outside src/ and tests/" }
            continue
        }

        # Non-code files: tests read them by name, or the project's code does.
        $name = [IO.Path]::GetFileName($path)
        $stem = $name -replace '\.[^.]+$', ''
        $readers = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($reader in (Find-MotifQuotedMentions $RepoRoot $name)) { [void] $readers.Add($reader) }
        $parent = [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($path))
        if ($parent -and $path.Contains('/') -and $path -notmatch '^(src|tests)/[^/]+/[^/]+$') {
            foreach ($reader in (Find-MotifQuotedMentions $RepoRoot ($parent + '/'))) { [void] $readers.Add($reader) }
            foreach ($reader in (Find-MotifQuotedMentions $RepoRoot ('"' + $parent + '"'))) { [void] $readers.Add($reader) }
        }
        if ($path -match '\.(axaml|resx)$') {
            $owner = "$path.cs"
            if ($fileIndex.ContainsKey($owner)) { [void] $readers.Add($owner) }
            foreach ($reader in (Find-MotifWordMentions $RepoRoot $stem)) { [void] $readers.Add($reader) }
        }
        $matched = $false
        foreach ($reader in $readers) {
            if (-not $fileIndex.ContainsKey($reader)) { continue }
            foreach ($className in (Get-Coverers $fileIndex[$reader])) { Add-Class $className "$path is read by $reader"; $matched = $true }
        }
        if ($matched) { $anyCodeChange = $true; continue }
        if ($script:InertPatterns | Where-Object { $path -match $_ }) { $selection.Ignored.Add("$path is documentation or tooling metadata no test executes"); continue }
        $why = if ($readers.Count -gt 0) { 'is named only by code no mapped test class executes' } else { 'is a non-code input and no source names it' }
        Add-Full "$path $why"
    }

    if ($declared.Count -gt 0) {
        $entriesFile = @($declared)
        foreach ($name in $classes.Keys) {
            foreach ($entry in $entriesFile.Entry) {
                $hit = if ($entry.EndsWith('*')) { $name.StartsWith($entry.Substring(0, $entry.Length - 1), [StringComparison]::Ordinal) } else { $name -eq $entry }
                if ($hit) { Add-Class $name 'declared in a changed test file'; break }
            }
        }
        $selection | Add-Member -NotePropertyName DeclaredEntries -NotePropertyValue $entriesFile
    }
    else { $selection | Add-Member -NotePropertyName DeclaredEntries -NotePropertyValue @() }

    # A class that failed or was skipped when the map was measured has partial coverage, so any code change selects it.
    if ($anyCodeChange) {
        foreach ($name in $classes.Keys) {
            $info = $classes[$name]
            if ($info.ContainsKey('u') -and $info.u -eq 1) { Add-Class $name 'a test failed when its coverage was measured' }
            elseif ($info.t -gt 0 -and $info.k -eq $info.t) { Add-Class $name 'every test was skipped when its coverage was measured' }
        }
    }
    if ($SmokePath -and (Test-Path -LiteralPath $SmokePath)) {
        foreach ($line in Get-Content -LiteralPath $SmokePath) {
            $entry = $line.Trim()
            if (-not $entry -or $entry.StartsWith('#')) { continue }
            if ($classes.Contains($entry)) { Add-Class $entry 'smoke set' }
        }
    }
    if ($anyCodeChange -and $selection.Classes.Count -eq 0) { Add-Full 'a code change selected no test class' }
    return $selection
}

<#
  .SYNOPSIS
  Fails before a long map build when this machine cannot create symbolic links, which New-MotifMapRunDirectory needs.
#>
function Assert-MotifSymlinkSupport {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $Directory)
    New-Item -ItemType Directory -Force -Path $Directory | Out-Null
    $target = Join-Path $Directory "symlink-probe-$([guid]::NewGuid().ToString('N'))"
    $link = $target + '.link'
    Set-Content -LiteralPath $target -Value 'probe'
    try {
        try { [void] (New-Item -ItemType SymbolicLink -Path $link -Target $target -ErrorAction Stop) }
        catch { throw "-BuildMap links test output into per-process folders and cannot create symbolic links here ($($_.Exception.Message)). On Windows turn on Developer Mode (Settings > System > For developers) or run elevated; see docs/testing-changed-files.md." }
    }
    finally {
        Remove-Item -LiteralPath $link, $target -Force -ErrorAction SilentlyContinue
    }
}

<#
  .SYNOPSIS
  A private copy of the test output for one map-build process, and the path of its test assembly.

  .DESCRIPTION
  The coverage collector rewrites the Motif assemblies it instruments in place, so processes that share one folder
  corrupt each other's images. Each process gets its own `tests` folder holding real copies of those assemblies and
  of the JSON files that name them (the host resolves dependencies beside a real copy of deps.json, not a link),
  and symlinks to everything else. Beside it sit the product files the tests start as child processes: the apphosts
  and JSON are real copies, so the host finds the dependencies beside them, and the Motif assemblies are links to the
  instrumented copies, so a child's hits reach the same hit file as the test process's. The folder sits two levels
  under the repository root, as bin/<Configuration> does, because tests find the repository from it.
#>
function New-MotifMapRunDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $ProductDirectory,
        [Parameter(Mandatory)][string] $Destination,
        [Parameter(Mandatory)][string] $Label,
        [Parameter(Mandatory)][string] $Assembly
    )
    $instrumented = '^(SIL\.Motif\..*|motif)\.(dll|pdb)$'
    $root = Join-Path $Destination "map-run-$Label"
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    $testsOut = Join-Path $root 'tests'
    New-Item -ItemType Directory -Force -Path $testsOut | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath (Split-Path -Parent $Assembly) -Force) {
        $target = Join-Path $testsOut $item.Name
        if (-not $item.PSIsContainer -and ($item.Name -match $instrumented -or $item.Extension -eq '.json')) { Copy-Item -LiteralPath $item.FullName -Destination $target }
        else { [void] (New-Item -ItemType SymbolicLink -Path $target -Target $item.FullName) }
    }
    foreach ($item in Get-ChildItem -LiteralPath $ProductDirectory -Force) {
        if ($item.Name -in @('tests', 'test-results')) { continue }
        $target = Join-Path $root $item.Name
        if (-not $item.PSIsContainer -and $item.Name -match $instrumented) {
            $copy = Join-Path $testsOut $item.Name
            if (-not (Test-Path -LiteralPath $copy)) { Copy-Item -LiteralPath $item.FullName -Destination $copy }
            [void] (New-Item -ItemType SymbolicLink -Path $target -Target $copy)
        }
        elseif (-not $item.PSIsContainer -and ($item.Extension -in @('.json', '.exe') -or $item.Name -match '^(motif|SIL\.Motif\.\w+)$')) { Copy-Item -LiteralPath $item.FullName -Destination $target }
        else { [void] (New-Item -ItemType SymbolicLink -Path $target -Target $item.FullName) }
    }
    return (Join-Path $testsOut (Split-Path -Leaf $Assembly))
}

<#
  .SYNOPSIS
  Settings for one test project's map-build processes: the shared template, minus the other test assemblies.
#>
function New-MotifMapRunSettings {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Template,
        [Parameter(Mandatory)][string] $Project,
        [Parameter(Mandatory)][string[]] $TestProjects,
        [Parameter(Mandatory)][string] $OutputPath
    )
    # A process loads only its own project and Support, so instrumenting the others is wasted start-up time.
    $others = @($TestProjects | Where-Object { $_ -ne $Project } | ForEach-Object { "[$_]*" })
    $text = Get-Content -LiteralPath $Template -Raw
    $text = $text -replace '<Exclude>([^<]*)</Exclude>', ('<Exclude>$1,' + ($others -join ',') + '</Exclude>')
    Set-Content -LiteralPath $OutputPath -Value $text -Encoding utf8
    return $OutputPath
}

<#
  .SYNOPSIS
  Turns a selection into per-project class lists, shard counts, the artifacts to prepare and a time estimate.

  .DESCRIPTION
  Seconds come from the shard-weights file (an ordinary run's class durations) and from the map where a class is
  not in it. The wall-clock estimate deals each project's classes into shards of about ShardTargetSeconds, runs the
  shards on Slots processes longest first, and adds ProcessOverheadSeconds per wave of process start-up.
#>
function Get-MotifChangedPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Selection,
        [string] $WeightsPath,
        [int] $Slots = 12,
        [double] $ShardTargetSeconds = 20,
        [double] $ProcessOverheadSeconds = 8
    )

    $plan = [pscustomobject]@{
        Projects = @{}; SelectionEntries = @(); ClassCount = 0; Seconds = 0.0; EstimatedSeconds = 0.0
        NeedsPortable = $false; NeedsExplainedWordCard = $false
    }
    if ($Selection.Full) { return $plan }
    $weights = @{}
    if ($WeightsPath -and (Test-Path -LiteralPath $WeightsPath)) {
        $weights = Get-Content -LiteralPath $WeightsPath -Raw | ConvertFrom-Json -AsHashtable
    }
    $entries = [System.Collections.Generic.List[string]]::new()
    foreach ($name in $Selection.Classes.Keys) {
        $info = $Selection.Classes[$name]
        $seconds = if ($weights.ContainsKey($name)) { [double] $weights[$name] } else { [double] $info.Seconds }
        $project = $info.Project
        if (-not $plan.Projects.ContainsKey($project)) { $plan.Projects[$project] = [pscustomobject]@{ Classes = [System.Collections.Generic.List[double]]::new(); Seconds = 0.0; Shards = 1 } }
        $plan.Projects[$project].Classes.Add($seconds)
        $plan.Projects[$project].Seconds += $seconds
        $entries.Add($name)
        if ($name -match 'PortableWorkerPackageTests') { $plan.NeedsPortable = $true }
        if ($name -match 'ExplainedWordCard') { $plan.NeedsExplainedWordCard = $true }
    }
    foreach ($declared in @($Selection.DeclaredEntries)) {
        $entries.Add($declared.Entry)
        if ($declared.Project -and -not $plan.Projects.ContainsKey($declared.Project)) {
            $plan.Projects[$declared.Project] = [pscustomobject]@{ Classes = [System.Collections.Generic.List[double]]::new(); Seconds = 0.0; Shards = 1 }
            $plan.Projects[$declared.Project].Classes.Add(1.0)
            $plan.Projects[$declared.Project].Seconds += 1.0
        }
        if ($declared.Entry -match 'PortableWorkerPackageTests') { $plan.NeedsPortable = $true }
        if ($declared.Entry -match 'ExplainedWordCard') { $plan.NeedsExplainedWordCard = $true }
    }
    $plan.SelectionEntries = @($entries | Select-Object -Unique)
    $plan.ClassCount = $Selection.Classes.Count

    $loads = [System.Collections.Generic.List[double]]::new()
    foreach ($project in $plan.Projects.Values) {
        $project.Shards = [Math]::Max(1, [Math]::Min($project.Classes.Count, [int][Math]::Ceiling($project.Seconds / $ShardTargetSeconds)))
        $shardLoads = [double[]]::new($project.Shards)
        foreach ($seconds in ($project.Classes | Sort-Object -Descending)) {
            $lightest = 0
            for ($i = 1; $i -lt $shardLoads.Length; $i++) { if ($shardLoads[$i] -lt $shardLoads[$lightest]) { $lightest = $i } }
            $shardLoads[$lightest] += $seconds
        }
        foreach ($load in $shardLoads) { $loads.Add($load + $ProcessOverheadSeconds) }
        $plan.Seconds += $project.Seconds
    }
    $slotLoads = [double[]]::new([Math]::Max(1, $Slots))
    foreach ($load in ($loads | Sort-Object -Descending)) {
        $lightest = 0
        for ($i = 1; $i -lt $slotLoads.Length; $i++) { if ($slotLoads[$i] -lt $slotLoads[$lightest]) { $lightest = $i } }
        $slotLoads[$lightest] += $load
    }
    $plan.EstimatedSeconds = [Math]::Round(($slotLoads | Measure-Object -Maximum).Maximum, 0)
    return $plan
}

function Write-MotifChangedSelection {
    [CmdletBinding()]
    param([Parameter(Mandatory)] $Selection, [Parameter(Mandatory)] $Plan)

    Write-Host ''
    Write-Host "==> changed-files selection (base $($Selection.Base), map $(if ($Selection.MapCommit) { $Selection.MapCommit.Substring(0, 9) } else { 'missing' }), map age $($Selection.MapAge) commits)" -ForegroundColor Cyan
    foreach ($change in $Selection.Changes | Select-Object -First 40) { Write-Host ("  {0} {1}" -f $change.Status, $change.Path) }
    if (@($Selection.Changes).Count -gt 40) { Write-Host "  ... and $(@($Selection.Changes).Count - 40) more changed files" }
    foreach ($note in $Selection.Ignored) { Write-Host "  ignored: $note" -ForegroundColor DarkGray }
    if ($Selection.Full) {
        foreach ($reason in $Selection.FullReasons) { Write-Host "  FULL SUITE: $reason" -ForegroundColor Yellow }
        return
    }
    foreach ($name in ($Selection.Classes.Keys | Sort-Object)) {
        $info = $Selection.Classes[$name]
        Write-Host ('  {0,-8} {1}  [{2}]' -f ([string] [Math]::Round($info.Seconds, 1) + 's'), $name, ($info.Reasons -join '; '))
    }
    foreach ($declared in @($Selection.DeclaredEntries | Where-Object { $_.Entry -notmatch '\*$' })) {
        Write-Host ("  declared in a changed test file: {0}" -f $declared.Entry)
    }
    Write-Host ('  {0} classes, {1:N0} test-seconds, about {2:N0} s wall with {3} projects' -f
        $Selection.Classes.Count, $Plan.Seconds, $Plan.EstimatedSeconds, $Plan.Projects.Count)
}

Export-ModuleMember -Function Assert-MotifSymlinkSupport, Merge-MotifCoverageMap, Select-MotifChangedTests, Read-MotifCoverageMap, New-MotifMapRunSettings, Get-MotifChangedPlan, Write-MotifChangedSelection, New-MotifMapRunDirectory
