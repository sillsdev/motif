"""Inner agent containment and a server-side MCP bridge within the sealed trial."""
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import threading
import time

LIMIT = 16 * 1024 * 1024


def agent_boundary(plan, command):
    args = ["/usr/bin/bwrap", "--unshare-all", "--share-net", "--die-with-parent", "--new-session",
            "--cap-drop", "ALL", "--clearenv"]
    for name in ("passwd", "group", "nsswitch.conf"):
        args += ["--ro-bind", "/opt/system/agent/" + name, "/etc/" + name]
    for name in ("possible", "present", "online"):
        source = "/sys/devices/system/cpu/" + name
        if Path(source).exists():
            args += ["--ro-bind", source, source]
    for path in ("/usr/lib", "/usr/lib64", "/lib", "/lib64", "/opt/powershell"):
        if Path(path).exists():
            args += ["--ro-bind", path, path]
    for path in ("/usr/bin/python3", "/usr/bin/bash", "/usr/bin/sh"):
        if Path(path).exists():
            args += ["--ro-bind", path, path]
    args += ["--symlink", "usr/bin", "/bin"]
    if Path("/opt/host").exists():
        args += ["--ro-bind", "/opt/host", "/opt/host"]
    for name in ("stdio.py", "protocol.psm1", "relay.ps1"):
        args += ["--ro-bind", "/opt/client/" + name, "/opt/client/" + name]
    args += ["--ro-bind", "/input/actions.jsonl", "/opt/client/actions.jsonl"] if Path("/input/actions.jsonl").exists() else []
    args += ["--ro-bind", "/session/channel/socket", "/channel/socket", "--bind", "/session/agent", "/workspace",
             "--proc", "/proc", "--dev", "/dev", "--tmpfs", "/var/lock", "--tmpfs", "/tmp"]
    claude_credentials = Path("/session/agent/config/claude/.credentials.json")
    if claude_credentials.is_file():
        args += ["--ro-bind", str(claude_credentials), "/workspace/config/claude/.credentials.json"]
    env = {"PATH": "/opt/powershell:/usr/bin:/bin", "LANG": "C.UTF-8", "TMPDIR": "/tmp",
           "POWERSHELL_TELEMETRY_OPTOUT": "1", "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "1",
           "XDG_DATA_HOME": "/workspace/state/data", "XDG_CACHE_HOME": "/workspace/state/cache",
           "DOTNET_CLI_HOME": "/workspace/state/dotnet",
           "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1"}
    env.update(plan["environment"])
    for key, value in env.items():
        args += ["--setenv", key, value]
    return args + ["--remount-ro", "/", "--chdir", "/workspace/work"] + command


class ToolBridge:
    def __init__(self, command, arguments):
        self.command = command
        self.arguments = arguments
        self.errors = []
        self.processes = []
        self.threads = []
        self.lock = threading.Lock()
        self.socket = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        Path("/session/channel").mkdir(exist_ok=True)
        self.socket.bind("/session/channel/socket")
        self.socket.listen(8)
        self.socket.settimeout(0.5)
        self.stop = threading.Event()
        self.acceptor = threading.Thread(target=self.accept, daemon=True)
        self.acceptor.start()

    def log(self, direction, payload):
        with self.lock, Path("/session/output/mcp-transcript.jsonl").open("a") as output:
            output.write(json.dumps({"atUtc": time.time(), "source": "mcp-client", "direction": direction, "payload": payload}) + "\n")

    def accept(self):
        while not self.stop.is_set():
            try:
                connection, _ = self.socket.accept()
            except socket.timeout:
                continue
            thread = threading.Thread(target=self.session, args=(connection,), daemon=True)
            self.threads.append(thread)
            thread.start()

    def session(self, connection):
        process = subprocess.Popen([self.command] + self.arguments, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                   stderr=subprocess.PIPE, cwd="/session/output", close_fds=True)
        self.processes.append(process)
        pending = {}
        stderr = []

        def drain_errors():
            stderr.append(process.stderr.read())

        def receive():
            try:
                for line in process.stdout:
                    if len(line) > LIMIT:
                        raise RuntimeError("Oversized tool response")
                    payload = json.loads(line)
                    self.log("received", payload)
                    if pending.get(payload.get("id")) == "tools/list":
                        tools = payload.get("result", {}).get("tools")
                        if not isinstance(tools, list):
                            raise RuntimeError("The agent did not receive a tool list")
                        Path("/session/output/client-tools.json").write_text(json.dumps(tools))
                    connection.sendall(line)
            except Exception as error:
                self.errors.append(str(error))

        reader = threading.Thread(target=receive, daemon=True)
        error_reader = threading.Thread(target=drain_errors, daemon=True)
        reader.start()
        error_reader.start()
        try:
            with connection.makefile("rb") as incoming:
                while True:
                    line = incoming.readline(LIMIT + 1)
                    if not line:
                        break
                    if len(line) > LIMIT or not line.endswith(b"\n"):
                        raise RuntimeError("Malformed tool request")
                    payload = json.loads(line)
                    pending[payload.get("id")] = payload.get("method")
                    self.log("sent", payload)
                    process.stdin.write(line)
                    process.stdin.flush()
        except Exception as error:
            self.errors.append(str(error))
        finally:
            process.stdin.close()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
                self.errors.append("Tool server did not close")
            reader.join(timeout=5)
            error_reader.join(timeout=5)
            if reader.is_alive() or error_reader.is_alive():
                self.errors.append('Tool evidence channels did not close')
            connection.close()
            with self.lock, Path("/session/output/mcp-stderr.log").open("ab") as output:
                output.write(b"".join(stderr))
            if process.returncode:
                self.errors.append("Tool server exited with " + str(process.returncode))

    def close(self):
        self.stop.set()
        self.acceptor.join(timeout=2)
        self.socket.close()
        for thread in self.threads:
            thread.join(timeout=20)
        if any(thread.is_alive() for thread in self.threads) or any(p.poll() is None for p in self.processes):
            self.errors.append("Tool connection remained alive")
        Path('/session/channel/socket').unlink(missing_ok=True)


def main(config):
    for directory in ("work", "home/.config/powershell", "config/codex", "config/claude",
                      "state/data", "state/cache/powershell", "state/dotnet"):
        (Path("/session/agent") / directory).mkdir(parents=True, exist_ok=True)
    plan = config["plan"]
    bridge = ToolBridge(config["serverCommand"], config["serverArguments"])
    try:
        if config.get('preflight'):
            blocked = ["/session", "/input", "/opt/product", "/opt/runtime", "/home", "/mnt", "/workspace/run-manifest.json"]
            check = "import os; assert not any(os.path.exists(p) for p in " + repr(blocked) + "); assert all(os.environ.get(k,'').startswith('/workspace/') for k in ['HOME','USERPROFILE','CODEX_HOME']); assert not os.listdir('/workspace/work')"
            subprocess.run(agent_boundary(plan, ["/usr/bin/python3", "-c", check]), check=True, stdin=subprocess.DEVNULL)
            return 0
        if plan["registrationArguments"]:
            registration = subprocess.run(agent_boundary(plan, [plan["command"]] + plan["registrationArguments"]),
                                          stdin=subprocess.DEVNULL, capture_output=True, timeout=120)
            Path("/session/output/mcp-registration.json").write_text(json.dumps({"exitCode": registration.returncode,
                "stdout": registration.stdout.decode(errors="replace"), "stderr": registration.stderr.decode(errors="replace")}))
            if registration.returncode:
                raise RuntimeError("Host MCP registration failed")
            if config["host"] == "codex":
                records = json.loads(registration.stdout)
                motif = [row for row in records if row.get("name") == "motif"]
                if len(motif) != 1 or not motif[0].get("enabled") or motif[0].get("transport", {}).get("command") != plan["bridge"]:
                    raise RuntimeError("Codex did not register the intended MCP bridge")
        result = subprocess.run(agent_boundary(plan, [plan["command"]] + plan["arguments"]), stdin=subprocess.DEVNULL)
    finally:
        bridge.close()
        Path("/session/output/bridge-status.json").write_text(json.dumps({"errors": bridge.errors}))
    return result.returncode


if __name__ == "__main__":
    config = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
    sys.exit(main(config))
