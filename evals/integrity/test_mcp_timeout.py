"""A delayed tool response exercises the real fake host and MCP client."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


@unittest.skipUnless(shutil.which('pwsh'), 'The fake host requires PowerShell')
class McpTimeoutTests(unittest.TestCase):
    def host(self, timeout_ms, expect_denied=False):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            tools = Path(__file__).resolve().parents[1] / 'tools'
            shutil.copyfile(tools / 'Invoke-ABFakeHost.ps1', root / 'relay.ps1')
            shutil.copyfile(tools / 'McpClient.psm1', root / 'protocol.psm1')
            (root / 'server.py').write_text('''import json, sys, time
for line in sys.stdin:
 r=json.loads(line)
 if 'id' not in r: continue
 if r['method']=='initialize': result={'capabilities':{'tools':{}}}
 elif r['method']=='tools/list': result={'tools':[{'name':'motif_finalize_proposal'}]}
 else:
  time.sleep(0.3)
  result={'content':[], 'structuredContent':{'result':{}}}
 print(json.dumps({'jsonrpc':'2.0','id':r['id'],'result':result}),flush=True)
''')
            (root / 'actions.jsonl').write_text(json.dumps({'tool': 'motif_finalize_proposal', 'arguments': {}, 'expectDenied': expect_denied}) + '\n')
            manifest = {'server': {'command': shutil.which('python3'), 'arguments': [str(root / 'server.py')]},
                        'agentDirectory': str(root), 'childEnvironment': {}, 'transcriptPath': str(root / 'transcript.jsonl'),
                        'fakeScript': str(root / 'actions.jsonl'), 'task': {'limits': {'turns': 10}},
                        'hostResultPath': str(root / 'host.json'), 'mcpTimeoutMs': timeout_ms}
            (root / 'session.json').write_text(json.dumps(manifest))
            process = subprocess.run(['pwsh', '-NoProfile', '-File', str(root / 'relay.ps1'), '-RunManifest', str(root / 'session.json')],
                                     stdin=subprocess.DEVNULL, capture_output=True, timeout=20)
            self.assertTrue((root / 'host.json').exists(), process.stderr.decode())
            result = json.loads((root / 'host.json').read_text(encoding='utf-8-sig'))
            return process.returncode, result

    def test_configured_deadline_expires_with_typed_timeout(self):
        code, result = self.host(50)
        self.assertEqual(1, code)
        self.assertEqual('mcp_timeout', result['failureKind'])
        self.assertIn('50 ms', result['failure'])

    def test_longer_deadline_accepts_delayed_response(self):
        code, result = self.host(2000)
        self.assertEqual(0, code, result)
        self.assertIsNone(result['failure'])

    def test_expect_denied_does_not_swallow_transport_timeout(self):
        code, result = self.host(50, expect_denied=True)
        self.assertEqual(1, code)
        self.assertEqual('mcp_timeout', result['failureKind'])

    def test_control_timeout_override_and_validation(self):
        module = Path(__file__).resolve().parents[1] / 'tools/ABHarness.psm1'
        for value, expected in [('', 200000), ('320000', 320000), ('0', None), ('-1', None), ('1.5', None), ('2147483648', None)]:
            with self.subTest(value=value):
                env = dict(os.environ, MOTIF_FAKE_MCP_TIMEOUT_MS=value)
                result = subprocess.run(['pwsh', '-NoProfile', '-Command',
                    "$ErrorActionPreference='Stop'; Import-Module '" + str(module).replace("'", "''") + "'; Get-ABFakeMcpTimeoutMs"],
                    env=env, stdin=subprocess.DEVNULL, capture_output=True, timeout=20)
                if expected is None:
                    self.assertNotEqual(0, result.returncode)
                    self.assertIn(b'positive integer', result.stderr)
                else:
                    self.assertEqual(0, result.returncode, result.stderr.decode())
                    self.assertEqual(expected, int(result.stdout.decode().strip()))

    def test_failure_classes_remain_in_report_denominator_and_schema(self):
        repo = Path(__file__).resolve().parents[2]
        module = repo / 'evals/tools/ABHarness.psm1'
        result = subprocess.run(['pwsh', '-NoProfile', '-Command',
            "Import-Module '" + str(module).replace("'", "''") + "'; "
            "$rows=@(@{integrity=@{state='clean'}},@{integrity=@{state='clean';failureClass='cloud_failure'}},@{integrity=@{state='isolation_failure'}}); "
            'Get-ABIntegritySummary $rows | ConvertTo-Json -Depth 10 -Compress'],
            stdin=subprocess.DEVNULL, capture_output=True, timeout=20)
        self.assertEqual(0, result.returncode, result.stderr.decode())
        summary = json.loads(result.stdout)
        self.assertEqual(3, summary['attempted'])
        self.assertEqual(2, summary['counts']['clean'])
        self.assertEqual(1, summary['counts']['isolation_failure'])
        self.assertEqual(1, summary['failureClasses']['cloud_failure'])
        self.assertEqual(1 / 3, summary['scorableRate'])
        self.assertIsNotNone(summary['confidenceInterval95'])
        schema = json.loads((repo / 'evals/integrity/Manifest.schema.json').read_text())
        self.assertEqual(['passed', 'failed', 'inconclusive', 'judge_disagreement', 'review', 'invalid',
                          'isolation_failure', 'cloud_failure', 'harness_defect'], schema['properties']['status']['enum'])
        self.assertEqual(['clean', 'review', 'invalid', 'isolation_failure'],
                         schema['properties']['integrity']['properties']['state']['enum'])
        self.assertIn('cloud_failure', schema['properties']['status']['enum'])
        self.assertIn('cloud_failure', schema['properties']['failureClass']['enum'])
