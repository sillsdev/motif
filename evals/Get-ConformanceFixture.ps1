[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$lockPath = Join-Path $PSScriptRoot 'conformance.lock.json'
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$commit = ([string]$lock.commit).ToLowerInvariant()
$repository = [string]$lock.repository
$sourceRoot = [string]$lock.sourceRoot
$files = @($lock.cacheFiles)

if ($repository -ne 'https://github.com/sillsdev/machine' -or
    $commit -ne 'f150e2a005ce639f7d68ef17fb0db25b2f6aaa3c' -or
    $sourceRoot -ne 'conformance/edge-cases/deep-optional-affix-nesting' -or
    $commit -notmatch '^[0-9a-f]{40}$' -or $files.Count -ne 6) {
    throw 'The conformance lock does not match the reviewed Machine source pin.'
}

$expectedCachePaths = @(
    'project.fwdata',
    'WritingSystemStore/de.ldml',
    'WritingSystemStore/en.ldml',
    'WritingSystemStore/es.ldml',
    'WritingSystemStore/fr.ldml',
    'words.yaml'
)
$actualCachePaths = @($files | ForEach-Object { [string]$_.cachePath } | Sort-Object)
$expectedCachePaths = @($expectedCachePaths | Sort-Object)
if (($actualCachePaths -join "`n") -ne ($expectedCachePaths -join "`n")) {
    throw 'The conformance lock does not contain the reviewed six-file input set.'
}

function Assert-CachePath([string] $RelativePath) {
    if ([IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath -match '(^|[\\/])\.{1,2}([\\/]|$)' -or
        $RelativePath.Contains(':')) {
        throw "Unsafe path in conformance lock: $RelativePath"
    }
}

function Assert-NoLinks([string] $Root, [string] $RelativePath) {
    $current = $Root
    foreach ($part in ($RelativePath -split '[\\/]')) {
        $current = Join-Path $current $part
        if (-not (Test-Path -LiteralPath $current)) { continue }
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq [IO.FileAttributes]::ReparsePoint) {
            throw "Conformance cache contains a linked path: $current"
        }
    }
}

function Read-BoundedBytes([string] $Path, [int] $ExpectedBytes) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $bytes = [byte[]]::new($ExpectedBytes)
        $total = 0
        while ($total -lt $bytes.Length) {
            $read = $stream.Read($bytes, $total, $bytes.Length - $total)
            if ($read -eq 0) { throw "Pinned conformance file ended early: $Path" }
            $total += $read
        }
        if ($stream.ReadByte() -ne -1) { throw "Pinned conformance file exceeds its byte limit: $Path" }
        return ,$bytes
    }
    finally {
        $stream.Dispose()
    }
}

function Assert-OracleSelections([byte[]] $Bytes) {
    $selectionNames = @($lock.oracleSelections.PSObject.Properties.Name | Sort-Object)
    $expectedNames = @('nineHundredTwentyFour', 'oneAnalysisLong', 'oneAnalysisShort') | Sort-Object
    if (($selectionNames -join "`n") -ne ($expectedNames -join "`n")) {
        throw 'The conformance lock must select exactly the three reviewed oracle cases.'
    }

    $prefix = [Text.Encoding]::UTF8.GetBytes('  - word: "')
    $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
    $ranges = [Collections.Generic.List[object]]::new()
    foreach ($name in $expectedNames) {
        $selection = $lock.oracleSelections.$name
        $offset = [long]$selection.byteOffset
        $length = [long]$selection.byteLength
        if ($offset -lt $prefix.Length -or $length -le 0 -or $offset + $length -ge $Bytes.LongLength) {
            throw "Invalid bounded oracle selection metadata: $name"
        }

        $lineStart = $offset - $prefix.Length
        while ($lineStart -gt 0 -and $Bytes[$lineStart - 1] -ne 10) { $lineStart-- }
        if ($lineStart -ne $offset - $prefix.Length) {
            throw "Oracle selection is not at a word scalar boundary: $name"
        }
        for ($index = 0; $index -lt $prefix.Length; $index++) {
            if ($Bytes[$lineStart + $index] -ne $prefix[$index]) {
                throw "Oracle selection is not at a word scalar boundary: $name"
            }
        }

        $end = $offset + $length
        if ($Bytes[$end] -ne 34) { throw "Oracle selection does not end at a scalar boundary: $name" }
        $tail = $end + 1
        while ($tail -lt $Bytes.LongLength -and $Bytes[$tail] -in @(9, 32)) { $tail++ }
        if ($tail -lt $Bytes.LongLength -and $Bytes[$tail] -notin @(10, 13)) {
            throw "Oracle selection has trailing scalar data: $name"
        }

        for ($index = $offset; $index -lt $end; $index++) {
            if ($Bytes[$index] -in @(0, 10, 13, 34, 92)) {
                throw "Oracle selection requires YAML escape processing: $name"
            }
        }
        [void]$strictUtf8.GetString($Bytes, [int]$offset, [int]$length)
        $ranges.Add([pscustomobject]@{ Start = $offset; End = $end })
    }

    $ordered = @($ranges | Sort-Object Start)
    for ($index = 1; $index -lt $ordered.Count; $index++) {
        if ($ordered[$index].Start -lt $ordered[$index - 1].End) {
            throw 'The conformance oracle selections overlap.'
        }
    }
}

function Assert-VerifiedCache([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Pinned conformance cache is absent: $Path. Run pwsh ./evals/Get-ConformanceFixture.ps1."
    }

    $root = [IO.Path]::GetFullPath($Path)
    $rootInfo = Get-Item -LiteralPath $root -Force
    if (($rootInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq [IO.FileAttributes]::ReparsePoint) {
        throw "Conformance cache root cannot be a link: $root"
    }

    $oracleBytes = $null
    foreach ($file in $files) {
        $relativePath = [string]$file.cachePath
        Assert-CachePath $relativePath
        $expectedBytes = [int]$file.bytes
        $expectedHash = ([string]$file.sha256).ToLowerInvariant()
        $sourcePath = [string]$file.sourcePath
        $expectedSourcePath = if ($relativePath -eq 'words.yaml') { "$sourceRoot/words.yaml" } else { "$sourceRoot/fieldworks/$relativePath" }
        if ($sourcePath -ne $expectedSourcePath -or
            $expectedBytes -le 0 -or $expectedBytes -gt 1048576 -or
            $expectedHash -notmatch '^[0-9a-f]{64}$') {
            throw "Invalid conformance lock entry for $relativePath"
        }

        $path = Join-Path $root $relativePath
        Assert-NoLinks $root $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Pinned conformance file is missing: $path. Run pwsh ./evals/Get-ConformanceFixture.ps1."
        }

        $actualBytes = (Get-Item -LiteralPath $path).Length
        if ($actualBytes -ne $expectedBytes) {
            throw "Pinned conformance file has $actualBytes bytes; expected ${expectedBytes}: $path"
        }

        $bytes = Read-BoundedBytes $path $expectedBytes
        $actualHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            throw "Pinned conformance file SHA-256 mismatch: $path (expected $expectedHash, got $actualHash)"
        }
        if ($relativePath -eq 'words.yaml') { $oracleBytes = $bytes }
    }

    Assert-OracleSelections $oracleBytes

    return $root
}

function Receive-PinnedFile([string] $Url, [string] $Path, [int] $ExpectedBytes, [string] $ExpectedHash) {
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $cancellation = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(30))
    $response = $null
    $source = $null
    $destination = $null
    try {
        $response = $client.GetAsync(
            $Url, [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $cancellation.Token
        ).GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) {
            throw "Pinned conformance request returned HTTP $([int]$response.StatusCode): $Url"
        }
        if ($null -ne $response.Content.Headers.ContentLength -and
            $response.Content.Headers.ContentLength -ne $ExpectedBytes) {
            throw "Pinned conformance response has an unexpected content length: $Url"
        }

        $source = $response.Content.ReadAsStreamAsync($cancellation.Token).GetAwaiter().GetResult()
        $destination = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $buffer = [byte[]]::new(8192)
        $total = 0
        while (($read = $source.ReadAsync($buffer, 0, $buffer.Length, $cancellation.Token).GetAwaiter().GetResult()) -gt 0) {
            $total += $read
            if ($total -gt $ExpectedBytes) { throw "Pinned conformance response exceeded its byte limit: $Url" }
            $destination.Write($buffer, 0, $read)
        }
        $destination.Flush($true)
        $destination.Dispose()
        $destination = $null

        if ($total -ne $ExpectedBytes) { throw "Pinned conformance response ended at $total bytes: $Url" }
        $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $ExpectedHash) { throw "Pinned conformance response SHA-256 mismatch: $Url" }
    }
    finally {
        if ($destination) { $destination.Dispose() }
        if ($source) { $source.Dispose() }
        if ($response) { $response.Dispose() }
        $cancellation.Dispose()
        $client.Dispose()
        $handler.Dispose()
    }
}

if (-not [string]::IsNullOrWhiteSpace($env:MOTIF_TEST_CONFORMANCE)) {
    $configuredPath = [IO.Path]::GetFullPath($env:MOTIF_TEST_CONFORMANCE)
    Write-Output (Assert-VerifiedCache $configuredPath)
    return
}

$cacheParent = Join-Path $repositoryRoot 'bin/.cache/conformance-oracle-v2'
$cachePath = Join-Path $cacheParent $commit
Assert-NoLinks $repositoryRoot 'bin/.cache/conformance-oracle-v2'
if (Test-Path -LiteralPath $cachePath) {
    Write-Output (Assert-VerifiedCache $cachePath)
    return
}

New-Item -ItemType Directory -Path $cacheParent -Force | Out-Null
$temporaryPath = Join-Path $cacheParent ('.fetch-' + [Guid]::NewGuid().ToString('N'))
try {
    [IO.Directory]::CreateDirectory($temporaryPath) | Out-Null
    foreach ($file in $files) {
        $relativePath = [string]$file.cachePath
        Assert-CachePath $relativePath
        $destination = Join-Path $temporaryPath $relativePath
        $destinationDirectory = Split-Path -Parent $destination
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        $uri = "https://raw.githubusercontent.com/sillsdev/machine/$commit/$([string]$file.sourcePath)"
        Receive-PinnedFile $uri $destination ([int]$file.bytes) ([string]$file.sha256)
    }

    [void](Assert-VerifiedCache $temporaryPath)
    try {
        [IO.Directory]::Move($temporaryPath, $cachePath)
    }
    catch {
        if (-not (Test-Path -LiteralPath $cachePath -PathType Container)) { throw }
        [void](Assert-VerifiedCache $cachePath)
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryPath -PathType Container) {
        Remove-Item -LiteralPath $temporaryPath -Recurse -Force
    }
}

Write-Output (Assert-VerifiedCache $cachePath)
