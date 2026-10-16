function Get-MotifPackageVersion {
    param(
        [Parameter(Mandatory)][string] $ProjectPath,
        [Parameter(Mandatory)][string] $PackageId
    )

    if (-not (Test-Path -LiteralPath $ProjectPath)) { return $null }

    $metadata = @(& dotnet msbuild $ProjectPath -getItem:PackageReference -nologo 2>$null)
    if ($LASTEXITCODE -ne 0) { return $null }

    try { $evaluation = ($metadata -join [Environment]::NewLine) | ConvertFrom-Json }
    catch { return $null }

    foreach ($package in @($evaluation.Items.PackageReference)) {
        if ($package.Identity -eq $PackageId -and -not [string]::IsNullOrWhiteSpace($package.Version)) {
            return [string]$package.Version
        }
    }

    return $null
}

<#
  .SYNOPSIS
  The MSBuild node count a restore or build passes as -m:N, or 0 for no cap.

  .DESCRIPTION
  MOTIF_BUILD_NODES, a positive whole number, wins. Otherwise CI (CI set to true, 1 or yes, any case) gets
  no cap, because a CI runner is one build on its own machine. Otherwise a developer machine gets a quarter
  of its processors, at least two: a dozen worktrees building at once would each start one node per
  processor and starve one another.
#>
function Get-MotifBuildNodeLimit {
    [CmdletBinding()]
    param([int] $ProcessorCount = [Environment]::ProcessorCount)

    $nodes = 0
    if ([int]::TryParse([string] $env:MOTIF_BUILD_NODES, [ref] $nodes) -and $nodes -gt 0) { return $nodes }
    if ($env:CI -match '^(?i:true|1|yes)$') { return 0 }
    return [Math]::Max(2, [int][Math]::Floor($ProcessorCount / 4))
}

function Invoke-MotifRestore {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Target,
        [string] $ConfigFile,
        [int] $MaxNodes = 0
    )

    $arguments = @('restore', $Target, '--ignore-failed-sources', '--verbosity', 'minimal')
    if ($MaxNodes -gt 0) { $arguments += "-m:$MaxNodes" }
    if (-not [string]::IsNullOrWhiteSpace($ConfigFile)) {
        $arguments += @('--configfile', $ConfigFile)
    }

    $restoreOutput = @(& dotnet @arguments 2>&1 | ForEach-Object { [string]$_ })
    $restoreExitCode = $LASTEXITCODE
    $friendlyMessages = [System.Collections.Generic.List[string]]::new()
    $reportedPackages = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    foreach ($line in $restoreOutput) {
        if ($line -notmatch '(?i)\berror\s+NU1101:\s+Unable to find package\s+(?<id>\S+?)\.\s+No packages exist') { continue }

        $packageId = $Matches.id
        $projectPath = $Target
        if ($line -match '^(?<project>.+?\.csproj)\s*:\s*error\s+NU1101:') {
            $projectPath = $Matches.project
        }
        $version = Get-MotifPackageVersion -ProjectPath $projectPath -PackageId $packageId
        $messageKey = if ($version) { "$packageId $version" } else { $packageId }
        if (-not $reportedPackages.Add($messageKey)) { continue }

        if ($version) {
            $friendlyMessages.Add("Package $packageId $version is not in the local cache; restore with network once.")
        }
        else {
            $friendlyMessages.Add("Package $packageId is not in the local cache; restore with network once.")
        }
    }

    [pscustomobject]@{
        ExitCode = $restoreExitCode
        Output = @($restoreOutput) + @($friendlyMessages)
    }
}

Export-ModuleMember -Function Invoke-MotifRestore, Get-MotifBuildNodeLimit
