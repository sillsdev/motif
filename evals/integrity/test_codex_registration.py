"""Exercise the shared plan against Codex without credentials or model sampling."""
from http.server import BaseHTTPRequestHandler, HTTPServer
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import unittest


@unittest.skipUnless(sys.platform == 'linux' and os.environ.get('MOTIF_CODEX_NATIVE'), 'Native Codex registration probe requires MOTIF_CODEX_NATIVE on Linux')
class CodexRegistrationTests(unittest.TestCase):
    def test_mcp_tools_reach_code_mode_registry_and_discovery_instructions(self):
        module = Path(__file__).resolve().parents[1] / 'tools/HostPlan.psm1'
        script = "Import-Module '" + str(module).replace("'", "''") + "'; New-ABHostPlan @{host='codex';model='gpt-6-luna';effort='high'} 'Read the workspace through tools.' @{turns=10} $null | ConvertTo-Json -Depth 20"
        plan = json.loads(subprocess.check_output(['pwsh', '-NoProfile', '-Command', script], stdin=subprocess.DEVNULL))
        requests = []

        class Handler(BaseHTTPRequestHandler):
            def do_POST(self):
                requests.append(json.loads(self.rfile.read(int(self.headers['Content-Length']))))
                if len(requests) == 1:
                    item = {'type': 'custom_tool_call', 'id': 'call_item', 'call_id': 'registry', 'name': 'exec',
                            'namespace': 'functions', 'input': "text(ALL_TOOLS.filter(t => t.name.startsWith('mcp__motif__')).map(t => t.name))"}
                    response = {'id': 'resp_local', 'object': 'response', 'status': 'completed', 'output': [item],
                                'usage': {'input_tokens': 1, 'output_tokens': 1, 'total_tokens': 2}}
                    events = [{'type': 'response.created', 'response': {'id': 'resp_local', 'status': 'in_progress', 'output': []}},
                              {'type': 'response.output_item.added', 'output_index': 0, 'item': item},
                              {'type': 'response.output_item.done', 'output_index': 0, 'item': item},
                              {'type': 'response.completed', 'response': response}]
                    body = ''.join('event: ' + row['type'] + '\ndata: ' + json.dumps(row) + '\n\n' for row in events).encode()
                    self.send_response(200)
                    self.send_header('Content-Type', 'text/event-stream')
                else:
                    body = b'{"error":{"message":"Offline registration check complete"}}'
                    self.send_response(400)
                self.send_header('Content-Length', str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def log_message(self, *args):
                pass

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            home = root / 'home'
            codex_home = root / 'config'
            home.mkdir()
            codex_home.mkdir()
            mock = root / 'server.py'
            mock.write_text('''import json, sys
for line in sys.stdin:
 request = json.loads(line)
 if 'id' not in request: continue
 method = request['method']
 result = {'protocolVersion':'2024-11-05','capabilities':{'tools':{}},'serverInfo':{'name':'motif','version':'1'}} if method == 'initialize' else {'tools':[{'name':'motif_probe','description':'Read the workspace','inputSchema':{'type':'object','properties':{}}}]} if method == 'tools/list' else {}
 print(json.dumps({'jsonrpc':'2.0','id':request['id'],'result':result}),flush=True)
''')
            server = HTTPServer(('127.0.0.1', 0), Handler)
            serving = threading.Thread(target=server.serve_forever, daemon=True)
            serving.start()

            def mapped(arguments):
                result = []
                for value in arguments:
                    if value.startswith('mcp_servers.motif.args='):
                        value = 'mcp_servers.motif.args=[' + json.dumps(str(mock)) + ']'
                    elif value.startswith('model_providers.isolated.base_url='):
                        value = 'model_providers.isolated.base_url="http://127.0.0.1:' + str(server.server_port) + '/v1"'
                    elif value == '/workspace/work':
                        value = str(root)
                    elif value == '/workspace/state/last-message.txt':
                        value = str(root / 'last-message.txt')
                    result.append(value)
                return result

            native = os.environ['MOTIF_CODEX_NATIVE']
            env = {'PATH': '/usr/bin:/bin', 'HOME': str(home), 'USERPROFILE': str(home),
                   'CODEX_HOME': str(codex_home), 'LANG': 'C.UTF-8'}
            try:
                registered = subprocess.run([native] + mapped(plan['registrationArguments']), cwd=root, env=env,
                                            stdin=subprocess.DEVNULL, capture_output=True, timeout=60)
                self.assertEqual(0, registered.returncode, registered.stderr.decode())
                motif = [row for row in json.loads(registered.stdout) if row['name'] == 'motif']
                self.assertEqual(1, len(motif))
                self.assertTrue(motif[0]['enabled'])
                self.assertEqual(120, motif[0]['startup_timeout_sec'])
                execution = subprocess.run([native] + mapped(plan['arguments']), cwd=root, env=env,
                                           stdin=subprocess.DEVNULL, capture_output=True, timeout=60)
                self.assertEqual(1, execution.returncode, execution.stderr.decode())
                self.assertEqual(2, len(requests), execution.stdout.decode())
                self.assertIn('discover their names with text(ALL_TOOLS', json.dumps(requests[0]))
                outputs = [item for item in requests[1]['input'] if item.get('type') == 'custom_tool_call_output']
                self.assertIn('mcp__motif__motif_probe', json.dumps(outputs))
            finally:
                server.shutdown()
                server.server_close()
                serving.join(timeout=2)
