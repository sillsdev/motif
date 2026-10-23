$ErrorActionPreference = 'Stop'
$summaryScript = Join-Path (Get-Location) 'tools/Summarize-MotifTrace.ps1'
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) "motif-trace-summary-$PID"
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

function Invoke-Summary([string]$CaseName, [string[]]$Lines, [string]$Label = $null) {
    $path = Join-Path $tempRoot "$CaseName.jsonl"
    [IO.File]::WriteAllText($path, [string]::Join([Environment]::NewLine, $Lines))
    $arguments = @('-NoProfile', '-File', $summaryScript, '-Path', $path)
    if ($PSBoundParameters.ContainsKey('Label')) { $arguments += @('-Label', $Label) }
    $output = @(& pwsh @arguments 2>&1)
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Text = $output -join "`n" }
}

function Assert-Contains([string]$CaseName, [string]$Text, [string]$Needle) {
    if ($Text -notlike "*$Needle*") { throw "$CaseName did not contain '$Needle': $Text" }
}

function Assert-NotContains([string]$CaseName, [string]$Text, [string]$Needle) {
    if ($Text -like "*$Needle*") { throw "$CaseName unexpectedly contained '$Needle': $Text" }
}

function Assert-Succeeded([string]$CaseName, $Result) {
    if ($Result.ExitCode -ne 0) { throw "$CaseName exited $($Result.ExitCode): $($Result.Text)" }
}

try {
    $unlabelled = '{"name":"view.switch","kind":"page","from":"Overview","to":"Texts","ms":123}'
    $labelled = '{"name":"view.switch","kind":"page","from":"Texts","to":"Timing","ms":234,"label":"selected"}'
    $otherLabel = '{"name":"view.switch","kind":"page","from":"Timing","to":"Warnings","ms":345,"label":"other"}'

    $labelledResult = Invoke-Summary 'labelled' @($labelled)
    Assert-Succeeded 'labelled' $labelledResult
    Assert-Contains 'labelled' $labelledResult.Text 'selected'
    Assert-Contains 'labelled' $labelledResult.Text 'Texts -> Timing'

    $unlabelledResult = Invoke-Summary 'unlabelled' @($unlabelled)
    Assert-Succeeded 'unlabelled' $unlabelledResult
    Assert-Contains 'unlabelled' $unlabelledResult.Text 'unlabelled'
    Assert-Contains 'unlabelled' $unlabelledResult.Text 'Overview -> Texts'

    $mixedResult = Invoke-Summary 'mixed' @($unlabelled, $labelled, $otherLabel)
    Assert-Succeeded 'mixed' $mixedResult
    Assert-Contains 'mixed' $mixedResult.Text 'unlabelled'
    Assert-Contains 'mixed' $mixedResult.Text 'selected'
    Assert-Contains 'mixed' $mixedResult.Text 'other'

    $selectedResult = Invoke-Summary 'selected' @($unlabelled, $labelled, $otherLabel) 'selected'
    Assert-Succeeded 'selected' $selectedResult
    Assert-Contains 'selected' $selectedResult.Text 'Texts -> Timing'
    Assert-NotContains 'selected' $selectedResult.Text 'Overview -> Texts'
    Assert-NotContains 'selected' $selectedResult.Text 'Warnings'

    $absentResult = Invoke-Summary 'absent' @($unlabelled, $labelled) 'missing'
    Assert-Succeeded 'absent selected label' $absentResult
    if (-not [string]::IsNullOrWhiteSpace($absentResult.Text)) { throw "Absent selected label produced output: $($absentResult.Text)" }

    $emptyResult = Invoke-Summary 'empty' @()
    Assert-Succeeded 'empty' $emptyResult
    if (-not [string]::IsNullOrWhiteSpace($emptyResult.Text)) { throw "Empty input produced output: $($emptyResult.Text)" }

    Write-Output 'Summary trace probe passed (labelled, unlabelled, mixed, filtered, absent label, empty).'
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force
}
