function Split-MotifCaptureScenes {
    param([Parameter(Mandatory)][string[]] $Scenes)

    for ($offset = 0; $offset -lt $Scenes.Count; $offset += 7) {
        $last = [Math]::Min($offset + 6, $Scenes.Count - 1)
        [pscustomobject]@{ Number = [int]($offset / 7) + 1; Scenes = @($Scenes[$offset..$last]) }
    }
}

function Write-MotifCaptureAtomicText {
    param([string] $Path, [string] $Text)

    [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
    $temporary = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temporary, $Text, [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporary, $Path, $true)
    }
    finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}

function Get-MotifCaptureCellKey {
    param($Cell)

    return "$($Cell.scene)|$($Cell.size)|$($Cell.zoom)|$($Cell.theme)"
}

function Read-MotifCapturePlan {
    param([Parameter(Mandatory)][string] $OutputDirectory)

    $path = Join-Path $OutputDirectory 'matrix/capture-plan.json'
    $plan = [IO.File]::ReadAllText($path) | ConvertFrom-Json -AsHashtable
    if ($plan -isnot [System.Collections.IDictionary] -or $plan.scenes.Count -eq 0 -or
        $plan.sizes.Count -eq 0 -or $plan.points.Count -eq 0) { throw 'Empty or malformed capture catalog.' }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($scene in $plan.scenes) {
        if ($scene -isnot [string] -or $scene -cnotmatch '^[a-z0-9][a-z0-9-]*$' -or -not $names.Add($scene)) {
            throw "Invalid or duplicate capture scene: $scene"
        }
    }
    $sizes = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($size in $plan.sizes) {
        if ($size.name -cnotin @('thin', 'half', 'full', 'short') -or $sizes.ContainsKey($size.name) -or
            $size.width -isnot [ValueType] -or $size.height -isnot [ValueType] -or
            $size.width -le 0 -or $size.height -le 0 -or
            $size.width -ne [Math]::Floor($size.width) -or $size.height -ne [Math]::Floor($size.height)) {
            throw 'Invalid or duplicate capture dimensions.'
        }
        $sizes.Add($size.name, $size)
    }
    $points = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($point in $plan.points) {
        if (-not $sizes.ContainsKey($point.size) -or ($point.zoom -isnot [int] -and $point.zoom -isnot [long]) -or $point.zoom -cnotin @(100, 150) -or
            $point.theme -cnotin @('light', 'dark') -or
            -not $points.Add("$($point.size)|$($point.zoom)|$($point.theme)")) {
            throw 'Invalid or duplicate capture point.'
        }
    }
    if ($sizes.Count -ne 4 -or $points.Count -ne 16) { throw 'All capture catalog must declare all 16 matrix points.' }
    return $plan
}

function Get-MotifCaptureBatchPaths {
    param([string] $OutputDirectory, $Batch)

    $prefix = Join-Path $OutputDirectory ('matrix/batches/batch-{0:D3}' -f $Batch.Number)
    return @{ Index = $prefix + '-index.csv'; Findings = $prefix + '-findings.json'
        Metadata = $prefix + '-run.json'; Log = $prefix + '.log' }
}

function Clear-MotifCaptureBatchLiveArtifacts {
    param([string] $OutputDirectory)

    foreach ($file in @('index.csv', 'findings.json')) {
        $path = Join-Path $OutputDirectory "matrix/$file"
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
}

function Save-MotifCaptureBatch {
    param([string] $OutputDirectory, $Batch, [int] $ExitCode)

    $paths = Get-MotifCaptureBatchPaths $OutputDirectory $Batch
    [IO.Directory]::CreateDirectory((Split-Path -Parent $paths.Index)) | Out-Null
    foreach ($item in @(@('index.csv', $paths.Index), @('findings.json', $paths.Findings))) {
        if (Test-Path -LiteralPath $item[1]) { Remove-Item -LiteralPath $item[1] -Force }
        $source = Join-Path $OutputDirectory ('matrix/' + $item[0])
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            Write-MotifCaptureAtomicText $item[1] ([IO.File]::ReadAllText($source))
        }
    }
    Write-MotifCaptureAtomicText $paths.Metadata (@{ scenes = $Batch.Scenes; exitCode = $ExitCode
        log = [IO.Path]::GetFileName($paths.Log) } | ConvertTo-Json -Depth 5)
    Clear-MotifCaptureBatchLiveArtifacts $OutputDirectory
}

function Get-MotifCapturePngSize {
    param([string] $Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 45 -or [Convert]::ToHexString($bytes[0..15]) -cne '89504E470D0A1A0A0000000D49484452') {
        throw "Invalid PNG header: $Path"
    }
    $width = [System.Buffers.Binary.BinaryPrimitives]::ReadUInt32BigEndian([byte[]]$bytes[16..19])
    $height = [System.Buffers.Binary.BinaryPrimitives]::ReadUInt32BigEndian([byte[]]$bytes[20..23])
    $offset = 8L
    $hasData = $false
    $hasEnd = $false
    while ($offset + 12 -le $bytes.LongLength) {
        $length = [System.Buffers.Binary.BinaryPrimitives]::ReadUInt32BigEndian([byte[]]$bytes[$offset..($offset + 3)])
        $type = [Text.Encoding]::ASCII.GetString($bytes, [int]$offset + 4, 4)
        $offset += 12L + $length
        if ($offset -gt $bytes.LongLength) { throw "Truncated PNG chunk: $Path" }
        if ($type -ceq 'IDAT') { $hasData = $true }
        if ($type -ceq 'IEND') { $hasEnd = $length -eq 0; break }
    }
    if (-not $hasData -or -not $hasEnd -or $offset -ne $bytes.LongLength -or $width -eq 0 -or $height -eq 0) {
        throw "Incomplete PNG: $Path"
    }
    return @{ width = $width; height = $height }
}

function Read-MotifCaptureBatch {
    param([string] $OutputDirectory, $Plan, $Batch)

    $paths = Get-MotifCaptureBatchPaths $OutputDirectory $Batch
    $issues = [Collections.Generic.List[string]]::new()
    $cells = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $invalid = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $rawFindings = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $exitCode = $null
    try {
        $metadata = [IO.File]::ReadAllText($paths.Metadata) | ConvertFrom-Json -AsHashtable
        $exitCode = $metadata.exitCode
        if (($metadata.scenes -join ',') -cne ($Batch.Scenes -join ',') -or $exitCode -isnot [long] -and
            $exitCode -isnot [int]) { throw 'Batch metadata does not match the declared batch.' }
        $rows = @(Import-Csv -LiteralPath $paths.Index)
        $document = [System.Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($paths.Findings))
        try {
            if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Array) {
                throw 'Finding snapshot must be a JSON array.'
            }
            foreach ($row in $rows) {
                $key = Get-MotifCaptureCellKey $row
                try {
                    if ($cells.ContainsKey($key)) { throw "Duplicate cell: $key" }
                    $size = @($Plan.sizes | Where-Object { $_.name -ceq $row.size })
                    $point = @($Plan.points | Where-Object {
                        $_.size -ceq $row.size -and [string]$_.zoom -ceq $row.zoom -and $_.theme -ceq $row.theme
                    })
                    if ($row.scene -cnotin $Batch.Scenes -or $size.Count -ne 1 -or $point.Count -ne 1 -or
                        $row.file -cne "$($row.scene)-$($row.size)-$($row.zoom)-$($row.theme).png" -or
                        $row.width -notmatch '^\d+$' -or $row.height -notmatch '^\d+$' -or
                        [double]$row.width -ne $size[0].width -or [double]$row.height -ne $size[0].height -or
                        $row.findings -notmatch '^\d+$') { throw "Invalid cell: $key" }
                    $pngSize = Get-MotifCapturePngSize (Join-Path $OutputDirectory ('matrix/' + $row.file))
                    if ($pngSize.width -ne $size[0].width -or $pngSize.height -ne $size[0].height) {
                        throw "PNG dimensions do not match: $key"
                    }
                    $cells.Add($key, $row)
                    $rawFindings.Add($key, [Collections.Generic.List[object]]::new())
                }
                catch { [void]$invalid.Add($key); $issues.Add($_.Exception.Message) }
            }
            foreach ($finding in $document.RootElement.EnumerateArray()) {
                $key = $null
                try {
                    $context = @{ scene = $finding.GetProperty('scene').GetString()
                        size = $finding.GetProperty('size').GetString(); zoom = $finding.GetProperty('zoom').GetInt32()
                        theme = $finding.GetProperty('theme').GetString() }
                    $key = Get-MotifCaptureCellKey $context
                    if (-not $cells.ContainsKey($key) -or
                        $finding.GetProperty('file').GetString() -cne $cells[$key].file) {
                        throw "Finding has no matching captured cell: $key"
                    }
                    $expected = $finding.GetProperty('expected').GetBoolean()
                    $rawFindings[$key].Add([pscustomobject]@{ Raw = $finding.GetRawText(); Unexpected = -not $expected })
                }
                catch {
                    if ($key) { [void]$invalid.Add($key) }
                    $issues.Add($_.Exception.Message)
                }
            }
            foreach ($key in @($cells.Keys)) {
                if ([long]$cells[$key].findings -ne $rawFindings[$key].Count) {
                    [void]$invalid.Add($key); $issues.Add("CSV/JSON finding count mismatch: $key")
                }
            }
        }
        finally { $document.Dispose() }
    }
    catch {
        $issues.Add($_.Exception.Message)
        $cells.Clear()
    }
    $valid = @($cells.Keys | Where-Object { -not $invalid.Contains($_) } | Sort-Object | ForEach-Object {
        [pscustomobject]@{ Row = $cells[$_]; Findings = $rawFindings[$_] }
    })
    return [pscustomobject]@{ Number = $Batch.Number; Scenes = $Batch.Scenes; ExitCode = $exitCode
        Cells = $valid; Issues = @($issues.ToArray()); Log = $paths.Log }
}

function Merge-MotifCaptureBatches {
    param([string] $OutputDirectory, $Plan, [object[]] $Batches,
        [object[]] $Runs = @(), [string[]] $CatalogIssues = @())

    $expected = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    if ($null -ne $Plan) {
        foreach ($scene in $Plan.scenes) {
            foreach ($point in $Plan.points) {
                $cell = [pscustomobject]@{ scene = $scene; size = $point.size; zoom = $point.zoom; theme = $point.theme }
                $expected.Add((Get-MotifCaptureCellKey $cell), $cell)
            }
        }
    }
    $issues = [Collections.Generic.List[string]]::new()
    foreach ($issue in $CatalogIssues) { $issues.Add($issue) }
    $results = @($Batches | ForEach-Object { Read-MotifCaptureBatch $OutputDirectory $Plan $_ })
    $candidates = @($results | ForEach-Object { $_.Cells })
    $valid = @($candidates | Group-Object { Get-MotifCaptureCellKey $_.Row } | ForEach-Object {
        if ($_.Count -ne 1 -or -not $expected.ContainsKey($_.Name)) { $issues.Add("Duplicate or undeclared cell: $($_.Name)") }
        else { $_.Group[0] }
    })
    foreach ($result in $results) { foreach ($issue in $result.Issues) { $issues.Add("Batch $($result.Number): $issue") } }
    $capturedKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($cell in $valid) { [void]$capturedKeys.Add((Get-MotifCaptureCellKey $cell.Row)) }
    $absent = @($expected.Keys | Where-Object { -not $capturedKeys.Contains($_) } | Sort-Object | ForEach-Object { $expected[$_] })
    $findings = @($valid | ForEach-Object { $_.Findings })
    $unexpected = @($findings | Where-Object { $_.Unexpected }).Count
    $complete = $null -ne $Plan -and $issues.Count -eq 0 -and $absent.Count -eq 0
    $summary = [ordered]@{ complete = $complete
        success = $complete -and $unexpected -eq 0 -and @($results | Where-Object { $_.ExitCode -ne 0 }).Count -eq 0 -and
            @($Runs | Where-Object { $_.exitCode -ne 0 }).Count -eq 0
        expected = @($expected.Values); captured = @($valid | ForEach-Object { $expected[(Get-MotifCaptureCellKey $_.Row)] })
        absent = $absent; unexpectedFindings = $unexpected; issues = @($issues.ToArray()); runs = $Runs
        batches = @($results | Select-Object Number, Scenes, ExitCode, Issues, Log) }
    $csv = if ($valid.Count) { (@($valid | ForEach-Object { $_.Row } | ConvertTo-Csv -NoTypeInformation) -join "`n") + "`n" }
        else { "file,scene,size,width,height,zoom,theme,findings`n" }
    Write-MotifCaptureAtomicText (Join-Path $OutputDirectory 'matrix/index.csv') $csv
    Write-MotifCaptureAtomicText (Join-Path $OutputDirectory 'matrix/findings.json') ('[' + (($findings | ForEach-Object { $_.Raw }) -join ",`n") + ']')
    Write-MotifCaptureAtomicText (Join-Path $OutputDirectory 'matrix/capture-completeness.json') ($summary | ConvertTo-Json -Depth 10)
    return $summary
}

function Get-MotifCaptureBatchEnvironment {
    param([string] $OutputDirectory, $Plan, $Batch)

    return Get-MotifCaptureEnvironment -OutputDirectory $OutputDirectory -Scenes $Batch.Scenes `
        -Sizes @($Plan.sizes | ForEach-Object { $_.name }) -Zooms '100,150' -Themes 'light,dark'
}

function Invoke-MotifCaptureRun {
    param([scriptblock] $RunTests, [string] $Filter, [string] $Log, [System.Collections.IDictionary] $Environment)

    [IO.Directory]::CreateDirectory((Split-Path -Parent $Log)) | Out-Null
    Write-MotifCaptureAtomicText $Log ("Filter: $Filter`n" + ($Environment | ConvertTo-Json) + "`n")
    $saved = Set-MotifCaptureEnvironment $Environment
    try {
        $code = & $RunTests $Filter $Log
        if ($code -isnot [int] -and $code -isnot [long]) { throw 'Screenshot runner must return one integer exit code.' }
    }
    catch {
        [IO.File]::AppendAllText($Log, $_.Exception.ToString() + "`n")
        $code = 1
    }
    finally { Restore-MotifCaptureEnvironment $saved }
    return [pscustomobject]@{ filter = $Filter; exitCode = $code; log = $Log }
}

function Invoke-MotifAllCaptures {
    param([Parameter(Mandatory)][string] $OutputDirectory, [Parameter(Mandatory)][scriptblock] $RunTests)

    $runs = [Collections.Generic.List[object]]::new()
    $issues = [Collections.Generic.List[string]]::new()
    $plan = $null
    $batches = @()
    $catalogPath = Join-Path $OutputDirectory 'matrix/capture-plan.json'
    if (Test-Path -LiteralPath $catalogPath) { Remove-Item -LiteralPath $catalogPath -Force }
    $allEnvironment = Get-MotifCaptureEnvironment -OutputDirectory $OutputDirectory -All
    $catalog = Invoke-MotifCaptureRun $RunTests 'FullyQualifiedName~SizeMatrixScreenshots.WriteCaptureMatrixPlan' `
        (Join-Path $OutputDirectory 'matrix/batches/catalog.log') $allEnvironment
    $runs.Add($catalog)
    try { $plan = Read-MotifCapturePlan $OutputDirectory; $batches = @(Split-MotifCaptureScenes $plan.scenes) }
    catch { $issues.Add("Capture catalog unavailable: $($_.Exception.Message)") }
    $runs.Add((Invoke-MotifCaptureRun $RunTests 'FullyQualifiedName~Screenshot&FullyQualifiedName!~SizeMatrixScreenshots' `
        (Join-Path $OutputDirectory 'matrix/batches/other-screenshots.log') $allEnvironment))
    foreach ($batch in $batches) {
        Clear-MotifCaptureBatchLiveArtifacts $OutputDirectory
        $paths = Get-MotifCaptureBatchPaths $OutputDirectory $batch
        $environment = Get-MotifCaptureBatchEnvironment $OutputDirectory $plan $batch
        $run = Invoke-MotifCaptureRun $RunTests `
            'FullyQualifiedName~SizeMatrixScreenshots.CaptureScenesAtEveryMatrixSizeAndWriteAllFindings' $paths.Log $environment
        Save-MotifCaptureBatch $OutputDirectory $batch $run.exitCode
    }
    return Merge-MotifCaptureBatches $OutputDirectory $plan $batches $runs.ToArray() $issues.ToArray()
}
