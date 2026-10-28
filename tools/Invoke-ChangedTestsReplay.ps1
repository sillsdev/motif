<#
  .SYNOPSIS
  Replays -Changed selection over the last commits of a branch and tabulates what each would have run.

  .DESCRIPTION
  For each of the last Count first-parent commits, selects with the commit's parent as the base and the commit as
  the head, using one coverage map. The map's age is ignored, because a replay map is rarely at the commit's parent.
  Prints a table and, with -Json, writes the rows. Nothing is built or run.
#>
[CmdletBinding()]
param(
    [int] $Count = 20,
    [string] $Ref = 'HEAD',
    [string] $MapPath,
    [string] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'MotifChangedTests.psm1') -Force
if (-not $MapPath) { $MapPath = Join-Path $repoRoot 'bin/.cache/test-map/coverage-map.json' }
$weights = Join-Path $repoRoot 'tests/test-shard-weights.json'
$smoke = Join-Path $repoRoot 'tests/test-smoke-classes.txt'

$rows = foreach ($commit in (& git -C $repoRoot rev-list --first-parent -n $Count $Ref)) {
    $subject = (& git -C $repoRoot log -1 --format=%s $commit)
    $selection = Select-MotifChangedTests -RepoRoot $repoRoot -MapPath $MapPath -Base "$commit^" -Head $commit -IgnoreMapAge -SmokePath $smoke
    $plan = Get-MotifChangedPlan -Selection $selection -WeightsPath $weights
    [pscustomobject]@{
        Commit = $commit.Substring(0, 9)
        Files = @($selection.Changes).Count
        Full = $selection.Full
        Classes = if ($selection.Full) { $null } else { $selection.Classes.Count }
        TestSeconds = if ($selection.Full) { $null } else { [Math]::Round($plan.Seconds) }
        EstimatedSeconds = if ($selection.Full) { $null } else { $plan.EstimatedSeconds }
        Reason = if ($selection.Full) { ($selection.FullReasons | Select-Object -First 1) } else { '' }
        Subject = $subject.Substring(0, [Math]::Min(70, $subject.Length))
    }
}
$rows | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Host
if ($Json) { $rows | ConvertTo-Json | Set-Content -LiteralPath $Json -Encoding utf8 }
