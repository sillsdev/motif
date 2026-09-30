Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-CurrentPanGlossRuntimeIdentifier {
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    if ([OperatingSystem]::IsWindows() -and $architecture -eq [System.Runtime.InteropServices.Architecture]::X64) {
        return 'win-x64'
    }
    if ([OperatingSystem]::IsLinux() -and $architecture -eq [System.Runtime.InteropServices.Architecture]::X64) {
        return 'linux-x64'
    }
    if ([OperatingSystem]::IsMacOS() -and $architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
        return 'osx-arm64'
    }
    if ([OperatingSystem]::IsMacOS() -and $architecture -eq [System.Runtime.InteropServices.Architecture]::X64) {
        return 'osx-x64'
    }
    throw "Cannot select a supported PanGloss RID for $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription) $architecture."
}

function Get-PinnedPanGlossArtifact {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $RepositoryRoot,

        [string] $RuntimeIdentifier,

        [string] $ArtifactPath,

        [string] $CacheDirectory
    )

    if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $RuntimeIdentifier = Get-CurrentPanGlossRuntimeIdentifier
    }
    if ($RuntimeIdentifier -notin @('win-x64', 'linux-x64', 'osx-arm64', 'osx-x64')) {
        throw "Unsupported runtime identifier '$RuntimeIdentifier'."
    }

    $pinPath = Join-Path $RepositoryRoot 'pangloss-release.json'
    $pin = Get-Content -LiteralPath $pinPath -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string] $pin.version) -or
        [string]::IsNullOrWhiteSpace([string] $pin.tag)) {
        throw 'pangloss-release.json must declare a version and tag.'
    }
    $assetProperty = $pin.assets.PSObject.Properties[$RuntimeIdentifier]
    if ($null -eq $assetProperty) {
        throw "No PanGloss build is pinned for $RuntimeIdentifier."
    }
    $asset = $assetProperty.Value
    if ([string] $asset.sha256 -cnotmatch '^[0-9a-f]{64}$') {
        throw 'pangloss-release.json does not carry a lowercase SHA-256.'
    }
    $assetUri = [Uri] [string] $asset.url
    if ($assetUri.Scheme -ne 'https' -or
        [string]::IsNullOrWhiteSpace([System.IO.Path]::GetFileName($assetUri.AbsolutePath))) {
        throw "PanGloss pin for $RuntimeIdentifier has an invalid HTTPS asset URL."
    }

    if ([string]::IsNullOrWhiteSpace($ArtifactPath)) {
        if ([string]::IsNullOrWhiteSpace($CacheDirectory)) {
            $CacheDirectory = Join-Path $RepositoryRoot '.tmp/pangloss'
        }
        $downloadDirectory = Join-Path $CacheDirectory "$($pin.tag)/$RuntimeIdentifier"
        $ArtifactPath = Join-Path $downloadDirectory ([System.IO.Path]::GetFileName($assetUri.AbsolutePath))
        if (-not (Test-Path -LiteralPath $ArtifactPath -PathType Leaf)) {
            New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null
            $temporaryPath = $ArtifactPath + '.download-' + [Guid]::NewGuid().ToString('N')
            try {
                Invoke-WebRequest -Uri $assetUri -OutFile $temporaryPath
                Move-Item -LiteralPath $temporaryPath -Destination $ArtifactPath
            }
            finally {
                if (Test-Path -LiteralPath $temporaryPath) {
                    Remove-Item -LiteralPath $temporaryPath -Force
                }
            }
        }
    }

    $artifact = Get-Item -LiteralPath $ArtifactPath -ErrorAction Stop
    if ($artifact.PSIsContainer) {
        throw "PanGloss artifact is not a file: $($artifact.FullName)"
    }
    if ($artifact.Length -eq 0) {
        throw "PanGloss artifact is empty: $($artifact.FullName)"
    }
    $actualHash = (Get-FileHash -LiteralPath $artifact.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -cne [string] $asset.sha256) {
        throw "PanGloss artifact is not the pinned $($pin.tag) for $RuntimeIdentifier (sha256 $actualHash): $($artifact.FullName)"
    }
    if (-not [OperatingSystem]::IsWindows() -and
        -not $RuntimeIdentifier.StartsWith('win-', [System.StringComparison]::Ordinal)) {
        try {
            $mode = [System.IO.File]::GetUnixFileMode($artifact.FullName)
        }
        catch {
            throw "Cannot prepare Unix PanGloss artifact '$($artifact.FullName)' on this operating system."
        }
        $ownerExecute = [System.IO.UnixFileMode]::UserExecute
        if (($mode -band $ownerExecute) -eq 0) {
            [System.IO.File]::SetUnixFileMode($artifact.FullName, $mode -bor $ownerExecute)
        }
    }

    return [pscustomobject]@{
        Path = $artifact.FullName
        Version = [string] $pin.version
        Tag = [string] $pin.tag
        RuntimeIdentifier = $RuntimeIdentifier
        AssetName = [System.IO.Path]::GetFileName($assetUri.AbsolutePath)
        Url = [string] $asset.url
        Sha256 = [string] $asset.sha256
    }
}

Export-ModuleMember -Function Get-CurrentPanGlossRuntimeIdentifier, Get-PinnedPanGlossArtifact
