Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ABProductPayload([string] $BuildRoot, [string] $ParserPath) {
    foreach ($file in Get-ChildItem -LiteralPath $BuildRoot -File) {
        if ($file.Name -match 'Tests|EvalSets|SampleProjects|SIL\.Motif\.App|\.pdb$|\.xml$') { continue }
        @{ source = $file.FullName; relative = $file.Name }
    }
    foreach ($directory in @('runtimes', 'IcuData')) {
        $tree = Join-Path $BuildRoot $directory
        if (-not (Test-Path -LiteralPath $tree -PathType Container)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $tree -File -Recurse) {
            @{ source = $file.FullName; relative = [IO.Path]::GetRelativePath($BuildRoot, $file.FullName) }
        }
    }
    @{ source = $ParserPath; relative = 'pangloss' }
}

function Copy-ABPayload([object[]] $Payload, [string] $Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($entry in $Payload) {
        $target = Join-Path $Destination $entry.relative
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        $file = Get-Item -LiteralPath $entry.source
        $source = if ($file.LinkTarget) { $file.ResolveLinkTarget($true).FullName } else { $file.FullName }
        Copy-Item -LiteralPath $source -Destination $target
    }
}

function Copy-ABProductRuntime([string] $BuildRoot, [string] $Destination, [string] $ParserPath) {
    Copy-ABPayload @(Get-ABProductPayload $BuildRoot $ParserPath) $Destination
}

function Get-ABPayloadFingerprint([object[]] $Payload) {
    $records = @($Payload | Sort-Object relative | ForEach-Object {
        $file = Get-Item -LiteralPath $_.source
        $source = if ($file.LinkTarget) { $file.ResolveLinkTarget($true).FullName } else { $file.FullName }
        $_.relative + ':' + (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    }) -join "`n"
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("motif-runtime-layer/v1`n" + $records))).ToLowerInvariant()
}

function Get-ABCachedLayer([string] $LayerRoot, [object[]] $Payload) {
    New-Item -ItemType Directory -Path $LayerRoot -Force | Out-Null
    $key = Get-ABPayloadFingerprint $Payload
    $destination = Join-Path $LayerRoot $key
    $lockPath = Join-Path $LayerRoot ($key + '.lock')
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $lock = $null
    while (-not $lock) {
        try { $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
        catch [IO.IOException] {
            if ($timer.Elapsed.TotalMinutes -ge 5) { throw "Timed out waiting for runtime layer: $key" }
            Start-Sleep -Milliseconds 200
        }
    }
    $partial = Join-Path $LayerRoot ([Guid]::NewGuid().ToString('N') + '.partial')
    try {
        if (-not (Test-Path -LiteralPath $destination)) {
            Copy-ABPayload $Payload $partial
            if ((Get-ABPayloadFingerprint $Payload) -ne $key) { throw 'Runtime inputs changed during staging; retry after the build finishes.' }
            Move-Item -LiteralPath $partial -Destination $destination
        }
        if ((Get-Item -LiteralPath $destination).LinkTarget) { throw 'A shared runtime layer must not be a symbolic link.' }
        return $destination
    }
    finally {
        if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Recurse -Force }
        $lock.Dispose()
    }
}

function Get-ABRuntimeLayers([string] $BuildRoot, [string] $ParserPath, [string] $RuntimeSource, [string] $LayerRoot) {
    $product = Get-ABCachedLayer $LayerRoot @(Get-ABProductPayload $BuildRoot $ParserPath)
    $runtimePayload = @(foreach ($name in @('dotnet', 'host', 'shared')) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $RuntimeSource $name) -File -Recurse) {
            @{ source = $file.FullName; relative = [IO.Path]::GetRelativePath($RuntimeSource, $file.FullName) }
        }
    })
    $runtime = Get-ABCachedLayer $LayerRoot $runtimePayload
    return @{ product = $product; runtime = $runtime }
}

function Get-ABHostLayer([string] $HostPath, [string] $LayerRoot) {
    $entries = @(@{ source = $HostPath; relative = 'client' })
    # A native host spawns helpers beside itself, e.g. Codex's code-mode host that carries its MCP tool calls.
    $prefix = [IO.Path]::GetFileNameWithoutExtension($HostPath) + '-'
    foreach ($helper in Get-ChildItem -LiteralPath (Split-Path $HostPath) -File -Filter ($prefix + '*') | Sort-Object Name) {
        $entries += @{ source = $helper.FullName; relative = $helper.Name }
    }
    return Get-ABCachedLayer $LayerRoot $entries
}

Export-ModuleMember -Function Copy-ABProductRuntime, Get-ABRuntimeLayers, Get-ABHostLayer
