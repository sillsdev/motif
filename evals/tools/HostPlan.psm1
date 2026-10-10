Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ABInferenceAuthMode([object] $Arm) {
    if ($Arm.host -eq 'fake') { return 'none' }
    if ($Arm.host -eq 'claude') {
        $mode = if ($Arm.Contains('authMode')) { [string]$Arm.authMode } else { 'api-key' }
        if ($mode -cnotin @('api-key', 'claude-plan')) { throw 'Claude authMode must be api-key or claude-plan.' }
        return $mode
    }
    if ($Arm.host -ne 'codex') { throw 'Unsupported inference host.' }
    if ($Arm.Contains('authMode')) { $mode = [string]$Arm.authMode }
    else {
        $selection = if ($env:MOTIF_INFERENCE_CODEX_AUTH) { $env:MOTIF_INFERENCE_CODEX_AUTH } else { 'api-key' }
        if ($selection -cnotin @('api-key', 'chatgpt')) { throw 'MOTIF_INFERENCE_CODEX_AUTH must be api-key or chatgpt.' }
        $mode = if ($selection -ceq 'chatgpt') { 'chatgpt-plan' } else { $selection }
    }
    if ($mode -cnotin @('api-key', 'chatgpt-plan')) { throw 'MOTIF_INFERENCE_CODEX_AUTH must be api-key or chatgpt.' }
    return $mode
}

function New-ABHostPlan([object] $Arm, [string] $Prompt, [object] $Limits, [string] $SystemAppend) {
    $authMode = Get-ABInferenceAuthMode $Arm
    $environment = [ordered]@{
        HOME = '/workspace/home'; USERPROFILE = '/workspace/home'; CODEX_HOME = '/workspace/config/codex'
        CLAUDE_CONFIG_DIR = '/workspace/config/claude'; XDG_DATA_HOME = '/workspace/state/data'
        XDG_CACHE_HOME = '/workspace/state/cache'; DOTNET_CLI_HOME = '/workspace/state/dotnet'
    }
    $bridge = '/usr/bin/python3'
    $bridgeArguments = @('/opt/client/stdio.py', '/channel/socket')
    $server = [ordered]@{ command = $bridge; args = $bridgeArguments; env = $environment }
    $config = [ordered]@{ mcpServers = [ordered]@{ motif = $server } }
    $registrationArgs = @()
    if ($Arm.host -eq 'codex') {
        $discovery = @'
Read the project through the registered Motif MCP tools. In code mode, discover their names with text(ALL_TOOLS.filter(t => t.name.startsWith('mcp__motif__'))), then call those names through tools in functions.exec. MCP resource lists do not list tools. The shell workspace contains no project data.
'@
        $instructions = if ($SystemAppend) { $discovery + "`n" + $SystemAppend } else { $discovery }
        $registrationArgs = @(
            '-c', ('mcp_servers.motif.command=' + ($bridge | ConvertTo-Json -Compress)),
            '-c', ('mcp_servers.motif.args=[' + (@($bridgeArguments | ForEach-Object { $_ | ConvertTo-Json -Compress }) -join ',') + ']'),
            '-c', ('mcp_servers.motif.env={' + (@($environment.Keys | ForEach-Object { $_ + '=' + ($environment[$_] | ConvertTo-Json -Compress) }) -join ',') + '}'),
            '-c', 'mcp_servers.motif.enabled=true', '-c', 'mcp_servers.motif.required=true',
            '-c', 'mcp_servers.motif.startup_timeout_sec=120', '-c', 'mcp_servers.motif.tool_timeout_sec=180'
        )
        $arguments = @(
            'exec', '--json', '-m', [string]$Arm.model, '--sandbox', 'read-only', '--ephemeral',
            '--skip-git-repo-check', '--ignore-user-config', '--ignore-rules', '-c', 'features.shell_snapshot=false',
            '--output-last-message', '/workspace/state/last-message.txt', '--cd', '/workspace/work',
            '-c', ('model_reasoning_effort=' + [string]$Arm.effort)
        ) + $registrationArgs + @(
            '-c', 'model_provider="isolated"', '-c', 'model_providers.isolated.name="Model service"',
            '-c', 'model_providers.isolated.base_url="http://127.0.0.1:8181/v1"',
            '-c', 'model_providers.isolated.wire_api="responses"',
            '-c', 'model_providers.isolated.requires_openai_auth=false'
        )
        $arguments += @('-c', ('developer_instructions=' + ($instructions | ConvertTo-Json -Compress)))
        $arguments += $Prompt
        $registration = @('mcp', 'list', '--json') + $registrationArgs
        $command = '/opt/host/client'
    } elseif ($Arm.host -eq 'claude') {
        $command = '/opt/host/client'
        $environment.ANTHROPIC_BASE_URL = 'http://127.0.0.1:8181'
        $arguments = @('-p', '--strict-mcp-config', '--mcp-config', '/workspace/config/mcp.json',
            '--output-format', 'stream-json', '--permission-mode', 'dontAsk', '--allowedTools', 'mcp__motif__*',
            '--max-turns', [string]$Limits.turns, '--model', [string]$Arm.model, '--effort', [string]$Arm.effort)
        if ($authMode -eq 'claude-plan') { $environment.CLAUDE_CONFIG_DIR = '/workspace/config/claude' }
        else {
            $environment.ANTHROPIC_API_KEY = 'local-session'
            $arguments = @('-p', '--bare') + $arguments[1..($arguments.Count - 1)]
        }
        if ($SystemAppend) { $arguments += @('--append-system-prompt', $SystemAppend) }
        $arguments += $Prompt
        $registration = @('mcp', 'list', '--strict-mcp-config', '--mcp-config', '/workspace/config/mcp.json')
    } elseif ($Arm.host -eq 'fake') {
        $command = '/opt/powershell/pwsh'
        $arguments = @('-NoProfile', '-File', '/opt/client/relay.ps1', '-RunManifest', '/workspace/config/session.json')
        $registration = @()
    } else { throw "Unsupported host '$($Arm.host)'." }
    return [ordered]@{ command = $command; arguments = $arguments; environment = $environment; authMode = $authMode
        registrationArguments = $registration; mcpConfig = $config; bridge = $bridge; bridgeArguments = $bridgeArguments }
}

Export-ModuleMember -Function New-ABHostPlan, Get-ABInferenceAuthMode
