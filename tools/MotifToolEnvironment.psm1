Set-StrictMode -Version Latest

<#
  .SYNOPSIS
  Point every per-user cache the build and test tools write at a folder inside this checkout.

  .DESCRIPTION
  A sandboxed agent may write only inside its worktree, and two worktrees building at once must not
  race on one per-user folder. Each tool below otherwise writes beside the user's home: the .NET CLI
  its first-run, telemetry and workload files; `dotnet run --file` its compiled program; NuGet its
  HTTP and plugin caches; Avalonia its build statistics; a child PowerShell its module cache. Any
  value the caller already set is kept, so a developer's own choice wins.

  NuGet's global packages folder stays shared and per-user (NUGET_PACKAGES): restore fills it once,
  and builds only read it. Temporary files stay in the system temporary folder, which every sandbox
  allows. `tools/Test-HermeticBuild.ps1` fails when anything else writes outside the checkout.
#>
function Initialize-MotifToolEnvironment {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $RepoRoot)

    $cache = Join-Path $RepoRoot 'bin/.cache'

    # The shared packages folder is fixed first: DOTNET_CLI_HOME below would otherwise move NuGet's default.
    if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
        $profileRoot = if ($env:USERPROFILE) { $env:USERPROFILE } elseif ($env:HOME) { $env:HOME } else {
            [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile) }
        $env:NUGET_PACKAGES = Join-Path $profileRoot '.nuget/packages'
    }

    function Set-Default([string] $Name, [string] $Value) {
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($Name))) {
            [Environment]::SetEnvironmentVariable($Name, $Value)
        }
    }

    Set-Default 'DOTNET_CLI_HOME' (Join-Path $cache 'dotnet-cli-home')
    Set-Default 'DOTNET_CLI_TELEMETRY_OPTOUT' '1'
    Set-Default 'TESTINGPLATFORM_TELEMETRY_OPTOUT' '1'
    Set-Default 'DOTNET_NOLOGO' '1'
    Set-Default 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE' '1'
    Set-Default 'DOTNET_GENERATE_ASPNET_CERTIFICATE' 'false'
    Set-Default 'DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE' '1'
    Set-Default 'DOTNET_ADD_GLOBAL_TOOLS_TO_PATH' 'false'
    Set-Default 'NUGET_HTTP_CACHE_PATH' (Join-Path $cache 'nuget/http-cache')
    Set-Default 'NUGET_PLUGINS_CACHE_PATH' (Join-Path $cache 'nuget/plugins-cache')
    Set-Default 'AVALONIA_TELEMETRY_OPTOUT' '1'
    Set-Default 'POWERSHELL_TELEMETRY_OPTOUT' '1'
    Set-Default 'POWERSHELL_UPDATECHECK' 'Off'
    Set-Default 'PSModuleAnalysisCachePath' (Join-Path $cache 'powershell/ModuleAnalysisCache')

    # On Linux the application-data folders follow XDG; on Windows they are left to the user profile.
    if (-not $IsWindows) {
        Set-Default 'XDG_DATA_HOME' (Join-Path $cache 'xdg-data')
        Set-Default 'XDG_CACHE_HOME' (Join-Path $cache 'xdg-cache')
    }
}

<#
  .SYNOPSIS
  The folder a file-based tool's compiled program is kept in, inside this checkout.
#>
function Get-MotifToolArtifactsPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $RepoRoot,
        [Parameter(Mandatory)][string] $ToolName
    )

    Join-Path $RepoRoot "bin/.cache/runfile/$ToolName"
}

<#
  .SYNOPSIS
  Copy the staged SIL ICU libraries beside the product and the test hosts, off Windows.

  .DESCRIPTION
  On Windows the SIL ICU package supplies its libraries through the build. On Linux and macOS they
  come from a staged folder named by MOTIF_SIL_ICU_STAGE, which CI fills and a developer fills once
  (the payload and its checksums are in tools/icu-payload.json). Without them every test that opens a
  FieldWorks project refuses to start, so a missing stage is reported here rather than as hundreds
  of failures.
#>
function Copy-MotifStagedIcu {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $RepoRoot,
        [Parameter(Mandatory)][string] $Configuration
    )

    if ($IsWindows) { return $true }
    $rid = if ($IsMacOS) {
        if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'osx-arm64' } else { 'osx-x64' }
    } else { 'linux-x64' }
    $manifest = Get-Content -LiteralPath (Join-Path $RepoRoot 'tools/icu-payload.json') -Raw | ConvertFrom-Json
    $payload = $manifest.rids.PSObject.Properties[$rid].Value
    $libraries = @($payload.libraries | ForEach-Object { [string] $_ })
    $stage = $env:MOTIF_SIL_ICU_STAGE
    if ([string]::IsNullOrWhiteSpace($stage) -or -not (Test-Path -LiteralPath $stage -PathType Container)) {
        Write-Host "MOTIF_SIL_ICU_STAGE is not set to a staged SIL ICU folder for $rid; tests that open a FieldWorks project will refuse to start." -ForegroundColor Yellow
        return $false
    }

    $pattern = if ($IsMacOS) { 'libicu*.dylib' } else { 'libicu*.so*' }
    foreach ($destination in @(
            (Join-Path $RepoRoot "bin/$Configuration/$($payload.nativeOutputDirectory)"),
            (Join-Path $RepoRoot "bin/$Configuration/tests/$($payload.nativeOutputDirectory)"))) {
        [System.IO.Directory]::CreateDirectory($destination) | Out-Null
        # cp -a keeps the version symlinks the loader resolves.
        & cp -a (Get-ChildItem -LiteralPath $stage -Filter $pattern).FullName $destination
        if ($LASTEXITCODE -ne 0) { return $false }
        foreach ($library in $libraries) {
            if (-not (Test-Path -LiteralPath (Join-Path $destination $library))) {
                Write-Host "The staged SIL ICU folder $stage has no $library." -ForegroundColor Red
                return $false
            }
        }
    }
    return $true
}

Export-ModuleMember -Function Initialize-MotifToolEnvironment, Get-MotifToolArtifactsPath, Copy-MotifStagedIcu
