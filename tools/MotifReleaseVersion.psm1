Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-MotifSemanticVersionParts {
    param([Parameter(Mandatory)][string] $Version)

    $number = '(?:0|[1-9][0-9]*)'
    $label = '(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
    $pattern = '\A(?<core>' + $number + '\.' + $number + '\.' + $number +
        ')(?:-(?<suffix>' + $label + '(?:\.' + $label + ')*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z'
    $match = [regex]::Match($Version, $pattern)
    $core = $null
    if (-not $match.Success -or -not [Version]::TryParse($match.Groups['core'].Value, [ref] $core)) {
        throw 'The product version must be a three-part SemVer value, such as 0.2.0-beta001.'
    }
    [pscustomobject]@{ Core = $core; Suffix = $match.Groups['suffix'].Value }
}

function Get-MotifReleaseVersion {
    param([Parameter(Mandatory)][string] $Version)

    $parts = Get-MotifSemanticVersionParts -Version $Version
    $suffix = $parts.Suffix
    if ($suffix.Length -eq 0) {
        $next = [Version]::new($parts.Core.Major, $parts.Core.Minor, $parts.Core.Build + 1).ToString(3)
    }
    elseif ($suffix -match '(?<prefix>.*?)(?<sequence>[0-9]+)\z') {
        $sequence = $Matches['sequence']
        $prefix = $Matches['prefix']
        $incremented = ([System.Numerics.BigInteger]::Parse($sequence) + 1).ToString()
        if ($suffix.Split('.')[-1] -notmatch '^[0-9]+$' -and $incremented.Length -gt $sequence.Length) {
            throw 'The prerelease sequence width is exhausted; advance the numeric product version.'
        }
        $next = $parts.Core.ToString(3) + '-' + $prefix + $incremented.PadLeft($sequence.Length, '0')
    }
    else {
        $next = $parts.Core.ToString(3) + '-' + $suffix + '.1'
    }
    [pscustomobject]@{ ProductVersion = $Version; NextProductVersion = $next }
}

Export-ModuleMember -Function Get-MotifSemanticVersionParts, Get-MotifReleaseVersion
