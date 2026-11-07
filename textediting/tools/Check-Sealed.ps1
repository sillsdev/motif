# Fails when a build file in this folder points outside it (ADR 0034). Exactly two outside paths are allowed: the host's
# shared-settings file, imported by Directory.Build.props, and the host's SilVersions.props, imported by Directory.Packages.props.
# It reads every .props, .targets, .csproj and .sln file and resolves each quoted relative path that climbs with "..".
# Pulling the folder out inlines both imports; then delete $allowed's entries and the check still passes.
[CmdletBinding()]
param([string] $Root = (Split-Path -Parent $PSScriptRoot))

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$allowed = @{
    'Directory.Build.props'    = @([IO.Path]::GetFullPath((Join-Path $root '..\Directory.Build.Common.props')))
    'Directory.Packages.props' = @([IO.Path]::GetFullPath((Join-Path $root '..\SilVersions.props')))
}
$escapes = [Collections.Generic.List[string]]::new()
$files = @(Get-ChildItem -LiteralPath $root -Recurse -File -Include *.props, *.targets, *.csproj, *.sln, *.slnx, *.proj |
    Where-Object { $_.FullName.Substring($root.Length) -notmatch '(^|[\\/])(bin|obj)[\\/]' })
if ($files.Count -eq 0) { Write-Host "sealed: no build files found under $root" -ForegroundColor Red; exit 1 }
# Files MSBuild, the SDK or the compiler would pick up from above the folder without any import. The host's nuget.config
# (nuget.org only) is the one accepted: pulling the folder out needs no NuGet source of its own.
foreach ($name in 'Directory.Build.targets', 'Directory.Solution.props', 'Directory.Solution.targets', 'Directory.Packages.props',
                  'Directory.Build.rsp', 'global.json', '.editorconfig') {
    $above = Split-Path -Parent $root.TrimEnd('\', '/')
    while ($above) {
        if (Test-Path -LiteralPath (Join-Path $above $name)) { $escapes.Add("'$name' above the folder, at $above, would reach its build") }
        $above = Split-Path -Parent $above
    }
}
foreach ($file in $files) {
    $relative = $file.FullName.Substring($root.Length)
    $text = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($match in [regex]::Matches($text, '"([^"]*\.\.[\\/][^"]*)"')) {
        $value = $match.Groups[1].Value -replace '\$\((MSBuildThisFileDirectory|MSBuildProjectDirectory)\)', ''
        if ($value -match '\$\(|%\(|@\(') { $escapes.Add("${relative}: '$($match.Groups[1].Value)' climbs out of an MSBuild property this check cannot resolve"); continue }
        $target = [IO.Path]::GetFullPath((Join-Path $file.DirectoryName $value))
        if ($target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { continue }
        if ($allowed.ContainsKey($relative) -and $allowed[$relative] -contains $target) { continue }
        $escapes.Add("${relative}: '$($match.Groups[1].Value)' leaves the folder")
    }
}
if ($escapes.Count -gt 0) {
    $escapes | ForEach-Object { Write-Host "sealed: $_" -ForegroundColor Red }
    Write-Host "sealed: $($escapes.Count) path(s) leave $root; only the two host imports may (ADR 0034)." -ForegroundColor Red
    exit 1
}
Write-Host "sealed: $($files.Count) build files checked; nothing leaves the folder but the two host imports."
