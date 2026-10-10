"""Namespace and stdio-bridge integration without an agent or scoring."""
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from TrialBoundary import boundary


@unittest.skipUnless(sys.platform == 'linux' and shutil.which('bwrap'), 'Namespace bridge check requires Linux and bubblewrap')
class BridgeTests(unittest.TestCase):
    def test_powershell_starts_with_private_identity_home_and_cache(self):
        with tempfile.TemporaryDirectory() as directory:
            session = Path(directory) / 'session'
            (session / 'output').mkdir(parents=True)
            powershell = Path(shutil.which('pwsh')).resolve().parent
            config = {'session': str(session), 'mounts': [[str(powershell), '/opt/powershell']]}
            script = """$ErrorActionPreference='Stop'
if ([Environment]::UserName -ne 'workspace') { throw 'Missing synthetic identity' }
if ([Environment]::GetFolderPath('UserProfile') -ne $env:HOME) { throw 'Wrong private user home' }
if (-not (Test-Path $env:HOME)) { throw 'Missing private home' }
$cache=Join-Path $env:XDG_CACHE_HOME 'powershell'
if (-not (Test-Path $cache)) { throw 'Missing PowerShell cache' }
Set-Content (Join-Path $cache 'startup-check') 'ready'
if (@(Get-Content /etc/passwd).Count -ne 1) { throw 'Ambient user identities exposed' }
try { [IO.File]::WriteAllText('/etc/passwd','changed'); throw 'Identity is writable' }
catch [IO.IOException] { }
Write-Output 'boundary-shell-ready'
"""
            result = subprocess.run(boundary(config, ['/opt/powershell/pwsh', '-NoProfile', '-NonInteractive', '-Command', script]),
                                    stdin=subprocess.DEVNULL, capture_output=True, timeout=30)
            self.assertEqual(0, result.returncode, result.stderr.decode())
            self.assertEqual('boundary-shell-ready', result.stdout.decode().strip())

    def test_stdio_reaches_server_without_exposing_project_or_server_code(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            client = root / 'client'
            client.mkdir()
            sources = Path(__file__).resolve().parents[1] / 'tools'
            for source, destination in [('AgentBoundary.py', 'agent-boundary.py'), ('McpBridgeClient.py', 'stdio.py'),
                                        ('McpClient.psm1', 'protocol.psm1'), ('Invoke-ABFakeHost.ps1', 'relay.ps1')]:
                shutil.copyfile(sources / source, client / destination)
            (client / 'backend.py').write_text('''import json, sys
for line in sys.stdin:
 request = json.loads(line)
 if 'id' not in request: continue
 result = {'protocolVersion':'2024-11-05','capabilities':{'tools':{}},'serverInfo':{'name':'workspace','version':'1'}} if request['method']=='initialize' else {'tools':[{'name':'motif_read'}]}
 print(json.dumps({'jsonrpc':'2.0','id':request['id'],'result':result}),flush=True)
''')
            session = root / 'session'
            (session / 'output').mkdir(parents=True)
            (session / 'project').mkdir()
            (session / 'project/project.fwdata').write_text('protected project bytes')
            input_root = root / 'input'
            input_root.mkdir()
            check = '''import json, os, subprocess
assert not os.path.exists('/session/project/project.fwdata')
assert not os.path.exists('/opt/client/backend.py')
assert not os.path.exists('/input/session.json')
assert not os.path.exists('/home')
assert not os.listdir('/workspace/work')
shell=subprocess.run(['/opt/powershell/pwsh','-NoProfile','-NonInteractive','-Command',"[Environment]::UserName; [Environment]::GetFolderPath('UserProfile'); Set-Content ($env:XDG_CACHE_HOME + '/powershell/startup-check') ready"],capture_output=True,text=True,timeout=30)
assert shell.returncode==0, shell.stderr
assert shell.stdout.splitlines()==['workspace','/workspace/home'], shell.stdout
p=subprocess.Popen(['/usr/bin/python3','/opt/client/stdio.py','/channel/socket'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,text=True)
for i,method in enumerate(['initialize','tools/list']):
 p.stdin.write(json.dumps({'jsonrpc':'2.0','id':i,'method':method})+'\\n');p.stdin.flush()
 response=json.loads(p.stdout.readline())
assert response['result']['tools'][0]['name']=='motif_read'
p.stdin.close();assert p.wait(timeout=15)==0
'''
            plan = {'command': '/usr/bin/python3', 'arguments': ['-c', check], 'registrationArguments': [],
                    'environment': {'HOME': '/workspace/home', 'USERPROFILE': '/workspace/home',
                                    'CODEX_HOME': '/workspace/config/codex'}}
            config = {'plan': plan, 'host': 'fake', 'serverCommand': '/usr/bin/python3',
                      'serverArguments': ['/opt/client/backend.py']}
            (input_root / 'session.json').write_text(json.dumps(config))
            powershell = Path(shutil.which('pwsh')).resolve().parent
            outer = {'session': str(session), 'mounts': [[str(client), '/opt/client'], [str(input_root), '/input'],
                                                      [str(powershell), '/opt/powershell']]}
            result = subprocess.run(boundary(outer, ['/usr/bin/python3', '/opt/client/agent-boundary.py', '/input/session.json']),
                                    stdin=subprocess.DEVNULL, capture_output=True, timeout=60)
            self.assertEqual(0, result.returncode, result.stderr.decode())
            self.assertEqual([{'name': 'motif_read'}], json.loads((session / 'output/client-tools.json').read_text()))
            self.assertEqual([], json.loads((session / 'output/bridge-status.json').read_text())['errors'])
            records = [json.loads(line) for line in (session / 'output/mcp-transcript.jsonl').read_text().splitlines()]
            self.assertTrue(any(row['direction'] == 'sent' and row['payload']['method'] == 'tools/list' for row in records))
