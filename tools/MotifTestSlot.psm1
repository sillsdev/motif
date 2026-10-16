Set-StrictMode -Version Latest

<#
  .SYNOPSIS
  The number of machine-wide test slots this run uses; 0 means it takes none.

  .DESCRIPTION
  MOTIF_TEST_SLOTS, a positive whole number, is that many slots. 0 means none, explicitly: an outside
  scheduler already admitted this run. Unset or empty means no slot on CI, where a runner holds one build,
  and a fifth of the processors, at least two, on a developer machine.
#>
function Get-MotifTestSlotCount {
    [CmdletBinding()]
    param([int] $ProcessorCount = [Environment]::ProcessorCount)

    $slots = 0
    if ([int]::TryParse([string] $env:MOTIF_TEST_SLOTS, [ref] $slots)) { return [Math]::Max(0, $slots) }
    if ($env:CI -match '^(?i:true|1|yes)$') { return 0 }
    return [Math]::Max(2, [int][Math]::Floor($ProcessorCount / 5))
}

<#
  .SYNOPSIS
  The folder that holds the machine-wide slot files, resolved before anything relocates TEMP.

  .DESCRIPTION
  MOTIF_TEST_SLOT_DIR wins. Otherwise Windows uses motif-test-slots in the temporary folder as it is at call
  time, so the caller must ask before it moves TEMP into a checkout. Elsewhere it is /tmp/motif-test-slots
  whatever TMPDIR says, so an agent with a private TMPDIR still shares slots with every other run.
#>
function Get-MotifTestSlotDirectory {
    [CmdletBinding()]
    param([string] $TempPath = [IO.Path]::GetTempPath())

    if ($env:MOTIF_TEST_SLOT_DIR) { return $env:MOTIF_TEST_SLOT_DIR }
    if ($IsWindows) { return Join-Path $TempPath 'motif-test-slots' }
    return '/tmp/motif-test-slots'
}

<#
  .SYNOPSIS
  Wait for one of the machine-wide test slots that Get-MotifTestSlotCount sizes.

  .DESCRIPTION
  Each test run sizes its own concurrency for the processors it sees, but nothing stops ten worktrees on
  one machine from running ten suites at once. That oversubscribes the processors, slows every suite
  several times over and makes timing-sensitive tests flaky. So a developer-machine run takes one of a
  few slot files in the -Directory folder (Get-MotifTestSlotDirectory by default)
  before it starts its tests, and the others wait. The slot is an exclusively opened file, so it frees
  itself when the run exits, however it exits.

  MOTIF_TEST_SLOTS overrides the count; 0 turns the wait off. CI takes no slot unless it sets a count.

  .OUTPUTS
  The open slot, which the caller keeps until its tests finish; $null when the count is 0.
#>
function Enter-MotifTestSlot {
    [CmdletBinding()]
    param([string] $Directory)

    $slots = Get-MotifTestSlotCount
    if ($slots -le 0) { return $null }
    $directory = if ($Directory) { $Directory } else { Get-MotifTestSlotDirectory }
    try { [IO.Directory]::CreateDirectory($directory) | Out-Null }
    catch {
        Write-Warning "test slots unavailable ($($_.Exception.Message)); running without one"
        return $null
    }

    $announced = $false
    $started = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        for ($index = 1; $index -le $slots; $index++) {
            $path = Join-Path $directory "slot-$index.lock"
            try {
                $slot = [IO.File]::Open($path, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
                if ($announced) { Write-Host "Took test slot $index of $slots after $([int]$started.Elapsed.TotalSeconds) s." }
                return $slot
            }
            catch [IO.IOException] { }
            catch {
                Write-Warning "test slots unavailable ($($_.Exception.Message)); running without one"
                return $null
            }
        }
        if (-not $announced) {
            Write-Host "All $slots test slots in $directory are taken; waiting for one (MOTIF_TEST_SLOTS, 0 turns the wait off)." -ForegroundColor Yellow
            $announced = $true
        }
        Start-Sleep -Seconds 5
    }
}

Export-ModuleMember -Function Enter-MotifTestSlot, Get-MotifTestSlotCount, Get-MotifTestSlotDirectory
