$ErrorActionPreference = 'Stop'

$forwardedArguments = @()
for ($index = 0; $index -lt $args.Count; $index++) {
    $argument = $args[$index]
    if ($argument -match '^-[^-].*:$' -and $index + 1 -lt $args.Count -and $args[$index + 1] -notmatch '^-') {
        $argument += $args[++$index]
    }
    $forwardedArguments += $argument
}

if ($IsWindows -and -not (Get-Command vswhere.exe -ErrorAction SilentlyContinue)) {
    $installerPath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
    $env:PATH = "$installerPath;$env:PATH"

    if (-not (Get-Command vswhere.exe -ErrorAction SilentlyContinue)) {
        throw "vswhere.exe is missing from PATH after adding '$installerPath'. Install Visual Studio Installer or add vswhere.exe to PATH."
    }
}

Push-Location (Join-Path $PSScriptRoot '..')
try {
    $publishArguments = @('publish', 'tests/AotCheck/AotCheck.csproj', '-c', 'Release', '-r', 'win-x64') + $forwardedArguments
    & dotnet @publishArguments
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
