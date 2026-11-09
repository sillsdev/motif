[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $StageDirectory,
    [Parameter(Mandatory = $true)] [string] $RuntimeIdentifier,
    [string] $ManifestPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'tunnel-client-release.json'),
    [string] $ArchivePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$assetProperty = $manifest.rids.PSObject.Properties[$RuntimeIdentifier]
if ($null -eq $assetProperty) { throw "No tunnel-client release is pinned for $RuntimeIdentifier." }
$asset = $assetProperty.Value
if ([string]$asset.sha256 -cnotmatch '^[0-9a-f]{64}$') {
    throw "A verified tunnel-client SHA-256 is required for $RuntimeIdentifier; update $ManifestPath."
}

$temporaryArchive = $null
try {
    if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
        $temporaryArchive = Join-Path ([System.IO.Path]::GetTempPath()) ("motif-tunnel-" + [Guid]::NewGuid() + '.zip')
        Invoke-WebRequest -Uri $asset.url -OutFile $temporaryArchive
        $ArchivePath = $temporaryArchive
    }
    if ((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $asset.sha256) {
        throw 'The tunnel-client archive did not match its pinned SHA-256.'
    }

    $suffix = if ($RuntimeIdentifier.StartsWith('win-')) { '.exe' } else { '' }
    $required = @("tunnel-client$suffix", "cloudflared$suffix", 'cloudflared-manifest.json', 'LICENSE', 'NOTICE')
    $destination = Join-Path $StageDirectory 'tunnel-client'
    [System.IO.Directory]::CreateDirectory($destination) | Out-Null
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($name in $required) {
            $entries = @($archive.Entries | Where-Object { $_.FullName -ceq $name })
            if ($entries.Count -ne 1) { throw "The tunnel-client archive must contain exactly one $name." }
            $stream = $entries[0].Open()
            try {
                $output = [System.IO.File]::Open((Join-Path $destination $name), [System.IO.FileMode]::CreateNew)
                try { $stream.CopyTo($output) } finally { $output.Dispose() }
            } finally { $stream.Dispose() }
        }
        $licences = @($archive.Entries | Where-Object { $_.FullName -cmatch '^tunnel-client-[a-z0-9-]+-licenses\.txt$' })
        if ($licences.Count -ne 1) { throw 'The tunnel-client archive must contain one licence report.' }
        $stream = $licences[0].Open()
        try {
            $output = [System.IO.File]::Open((Join-Path $destination 'THIRD-PARTY-LICENSES.txt'), [System.IO.FileMode]::CreateNew)
            try { $stream.CopyTo($output) } finally { $output.Dispose() }
        } finally { $stream.Dispose() }
    } finally { $archive.Dispose() }
    if (-not $RuntimeIdentifier.StartsWith('win-')) {
        $mode = [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite -bor
            [System.IO.UnixFileMode]::UserExecute -bor [System.IO.UnixFileMode]::GroupRead -bor
            [System.IO.UnixFileMode]::GroupExecute -bor [System.IO.UnixFileMode]::OtherRead -bor
            [System.IO.UnixFileMode]::OtherExecute
        foreach ($name in @('tunnel-client', 'cloudflared')) {
            [System.IO.File]::SetUnixFileMode((Join-Path $destination $name), $mode)
        }
    }
} finally {
    if ($null -ne $temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive -Force -ErrorAction SilentlyContinue }
}
