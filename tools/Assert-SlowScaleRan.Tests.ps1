Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The required slow-scale list must name only tests that exist and are slow-gated.
$script = Join-Path $PSScriptRoot 'Assert-SlowScaleRan.ps1'
function Get-Failure([scriptblock] $Action) { try { & $Action | Out-Null } catch { return $_.Exception.Message }; return $null }

$real = Get-Failure { & $script -ListOnly }
if ($real) { throw "The list does not match the repository's tests: $real" }

$root = Join-Path ([IO.Path]::GetTempPath()) ("slow-list-" + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $root | Out-Null
    $failure = Get-Failure { & $script -ListOnly -TestsRoot $root }
    if (-not $failure -or $failure -notmatch 'TextsProposalChangesMemoryTests\.A.* \(no such test\)') {
        throw "Tests missing from the sources were not named: $failure"
    }

    # A test that exists without the trait is reported separately from one that is gone.
    Set-Content (Join-Path $root 'T.cs') @'
public sealed class ProgressiveDisplayTests
{
    [Fact]
    public void RecentProjectMenuReachesTheLastOfTwoThousandProjects() { }
}
'@
    $failure = Get-Failure { & $script -ListOnly -TestsRoot $root }
    if ($failure -notmatch 'RecentProjectMenuReachesTheLastOfTwoThousandProjects \(not marked Category=SlowScale\)') {
        throw "An untagged test was not named: $failure"
    }
    if ($failure -match 'RecentProjectMenuReachesTheLastOfTwoThousandProjects \(no such test\)') { throw 'An existing test was called missing.' }
} finally { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
Write-Output 'Assert-SlowScaleRan list regression passed.'
