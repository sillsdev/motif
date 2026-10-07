[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $StageDirectory,

    [Parameter(Mandatory = $true)]
    [string[]] $AssetsFiles
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$stage = [System.IO.Path]::GetFullPath($StageDirectory)
$productLicence = Join-Path $repoRoot 'LICENSE'
if (-not (Test-Path -LiteralPath $productLicence -PathType Leaf)) {
    throw 'The root LICENSE file is required in every Motif payload.'
}
Copy-Item -LiteralPath $productLicence -Destination $stage
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repoRoot 'licenses') -Destination $stage -Recurse -Force
$records = @{}
foreach ($assetsFile in $AssetsFiles) {
    $assets = Get-Content -LiteralPath $assetsFile -Raw | ConvertFrom-Json
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package' -or $records.ContainsKey($library.Name)) { continue }
        $packagePath = $null
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $candidate = Join-Path $folder $library.Value.path
            if (Test-Path -LiteralPath $candidate -PathType Container) { $packagePath = $candidate; break }
        }
        if ($null -eq $packagePath) { throw "Restored package is missing: $($library.Name)" }
        $specs = @(Get-ChildItem -LiteralPath $packagePath -Filter '*.nuspec' -File)
        if ($specs.Count -ne 1) { throw "Expected one NuGet specification for $($library.Name)." }
        [xml] $spec = Get-Content -LiteralPath $specs[0].FullName -Raw
        $metadata = $spec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
        $licenseNode = $metadata.SelectSingleNode('*[local-name()="license"]')
        $licenseUrl = $metadata.SelectSingleNode('*[local-name()="licenseUrl"]')
        $destination = Join-Path $stage ("licenses/nuget/" + $library.Name)
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        # The update package's own .nuspec must stay its only one, so dependency specifications ship renamed.
        $specificationName = $specs[0].Name + '.xml'
        Copy-Item -LiteralPath $specs[0].FullName -Destination (Join-Path $destination $specificationName)
        $notices = @()
        foreach ($file in $library.Value.files) {
            if ([System.IO.Path]::GetFileName($file) -notmatch '^(?:licen[cs]e|copying|notice|third[-._]party[-._]notices)(?:\..*)?$') {
                continue
            }
            $source = Join-Path $packagePath $file
            $noticeDestination = Join-Path $destination $file
            New-Item -ItemType Directory -Path (Split-Path $noticeDestination -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $noticeDestination
            $notices += "licenses/nuget/$($library.Name)/$file"
        }
        $records[$library.Name] = [ordered]@{
            package = $library.Name
            declaredLicence = if ($null -ne $licenseNode) { $licenseNode.InnerText } else { $null }
            licenceUrl = if ($null -ne $licenseUrl) { $licenseUrl.InnerText } else { $null }
            specification = "licenses/nuget/$($library.Name)/$specificationName"
            notices = @($notices)
        }
    }
}
$inventory = @($records.Keys | Sort-Object | ForEach-Object { $records[$_] })
if ($inventory.Count -eq 0) { throw 'The release dependency inventory is empty.' }
[System.IO.File]::WriteAllText((Join-Path $stage 'licenses/nuget-packages.json'),
    ($inventory | ConvertTo-Json -Depth 5) + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))
