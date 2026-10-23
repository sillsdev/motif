<#
.SYNOPSIS
Summarizes a Motif.Ui JSON-lines trace (MOTIF_TRACE=1, MOTIF_TRACE_FILE=<path>) as p50/p95/max milliseconds.
.DESCRIPTION
Prints one table per label for the view.switch root spans by (from, to), then one table per span name, then the mean of each n.* counter per switch.
#>
param([Parameter(Mandatory)][string]$Path, [string]$Label)
$ErrorActionPreference = 'Stop'
function Get-Percentile($sorted, [double]$p) {
    if ($sorted.Count -eq 0) { return 0 }
    $index = [math]::Min($sorted.Count - 1, [math]::Ceiling($p * $sorted.Count) - 1)
    [math]::Round($sorted[[math]::Max(0, $index)], 1)
}
function Get-Stats($group) {
    $values = @($group.Group | ForEach-Object { [double]$_.ms } | Sort-Object)
    [pscustomobject]@{ Count = $values.Count; P50 = Get-Percentile $values 0.5; P95 = Get-Percentile $values 0.95; Max = [math]::Round($values[-1], 1) }
}
$spans = @(Get-Content -LiteralPath $Path | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
if ($Label) { $spans = @($spans | Where-Object { $_.label -eq $Label }) }
$labelGroups = @($spans | Group-Object {
    $spanLabel = [string]$_.label
    if ([string]::IsNullOrWhiteSpace($spanLabel)) { 'unlabelled' } else { "label:$spanLabel" }
})
foreach ($labelGroup in $labelGroups) {
    $current = if ($labelGroup.Name -eq 'unlabelled') { 'unlabelled' } else { $labelGroup.Name.Substring(6) }
    $inLabel = $labelGroup.Group
    Write-Host "`n== $current : view.switch by (from -> to), ms"
    $inLabel | Where-Object name -eq 'view.switch' | Group-Object { "$($_.from) -> $($_.to)" } | ForEach-Object {
        $s = Get-Stats $_; [pscustomobject]@{ Switch = $_.Name; N = $s.Count; P50 = $s.P50; P95 = $s.P95; Max = $s.Max }
    } | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
    Write-Host "== $current : view.switch by destination, ms"
    $inLabel | Where-Object name -eq 'view.switch' | Group-Object { "$($_.kind) $($_.to)" } | ForEach-Object {
        $s = Get-Stats $_; [pscustomobject]@{ To = $_.Name; N = $s.Count; P50 = $s.P50; P95 = $s.P95; Max = $s.Max }
    } | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
    Write-Host "== $current : per span name, ms"
    $inLabel | Group-Object name | ForEach-Object {
        $s = Get-Stats $_; [pscustomobject]@{ Span = $_.Name; N = $s.Count; P50 = $s.P50; P95 = $s.P95; Max = $s.Max }
    } | Sort-Object P95 -Descending | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
    Write-Host "== $current : mean counter per switch (n.*), by destination"
    $inLabel | Where-Object name -eq 'view.switch' | Group-Object to | ForEach-Object {
        $row = [ordered]@{ To = $_.Name; N = $_.Count }
        foreach ($key in ($_.Group | ForEach-Object { $_.PSObject.Properties.Name } | Where-Object { $_ -like 'n.*' } | Sort-Object -Unique)) {
            $row[$key] = [math]::Round((($_.Group | ForEach-Object { [double]($_.$key) } | Measure-Object -Sum).Sum) / $_.Count, 1)
        }
        [pscustomobject]$row
    } | Format-Table -AutoSize | Out-String -Width 250 | Write-Host
}
