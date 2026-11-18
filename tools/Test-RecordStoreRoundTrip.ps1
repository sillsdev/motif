[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$IndonesianCopy,
    [string]$CebuanoCopy,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
foreach ($inputPath in @($IndonesianCopy, $CebuanoCopy)) {
    if ([string]::IsNullOrWhiteSpace($inputPath)) { continue }
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf) -or
        [IO.Path]::GetExtension($inputPath) -ne '.fwdata') {
        throw "Round-trip gate requires an external .fwdata copy: $inputPath"
    }
}
$oldIndonesian = $env:MOTIF_RECORD_STORE_INDONESIAN
$oldCebuano = $env:MOTIF_RECORD_STORE_CEBUANO
try {
    $env:MOTIF_RECORD_STORE_INDONESIAN = (Resolve-Path -LiteralPath $IndonesianCopy).Path
    $env:MOTIF_RECORD_STORE_CEBUANO = if ($CebuanoCopy) { (Resolve-Path -LiteralPath $CebuanoCopy).Path } else { $null }
    & (Join-Path $PSScriptRoot '../test.ps1') -Project SIL.Motif.Tests.RecordStore -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    $env:MOTIF_RECORD_STORE_INDONESIAN = $oldIndonesian
    $env:MOTIF_RECORD_STORE_CEBUANO = $oldCebuano
}
