$ErrorActionPreference = 'Stop'
# Repo-owned test policy. Drover supplies only the CI kind and changed-file JSON.
$scope = @'
{
  "always": [],
  "fallback": [
    "SIL.Motif.Tests.App",
    "SIL.Motif.Tests.Cli",
    "SIL.Motif.Tests.Commands",
    "SIL.Motif.Tests.Contract",
    "SIL.Motif.Tests.LibLcm",
    "SIL.Motif.Tests.Mcp",
    "SIL.Motif.Tests.Worker",
    "SIL.Motif.Tests.App.Lifetime"
  ],
  "patterns": {
    "src/SIL.Motif.Contract/*": [
      "SIL.Motif.Tests.App",
      "SIL.Motif.Tests.Cli",
      "SIL.Motif.Tests.Commands",
      "SIL.Motif.Tests.Contract",
      "SIL.Motif.Tests.LibLcm",
      "SIL.Motif.Tests.Mcp",
      "SIL.Motif.Tests.Worker",
      "SIL.Motif.Tests.App.Lifetime"
    ],
    "src/SIL.Motif.Host/*": [
      "SIL.Motif.Tests.App",
      "SIL.Motif.Tests.Cli",
      "SIL.Motif.Tests.Commands",
      "SIL.Motif.Tests.Contract",
      "SIL.Motif.Tests.LibLcm",
      "SIL.Motif.Tests.Mcp",
      "SIL.Motif.Tests.Worker",
      "SIL.Motif.Tests.App.Lifetime"
    ],
    "src/SIL.Motif.Runner/*": [
      "SIL.Motif.Tests.Cli",
      "SIL.Motif.Tests.Commands",
      "SIL.Motif.Tests.Contract",
      "SIL.Motif.Tests.LibLcm",
      "SIL.Motif.Tests.Mcp",
      "SIL.Motif.Tests.Worker"
    ],
    "src/SIL.Motif.Projection/*": [
      "SIL.Motif.Tests.App",
      "SIL.Motif.Tests.Cli",
      "SIL.Motif.Tests.Commands",
      "SIL.Motif.Tests.Contract",
      "SIL.Motif.Tests.LibLcm",
      "SIL.Motif.Tests.Mcp",
      "SIL.Motif.Tests.Worker",
      "SIL.Motif.Tests.App.Lifetime"
    ],
    "tests/SIL.Motif.Tests.Support/*": [
      "SIL.Motif.Tests.App",
      "SIL.Motif.Tests.Cli",
      "SIL.Motif.Tests.Commands",
      "SIL.Motif.Tests.Contract",
      "SIL.Motif.Tests.LibLcm",
      "SIL.Motif.Tests.Mcp",
      "SIL.Motif.Tests.Worker",
      "SIL.Motif.Tests.App.Lifetime"
    ],
    "src/SIL.Motif.App/*": [
      "SIL.Motif.Tests.App",
      "SIL.Motif.Tests.App.Lifetime"
    ],
    "src/SIL.Motif.Help/*": [
      "SIL.Motif.Tests.Cli",
      "SIL.Motif.Tests.App",
      "SIL.Motif.Tests.App.Lifetime"
    ],
    "src/SIL.Motif.Mcp/*": [
      "SIL.Motif.Tests.Mcp"
    ],
    "src/SIL.Motif.Cli/*": [
      "SIL.Motif.Tests.Cli"
    ],
    "src/SIL.Motif.Commands/*": [
      "SIL.Motif.Tests.Commands",
      "SIL.Motif.Tests.Cli",
      "SIL.Motif.Tests.Mcp",
      "SIL.Motif.Tests.App",
      "SIL.Motif.Tests.App.Lifetime"
    ],
    "src/SIL.Motif.Worker/*": [
      "SIL.Motif.Tests.Worker",
      "SIL.Motif.Tests.Cli"
    ],
    "src/SIL.Motif.LibLcm/*": [
      "SIL.Motif.Tests.LibLcm",
      "SIL.Motif.Tests.Commands",
      "SIL.Motif.Tests.Worker"
    ],
    "tests/SIL.Motif.Tests.App/*": [
      "SIL.Motif.Tests.App",
      "SIL.Motif.Tests.App.Lifetime"
    ],
    "tests/SIL.Motif.Tests.Cli/*": [
      "SIL.Motif.Tests.Cli"
    ],
    "tests/SIL.Motif.Tests.Commands/*": [
      "SIL.Motif.Tests.Commands"
    ],
    "tests/SIL.Motif.Tests.Contract/*": [
      "SIL.Motif.Tests.Contract"
    ],
    "tests/SIL.Motif.Tests.LibLcm/*": [
      "SIL.Motif.Tests.LibLcm"
    ],
    "tests/SIL.Motif.Tests.Mcp/*": [
      "SIL.Motif.Tests.Mcp"
    ],
    "tests/SIL.Motif.Tests.Worker/*": [
      "SIL.Motif.Tests.Worker"
    ],
    "docs/*": [
      "SIL.Motif.Tests.Contract"
    ],
    "*.md": [
      "SIL.Motif.Tests.Contract"
    ],
    "site/*": [],
    "evals/*": [],
    ".github/*": [],
    "README.md": [
      "SIL.Motif.Tests.Contract"
    ],
    "tests/test-shard-weights.json": [],
    "tests/SIL.Motif.Tests.App.Lifetime/*": [
      "SIL.Motif.Tests.App.Lifetime"
    ]
  }
}
'@ | ConvertFrom-Json -AsHashtable
$env:MOTIF_TEST_SLOTS = '2'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$tempRoot = if ($env:TMPDIR) { $env:TMPDIR } else { [IO.Path]::GetTempPath() }
$scratch = Join-Path $tempRoot ('motif-ci-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $scratch 'dotnet-home'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $scratch 'nuget-http'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $scratch 'nuget-plugins'
try {
    if ($env:DROVER_CI_KIND -ne 'checks' -or -not $env:DROVER_CHANGED_FILES) {
        & pwsh -NoLogo -NoProfile -NonInteractive -File ./test.ps1 -All -Configuration Release
        exit $LASTEXITCODE
    }
    $paths = @($env:DROVER_CHANGED_FILES | ConvertFrom-Json)
    if ($paths.Count -eq 0) {
        & pwsh -NoLogo -NoProfile -NonInteractive -File ./test.ps1 -All -Configuration Release
        exit $LASTEXITCODE
    }
    $projects = [Collections.Generic.List[string]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($project in $scope.always) {
        if ($seen.Add($project)) { $projects.Add($project) }
    }
    $unmapped = $false
    foreach ($path in $paths) {
        $matched = $false
        foreach ($pattern in $scope.patterns.Keys) {
            $wildcard = [WildcardPattern]::new($pattern, [System.Management.Automation.WildcardOptions]::CultureInvariant)
            if ($wildcard.IsMatch($path)) {
                $matched = $true
                foreach ($project in $scope.patterns[$pattern]) {
                    if ($seen.Add($project)) { $projects.Add($project) }
                }
            }
        }
        if (-not $matched) { $unmapped = $true }
    }
    if ($unmapped) {
        foreach ($project in $scope.fallback) {
            if ($seen.Add($project)) { $projects.Add($project) }
        }
    }
    if ($projects.Count -eq 0) {
        Write-Output 'No test projects selected by repository test policy.'
    }
    foreach ($project in $projects) {
        & pwsh -NoLogo -NoProfile -NonInteractive -File ./test.ps1 -All -Configuration Release -Project $project
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    exit 0
} finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}
