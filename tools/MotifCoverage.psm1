Set-StrictMode -Version Latest

<#
  .SYNOPSIS
  Line coverage for test.ps1 -Coverage: fetch the collector, then fold the Cobertura reports into one summary.

  .DESCRIPTION
  The collector is never referenced by a project. Install-MotifCoverageCollector restores a stub project so
  coverlet.collector sits in the shared NuGet packages folder, and test.ps1 hands its folder to `dotnet test`
  as the test adapter path. Merge-MotifCoverage reads every coverage.cobertura.xml a run wrote and keeps, for
  each source line, the highest hit count any process saw, because a sharded project covers a line in one shard.
#>
function Install-MotifCoverageCollector {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $RepoRoot)

    $stub = Join-Path $RepoRoot 'tools/coverage/CoverletCollector.csproj'
    & dotnet restore $stub --verbosity quiet | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Restoring the coverage collector failed.' }
    [xml] $props = Get-Content (Join-Path $RepoRoot 'SilVersions.props') -Raw
    $version = $props.Project.PropertyGroup.CoverletCollectorVersion
    $packages = $env:NUGET_PACKAGES
    $adapter = Join-Path $packages "coverlet.collector/$version/build/netstandard2.0"
    if (-not (Test-Path -LiteralPath (Join-Path $adapter 'coverlet.collector.dll'))) {
        throw "coverlet.collector $version is not in $packages after restore."
    }
    return $adapter
}

function Merge-MotifCoverage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $ResultsDirectory,
        [Parameter(Mandatory)][string] $OutputDirectory
    )

    # Assembly -> "file|line" -> hits.
    $hits = @{}
    $reports = @(Get-ChildItem $ResultsDirectory -Filter 'coverage.cobertura.xml' -Recurse -ErrorAction SilentlyContinue)
    foreach ($report in $reports) {
        $reader = [Xml.XmlReader]::Create($report.FullName, [Xml.XmlReaderSettings]@{ DtdProcessing = 'Ignore' })
        try {
            $assembly = $null
            $file = $null
            while ($reader.Read()) {
                if ($reader.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
                switch ($reader.Name) {
                    'package' {
                        $assembly = $reader.GetAttribute('name')
                        if (-not $hits.ContainsKey($assembly)) { $hits[$assembly] = @{} }
                    }
                    'class' { $file = $reader.GetAttribute('filename') }
                    'line' {
                        $key = "$file|$($reader.GetAttribute('number'))"
                        $count = [long] $reader.GetAttribute('hits')
                        $lines = $hits[$assembly]
                        if (-not $lines.ContainsKey($key) -or $lines[$key] -lt $count) { $lines[$key] = $count }
                    }
                }
            }
        }
        finally { $reader.Dispose() }
    }

    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $rows = @(foreach ($assembly in ($hits.Keys | Sort-Object)) {
        $lines = $hits[$assembly]
        [pscustomobject]@{
            Assembly = $assembly
            Covered = @($lines.Values | Where-Object { $_ -gt 0 }).Count
            Total = $lines.Count
        }
    })

    # Parsimony and Retirement live in several assemblies, so the subtotal is by source folder.
    $parsimonyCovered = 0
    $parsimonyTotal = 0
    foreach ($assembly in $hits.Keys) {
        foreach ($entry in $hits[$assembly].GetEnumerator()) {
            $path = $entry.Key.Substring(0, $entry.Key.LastIndexOf('|')).Replace('\', '/')
            if ($path -notmatch '/(Parsimony|Retirement)/') { continue }
            $parsimonyTotal++
            if ($entry.Value -gt 0) { $parsimonyCovered++ }
        }
    }

    function Percent([long] $covered, [long] $total) {
        if ($total -eq 0) { return 0.0 }
        return [Math]::Round(100.0 * $covered / $total, 1)
    }
    $summary = [pscustomobject]@{
        reports = $reports.Count
        assemblies = @($rows | ForEach-Object {
            [pscustomobject]@{ name = $_.Assembly; covered = $_.Covered; total = $_.Total
                percent = (Percent $_.Covered $_.Total) } })
        parsimony = [pscustomobject]@{ covered = $parsimonyCovered; total = $parsimonyTotal
            percent = (Percent $parsimonyCovered $parsimonyTotal) }
    }
    $builder = [Text.StringBuilder]::new()
    [void] $builder.Append("# Line coverage`n`n")
    [void] $builder.Append("Merged from $($reports.Count) Cobertura reports; a line counts as covered when any process hit it.`n`n")
    [void] $builder.Append("| Assembly | Covered | Lines | Line coverage |`n|---|---:|---:|---:|`n")
    foreach ($row in $summary.assemblies) {
        [void] $builder.Append("| $($row.name) | $($row.covered) | $($row.total) | $($row.percent)% |`n")
    }
    [void] $builder.Append("| **Parsimony and Retirement folders** | $parsimonyCovered | $parsimonyTotal | " +
        "$($summary.parsimony.percent)% |`n")
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'coverage-summary.md'), $builder.ToString(),
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'coverage-summary.json'),
        ($summary | ConvertTo-Json -Depth 5) + "`n", [Text.UTF8Encoding]::new($false))
    return $summary
}

Export-ModuleMember -Function Install-MotifCoverageCollector, Merge-MotifCoverage
