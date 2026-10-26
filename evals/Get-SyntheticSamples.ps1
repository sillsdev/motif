[CmdletBinding()]
param(
    [string] $ArchivePath,
    [switch] $Download
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $repositoryRoot 'tools/SyntheticSamples.psm1') -Force
$lock = Get-SyntheticSampleLock $repositoryRoot
if ($lock.files.Count -ne 6) { throw 'Synthetic sample input lock must name exactly six files.' }
$cacheRoot = Join-Path $repositoryRoot "bin/.cache/synthetic-samples/$($lock.commit)"
$target = Join-Path $cacheRoot 'samples'
if (Test-Path -LiteralPath $target) {
    $verified = Assert-SyntheticSampleRoot -RepositoryRoot $repositoryRoot -SampleRoot $target
    Write-Output $verified
    exit 0
}

if ($Download -and $ArchivePath) { throw 'Choose -Download or -ArchivePath, not both.' }
if ($Download) {
    $ArchivePath = Join-Path $cacheRoot 'synthetic-samples.zip'
    [IO.Directory]::CreateDirectory($cacheRoot) | Out-Null
    Invoke-WebRequest -Uri $lock.asset -OutFile $ArchivePath
}
if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
    throw "Synthetic sample inputs are not provisioned. Run ./evals/Get-SyntheticSamples.ps1 -Download, or pass the owner-approved archive with -ArchivePath."
}

$archive = [IO.Path]::GetFullPath($ArchivePath)
if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) { throw "Archive not found: $archive" }
if ((Get-Item -LiteralPath $archive).Length -ne [long] $lock.bytes) { throw 'Synthetic sample archive has the wrong byte length.' }
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne [string] $lock.sha256) {
    throw 'Synthetic sample archive failed SHA-256 verification.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
$contents = [System.Collections.Generic.Dictionary[string, byte[]]]::new([StringComparer]::Ordinal)
try {
    if ($zip.Entries.Count -ne $lock.files.Count) { throw 'Synthetic sample archive must contain exactly the six catalog files.' }
    $expectedEntries = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($file in $lock.files) { $expectedEntries.Add([string] $file.path, $file) }
    foreach ($entry in $zip.Entries) {
        $name = [string] $entry.FullName
        if ($name.Contains('\') -or [IO.Path]::IsPathRooted($name) -or $name.Split('/') -contains '..' -or $name.EndsWith('/')) {
            throw "Unsafe synthetic sample archive entry: $name"
        }
        $mode = ($entry.ExternalAttributes -shr 16) -band 0xF000
        if ($mode -eq 0xA000 -or (($entry.ExternalAttributes -band 0x400) -ne 0)) {
            throw "Linked entries are not allowed in the synthetic sample archive: $name"
        }
        if (-not $expectedEntries.ContainsKey($name)) { throw "Uncataloged synthetic sample archive entry: $name" }
        if ($contents.ContainsKey($name)) { throw "Duplicate synthetic sample archive entry: $name" }
        $expected = $expectedEntries[$name]
        if ($entry.Length -gt 65536 -or $entry.Length -ne [long] $expected.bytes) { throw "Invalid size for synthetic sample archive entry: $name" }
        $stream = $entry.Open()
        $memory = [IO.MemoryStream]::new()
        try {
            $buffer = [byte[]]::new(8192)
            while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                if ($memory.Length + $read -gt 65536) { throw "Oversized synthetic sample archive entry: $name" }
                $memory.Write($buffer, 0, $read)
            }
            $bytes = $memory.ToArray()
        }
        finally { $memory.Dispose(); $stream.Dispose() }
        $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
        if ($digest -ne [string] $expected.sha256) { throw "Synthetic sample archive entry failed SHA-256 verification: $name" }
        $contents.Add($name, $bytes)
    }
}
finally { $zip.Dispose() }

$stage = Join-Path $cacheRoot ('.samples-' + [Guid]::NewGuid().ToString('N'))
try {
    foreach ($entry in $contents.GetEnumerator()) {
        $relative = $entry.Key.Substring('samples/'.Length).Replace('/', [IO.Path]::DirectorySeparatorChar)
        $destination = Join-Path $stage $relative
        [IO.Directory]::CreateDirectory((Split-Path $destination -Parent)) | Out-Null
        [IO.File]::WriteAllBytes($destination, $entry.Value)
    }
    $verified = Assert-SyntheticSampleRoot -RepositoryRoot $repositoryRoot -SampleRoot $stage
    [IO.Directory]::CreateDirectory($cacheRoot) | Out-Null
    [IO.Directory]::Move($stage, $target)
}
catch {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    throw
}
Write-Output $target
