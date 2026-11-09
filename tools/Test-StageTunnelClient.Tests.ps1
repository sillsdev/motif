Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("motif-tunnel-test-" + [Guid]::NewGuid())
[System.IO.Directory]::CreateDirectory($root) | Out-Null
$script = Join-Path $PSScriptRoot 'Stage-TunnelClient.ps1'
$rid = if ($IsWindows) { 'win-x64' } else { 'linux-x64' }
$suffix = if ($IsWindows) { '.exe' } else { '' }
try {
    $todoManifest = Join-Path $root 'todo.json'
    @{ rids = @{ $rid = @{ sha256 = 'TODO' } } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $todoManifest
    $failure = $null
    try { & $script -StageDirectory $root -RuntimeIdentifier $rid -ManifestPath $todoManifest } catch { $failure = $_.Exception.Message }
    if ($null -eq $failure -or -not $failure.Contains('verified tunnel-client SHA-256')) {
        throw "The placeholder hash was not refused: $failure"
    }

    $archivePath = Join-Path $root 'test.zip'
    $archive = [System.IO.Compression.ZipFile]::Open($archivePath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in @("tunnel-client$suffix", "cloudflared$suffix", 'cloudflared-manifest.json', 'LICENSE', 'NOTICE',
                'tunnel-client-linux-amd64-licenses.txt', '../outside')) {
            $entry = $archive.CreateEntry($name)
            $writer = [System.IO.StreamWriter]::new($entry.Open())
            try { $writer.Write("test $name") } finally { $writer.Dispose() }
        }
    } finally { $archive.Dispose() }
    $manifestPath = Join-Path $root 'pin.json'
    $pin = @{ rids = @{ $rid = @{ sha256 = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() } } }
    $pin | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath
    $stage = Join-Path $root 'stage'
    & $script -StageDirectory $stage -RuntimeIdentifier $rid -ManifestPath $manifestPath -ArchivePath $archivePath
    foreach ($name in @("tunnel-client$suffix", "cloudflared$suffix", 'LICENSE', 'NOTICE', 'THIRD-PARTY-LICENSES.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $stage "tunnel-client/$name") -PathType Leaf)) {
            throw "Missing staged file: $name"
        }
    }
    if (Test-Path -LiteralPath (Join-Path $root 'outside')) { throw 'An unexpected archive entry was extracted.' }
    $pin.rids[$rid].sha256 = '0' * 64
    $pin | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath
    $failure = $null
    try { & $script -StageDirectory (Join-Path $root 'bad') -RuntimeIdentifier $rid -ManifestPath $manifestPath -ArchivePath $archivePath }
    catch { $failure = $_.Exception.Message }
    if ($null -eq $failure -or -not $failure.Contains('did not match')) { throw "Wrong hash was accepted: $failure" }
    Write-Host 'Tunnel staging refuses placeholder and wrong hashes and extracts only the expected files.'
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
