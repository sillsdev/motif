Set-StrictMode -Version Latest

function Get-SyntheticSampleLock {
    param([Parameter(Mandatory = $true)][string] $RepositoryRoot)
    $path = Join-Path $RepositoryRoot 'evals/synthetic-samples.lock.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Synthetic sample input lock is missing: $path"
    }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

function Assert-SyntheticSampleRoot {
    param(
        [Parameter(Mandatory = $true)][string] $RepositoryRoot,
        [Parameter(Mandatory = $true)][string] $SampleRoot
    )
    $lock = Get-SyntheticSampleLock $RepositoryRoot
    if ($lock.files.Count -ne 6) { throw 'Synthetic sample input lock must name exactly six files.' }
    $root = [IO.Path]::GetFullPath($SampleRoot)
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "Verified synthetic sample input root is missing: $root. Run ./evals/Get-SyntheticSamples.ps1 -Download or provide a verified local input."
    }
    $rootItem = Get-Item -LiteralPath $root -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Synthetic sample input root may not be a link: $root"
    }

    $expected = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $lock.files) {
        $entryPath = [string] $entry.path
        if (-not $entryPath.StartsWith('samples/', [StringComparison]::Ordinal)) {
            throw "Invalid path in synthetic sample input lock: $entryPath"
        }
        $relative = $entryPath.Substring('samples/'.Length).Replace('/', [IO.Path]::DirectorySeparatorChar)
        [void] $expected.Add($relative.Replace([IO.Path]::DirectorySeparatorChar, '/'))
    }
    if ($expected.Count -ne 6) { throw 'Synthetic sample input lock must name six distinct files.' }
    $actualDirectories = @(Get-ChildItem -LiteralPath $root -Directory -Recurse -Force)
    if ($actualDirectories.Count -ne 2) {
        throw "Synthetic sample input root must contain exactly the two catalog sample directories: $root"
    }
    foreach ($directory in $actualDirectories) {
        if (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Synthetic sample input may not contain linked directories: $($directory.FullName)"
        }
        if ([IO.Path]::GetRelativePath($root, $directory.FullName).Replace('\', '/') -notin @('synthetic-bantu', 'synthetic-turkic')) {
            throw "Uncataloged directory in synthetic sample input root: $($directory.FullName)"
        }
    }
    $actualFiles = @(Get-ChildItem -LiteralPath $root -File -Recurse -Force)
    if ($actualFiles.Count -ne $expected.Count) {
        throw "Synthetic sample input root must contain exactly $($expected.Count) catalog files: $root"
    }
    foreach ($file in $actualFiles) {
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Synthetic sample input may not contain linked files: $($file.FullName)"
        }
        $relative = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
        if (-not $expected.Contains($relative)) {
            throw "Uncataloged file in synthetic sample input root: $($file.FullName)"
        }
    }

    foreach ($entry in $lock.files) {
        $relative = ([string] $entry.path).Substring('samples/'.Length).Replace('/', [IO.Path]::DirectorySeparatorChar)
        $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
        $prefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $path.StartsWith($prefix, [StringComparison]::Ordinal)) {
            throw "Synthetic sample input path escapes its root: $($entry.path)"
        }
        $file = Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        if (-not $file -or $file.PSIsContainer) {
            throw "Required synthetic sample input is missing: $path. Provision release $($lock.release)."
        }
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Synthetic sample input may not be a link: $path"
        }
        if ($file.Length -ne [long] $entry.bytes) {
            throw "Synthetic sample input has the wrong size: $path"
        }
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne [string] $entry.sha256) {
            throw "Synthetic sample input failed SHA-256 verification: $path"
        }
    }

    return $root
}

function Resolve-SyntheticSampleRoot {
    param([Parameter(Mandatory = $true)][string] $RepositoryRoot)
    $lock = Get-SyntheticSampleLock $RepositoryRoot
    $configured = [Environment]::GetEnvironmentVariable('MOTIF_SYNTHETIC_SAMPLES')
    $root = if ([string]::IsNullOrWhiteSpace($configured)) {
        Join-Path $RepositoryRoot "bin/.cache/synthetic-samples/$($lock.commit)/samples"
    } else {
        $configured
    }
    return Assert-SyntheticSampleRoot -RepositoryRoot $RepositoryRoot -SampleRoot $root
}

Export-ModuleMember -Function Get-SyntheticSampleLock, Assert-SyntheticSampleRoot, Resolve-SyntheticSampleRoot
