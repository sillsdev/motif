Set-StrictMode -Version Latest

<#
  .SYNOPSIS
  Wait for one of a fixed number of machine-wide test slots, when MOTIF_TEST_SLOTS asks for a limit.

  .DESCRIPTION
  Each test run sizes its own concurrency for the processors it sees, but nothing stops ten worktrees on
  one machine from running ten suites at once. That oversubscribes the processors, slows every suite
  several times over and makes timing-sensitive tests flaky. With MOTIF_TEST_SLOTS set to a positive
  number, a run takes one of that many slot files in MOTIF_TEST_SLOT_DIR (the system temporary folder's
  motif-test-slots by default) before it starts its tests, and the others wait. The slot is an
  exclusively opened file, so it frees itself when the run exits, however it exits.

  Unset, nothing waits: CI and a single developer run are unchanged.

  .OUTPUTS
  The open slot, which the caller keeps until its tests finish; $null when no limit is set.
#>
function Enter-MotifTestSlot {
    [CmdletBinding()]
    param()

    $slots = 0
    if (-not [int]::TryParse([string] $env:MOTIF_TEST_SLOTS, [ref] $slots) -or $slots -le 0) { return $null }
    $directory = if ($env:MOTIF_TEST_SLOT_DIR) { $env:MOTIF_TEST_SLOT_DIR } else {
        Join-Path ([IO.Path]::GetTempPath()) 'motif-test-slots' }
    [IO.Directory]::CreateDirectory($directory) | Out-Null

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
        }
        if (-not $announced) {
            Write-Host "All $slots test slots in $directory are taken; waiting for one (MOTIF_TEST_SLOTS)." -ForegroundColor Yellow
            $announced = $true
        }
        Start-Sleep -Seconds 5
    }
}

Export-ModuleMember -Function Enter-MotifTestSlot
