Set-StrictMode -Version Latest

function Get-ABTaskTools([string] $RepoRoot, [object] $Task) {
    if (-not $Task.Contains('needs')) { return @() }
    $capabilities = Get-Content -LiteralPath (Join-Path $RepoRoot 'evals/profiles/task-capabilities.json') -Raw |
        ConvertFrom-Json -AsHashtable
    $tools = [Collections.Generic.List[object]]::new()
    foreach ($need in @($Task.needs)) {
        if (-not $capabilities.Contains([string]$need)) {
            throw "Task '$($Task.id)' needs '$need', which no entry in evals/profiles/task-capabilities.json provides."
        }
        foreach ($tool in @($capabilities[[string]$need])) {
            if (-not @($tools | Where-Object { $_.name -eq $tool.name }).Count) { $tools.Add($tool) }
        }
    }
    return @($tools)
}

# A null tool list means every default tool, so those defaults are listed before the task's tools join.
function Join-ABTaskTools([Collections.IDictionary] $ServerProfile, [string[]] $DefaultTools, [object[]] $TaskTools) {
    $joined = @{} + $ServerProfile
    $selections = if ($ServerProfile.Contains('tools') -and $null -ne $ServerProfile.tools) { @($ServerProfile.tools) }
        else { @($DefaultTools | ForEach-Object { @{ tool = $_ } }) }
    $selections = @($selections)
    foreach ($tool in $TaskTools) {
        if (-not @($selections | Where-Object { $_.tool -eq $tool.name }).Count) { $selections += @{ tool = [string]$tool.name } }
    }
    $joined.tools = $selections
    return $joined
}

Export-ModuleMember -Function Get-ABTaskTools, Join-ABTaskTools
