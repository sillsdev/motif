[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$lock = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'test-grammars.lock.json') -Raw | ConvertFrom-Json
$commit = ([string]$lock.commit).ToLowerInvariant()
if ($commit -notmatch '^[0-9a-f]{40}$') { throw 'The test-grammars lock must contain a full commit hash.' }
if ([string]::IsNullOrWhiteSpace([string]$lock.tag) -or
    [string]::IsNullOrWhiteSpace([string]$lock.repository)) {
    throw 'The test-grammars lock must contain a repository URL and tag.'
}

function Invoke-Git([string] $WorkingDirectory, [string[]] $Arguments) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new((Get-Command git).Source)
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    if ($WorkingDirectory) { $startInfo.WorkingDirectory = $WorkingDirectory }
    foreach ($argument in $Arguments) { [void]$startInfo.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw "Could not start git $($Arguments -join ' ')." }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(600000)) {
        $process.Kill($true)
        $process.WaitForExit()
        throw "git $($Arguments -join ' ') exceeded ten minutes."
    }
    $stdout = $stdoutTask.GetAwaiter().GetResult().Trim()
    $stderr = $stderrTask.GetAwaiter().GetResult().Trim()
    if ($process.ExitCode -ne 0) { throw "git $($Arguments -join ' ') failed: $stderr`n$stdout" }
    return $stdout
}

function Assert-PinnedCheckout([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Test-grammars checkout not found: $Path"
    }
    $actual = Invoke-Git $Path @('rev-parse', '--verify', 'HEAD')
    if ($actual -ne $commit) {
        throw "Test-grammars checkout has commit $actual; expected $commit from tag $($lock.tag)."
    }
    $setRoot = Join-Path $Path 'evals/sets'
    if (-not (Test-Path -LiteralPath $setRoot -PathType Container)) {
        throw "Test-grammars checkout has no evals/sets directory: $Path"
    }
    return [IO.Path]::GetFullPath($Path)
}

if (-not [string]::IsNullOrWhiteSpace($env:MOTIF_TEST_GRAMMARS)) {
    $checkout = Assert-PinnedCheckout ([IO.Path]::GetFullPath($env:MOTIF_TEST_GRAMMARS))
    Write-Output $checkout
    return
}

$cacheRoot = Join-Path $repositoryRoot 'bin/.cache/test-grammars'
$checkoutPath = Join-Path $cacheRoot $commit
if (Test-Path -LiteralPath $checkoutPath -PathType Container) {
    $checkout = Assert-PinnedCheckout $checkoutPath
    Write-Output $checkout
    return
}

New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null
$temporaryPath = Join-Path $cacheRoot ('.fetch-' + [Guid]::NewGuid().ToString('N'))
try {
    [void](Invoke-Git '' @(
        'clone', '--quiet', '--depth', '1', '--branch', [string]$lock.tag,
        '--single-branch', [string]$lock.repository, $temporaryPath
    ))
    $checkout = Assert-PinnedCheckout $temporaryPath
    try {
        [IO.Directory]::Move($temporaryPath, $checkoutPath)
        $checkout = Assert-PinnedCheckout $checkoutPath
    }
    catch {
        if (-not (Test-Path -LiteralPath $checkoutPath -PathType Container)) { throw }
        Remove-Item -LiteralPath $temporaryPath -Recurse -Force
        $checkout = Assert-PinnedCheckout $checkoutPath
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryPath -PathType Container) {
        Remove-Item -LiteralPath $temporaryPath -Recurse -Force
    }
}

Write-Output $checkout
