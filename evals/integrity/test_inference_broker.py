"""Control-side ChatGPT inference uses fake responses and disposable credentials."""
import base64
import contextlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch
import urllib.error

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from InferenceBroker import Broker, CloudFailure, InferenceFailure, LOGIN_FAILURE, NoRedirect, chatgpt_credentials, inference_upstream
from TrialBoundary import environment, export, run


class Response(io.BytesIO):
    def __init__(self, content=b'{}', code=200, content_type='application/json'):
        super().__init__(content)
        self.code = code
        self.headers = {'Content-Type': content_type}


class BrokerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.auth = self.root / 'controller'
        self.auth.mkdir()
        self.archive = self.root / 'archive'
        self.archive.mkdir()
        self.env = patch.dict(os.environ, CODEX_HOME=str(self.auth), MOTIF_INFERENCE_CODEX_AUTH='chatgpt',
                              MOTIF_INFERENCE_OPENAI_KEY='UNUSED-API-KEY', HTTP_PROXY='http://forbidden.invalid',
                              HTTPS_PROXY='http://forbidden.invalid', ALL_PROXY='http://forbidden.invalid')
        self.env.start()
        self.addCleanup(self.env.stop)
        self.token = self.login()
        self.channel = patch.object(Broker, 'start_channel')
        self.channel.start()
        self.addCleanup(self.channel.stop)

    def login(self, expiry=None, suffix='SENTINEL-ACCESS-TOKEN'):
        claims = base64.urlsafe_b64encode(json.dumps({'exp': expiry if expiry is not None else time.time() + 3600}).encode()).decode().rstrip('=')
        token = 'eyJhbGciOiJub25lIn0.' + claims + '.' + suffix
        (self.auth / 'auth.json').write_text(json.dumps({'tokens': {'access_token': token, 'account_id': 'SENTINEL-ACCOUNT',
                                                                 'refresh_token': 'SENTINEL-REFRESH', 'id_token': 'SENTINEL-ID'}}))
        return token

    def broker(self):
        return Broker('codex', 'gpt-6-luna', self.archive)

    def frame(self, **values):
        document = {'model': 'gpt-6-luna', 'input': [], 'stream': False, 'store': True}
        document.update(values)
        return {'method': 'POST', 'path': '/v1/responses',
                'headers': {'Authorization': 'Bearer trial-value', 'chatgpt-account-id': 'trial-account',
                            'session_id': 'trial-session', 'Host': 'forbidden.invalid', 'originator': 'trial-origin'},
                'body': base64.b64encode(json.dumps(document).encode()).decode()}

    def test_injected_headers_body_and_fixed_destination_with_buffered_stream(self):
        broker = self.broker()
        stream = b'event: response.completed\ndata: {"type":"response.completed"}\n\n'
        with patch.object(broker.opener, 'open', return_value=Response(stream, content_type='text/event-stream')) as opened:
            status, headers, content = broker.forward(self.frame())
        self.assertEqual((200, {'Content-Type': 'text/event-stream'}, stream), (status, headers, content))
        request = opened.call_args.args[0]
        self.assertEqual('https://chatgpt.com/backend-api/codex/responses', request.full_url)
        injected = {key.lower(): value for key, value in request.header_items()}
        self.assertEqual('Bearer ' + self.token, injected['authorization'])
        self.assertEqual('SENTINEL-ACCOUNT', injected['chatgpt-account-id'])
        self.assertEqual('responses=experimental', injected['openai-beta'])
        self.assertEqual('codex_cli_rs', injected['originator'])
        self.assertEqual(broker.session_id, injected['session_id'])
        self.assertNotIn('host', injected)
        document = json.loads(request.data)
        self.assertTrue(document['stream'])
        self.assertFalse(document['store'])
        self.assertEqual('gpt-6-luna', document['model'])
        self.assertNotIn(self.token, json.dumps(vars(broker), default=str))
        row = json.loads((self.archive / 'network.jsonl').read_text())
        self.assertEqual('chatgpt-plan', row['authMode'])

    def test_login_is_read_again_on_every_request_and_not_cached(self):
        broker = self.broker()
        seen = []
        def respond(request, **kwargs):
            seen.append(request.get_header('Authorization'))
            return Response()
        with patch.object(broker.opener, 'open', side_effect=respond):
            broker.forward(self.frame())
            replacement = self.login(suffix='ROTATED-SENTINEL')
            broker.forward(self.frame())
            self.login(expiry=1)
            with self.assertRaisesRegex(CloudFailure, 'run codex on the host to refresh the login'):
                broker.forward(self.frame())
        self.assertEqual(['Bearer ' + self.token, 'Bearer ' + replacement], seen)

    def test_missing_expired_and_malformed_logins_are_cloud_failures(self):
        for contents in (None, '{}', 'malformed SENTINEL-ACCESS-TOKEN', '{"tokens":[]}',
                         '{"tokens":{"access_token":"opaque","account_id":"a"}}'):
            with self.subTest(contents=contents):
                path = self.auth / 'auth.json'
                if contents is None:
                    path.unlink(missing_ok=True)
                else:
                    path.write_text(contents)
                self.assert_login_failure()
        for expiry in (1, True, float('nan'), float('inf')):
            with self.subTest(expiry=expiry):
                self.login(expiry=expiry)
                self.assert_login_failure()

    def assert_login_failure(self):
        session = self.root / 'session'
        (session / 'project').mkdir(parents=True, exist_ok=True)
        config = {'archive': str(self.archive), 'session': str(session), 'host': 'codex', 'model': 'gpt-6-luna',
                  'protected': [], 'mounts': []}
        with patch('TrialBoundary.shutil.which', return_value='/available'), \
                patch('TrialBoundary.audited_run', side_effect=lambda *a: self.broker()):
            result = run(config)
        self.assertEqual('clean', result['state'])
        self.assertEqual('cloud_failure', result['failureClass'])
        self.assertEqual(LOGIN_FAILURE, result['failureEvidence'][0]['evidence'])
        self.assertEqual('chatgpt-plan', result['network']['authMode'])
        self.assertEqual('chatgpt.com', result['network']['endpoint'])
        self.assertNotIn('SENTINEL', json.dumps(result))

    def test_home_default_uses_controller_home(self):
        with patch.dict(os.environ, {}, clear=True), patch('InferenceBroker.Path.home', return_value=self.root):
            (self.root / '.codex').mkdir()
            (self.auth / 'auth.json').replace(self.root / '.codex/auth.json')
            self.assertEqual((self.token, 'SENTINEL-ACCOUNT'), chatgpt_credentials())

    def test_refusals_never_open_upstream_or_read_credentials(self):
        broker = self.broker()
        (self.auth / 'auth.json').unlink()
        with patch.object(broker.opener, 'open') as opened:
            for path in ('/v1/responses/compact', '/v1/models', '/v1/responses?x=1',
                         'https://evil.invalid/v1/responses', '//evil.invalid/v1/responses'):
                frame = self.frame()
                frame['path'] = path
                self.assertEqual(403, broker.forward(frame)[0])
            self.assertEqual(403, broker.forward(self.frame(model='other-model'))[0])
            frame = self.frame()
            frame['method'] = 'GET'
            self.assertEqual(403, broker.forward(frame)[0])
        opened.assert_not_called()
        self.assertEqual(7, len(broker.denials))

    def test_proxies_redirects_and_auth_mode_typos_fail_closed(self):
        with patch('InferenceBroker.urllib.request.build_opener') as build:
            self.broker()
        proxy, redirect = build.call_args.args
        self.assertEqual({}, proxy.proxies)
        self.assertIsInstance(redirect, NoRedirect)
        self.assertIsNone(redirect.redirect_request(None, None, 302, 'redirect', {}, 'https://evil.invalid'))
        with patch.dict(os.environ, MOTIF_INFERENCE_CODEX_AUTH='typo'):
            with self.assertRaises(InferenceFailure):
                self.broker()
        with patch.dict(os.environ, {}, clear=True):
            self.assertEqual(('codex', 'api-key'), inference_upstream('codex'))
        self.assertEqual(('claude', 'api-key'), inference_upstream('claude'))
        self.assertEqual((None, 'none'), inference_upstream('fake'))

    def test_api_key_route_keeps_original_body_and_compact(self):
        broker = Broker('codex', 'gpt-6-luna', self.archive, 'api-key')
        frame = self.frame()
        frame['path'] = '/v1/responses/compact'
        with patch.object(broker.opener, 'open', return_value=Response()) as opened:
            self.assertEqual(200, broker.forward(frame)[0])
        request = opened.call_args.args[0]
        self.assertEqual('https://api.openai.com/v1/responses/compact', request.full_url)
        self.assertEqual('Bearer UNUSED-API-KEY', request.get_header('Authorization'))
        self.assertEqual(base64.b64decode(frame['body']), request.data)

    def test_claude_plan_forwards_only_authorized_message_requests_to_anthropic(self):
        broker = Broker('claude', 'claude-haiku-5-5', self.archive, 'claude-plan')
        token = 'OWNER-SUBSCRIPTION-TOKEN'
        document = {'model': 'claude-haiku-5-5', 'messages': [{'role': 'user', 'content': 'hello'}]}
        frame = {'method': 'POST', 'path': '/v1/messages', 'headers': {
            'Authorization': 'Bearer ' + token, 'anthropic-version': '2023-06-01',
            'anthropic-beta': 'oauth-2025-04-20', 'Host': 'trial.invalid'},
            'body': base64.b64encode(json.dumps(document).encode()).decode()}
        echoed = Response(json.dumps({'text': token}).encode())
        with patch.object(broker.opener, 'open', return_value=echoed) as opened:
            status, _, body = broker.forward(frame)
        request = opened.call_args.args[0]
        headers = {key.lower(): value for key, value in request.header_items()}
        self.assertEqual(200, status)
        self.assertEqual('https://api.anthropic.com/v1/messages', request.full_url)
        self.assertEqual('Bearer ' + token, headers['authorization'])
        self.assertEqual('2023-06-01', headers['anthropic-version'])
        self.assertEqual('oauth-2025-04-20', headers['anthropic-beta'])
        self.assertNotIn('host', headers)
        self.assertNotIn(token, body.decode())
        self.assertNotIn(token, json.dumps(vars(broker), default=str))
        self.assertEqual('claude-plan', json.loads((self.archive / 'network.jsonl').read_text())['authMode'])

    def test_claude_plan_rejects_missing_login_and_unapproved_routes(self):
        broker = Broker('claude', 'claude-haiku-5-5', self.archive, 'claude-plan')
        frame = {'method': 'POST', 'path': '/v1/messages', 'headers': {},
                 'body': base64.b64encode(json.dumps({'model': 'claude-haiku-5-5'}).encode()).decode()}
        with self.assertRaisesRegex(CloudFailure, 'subscription login'):
            broker.forward(frame)
        frame['headers'] = {'Authorization': 'Bearer trial-token'}
        frame['path'] = '/v1/models'
        self.assertEqual(403, broker.forward(frame)[0])
        self.assertEqual(['inference route or request limit refused'], broker.denials)

    def serve_frame(self, broker, frame):
        incoming = self.root / 'request.jsonl'
        outgoing = self.root / 'trial-response.jsonl'
        incoming.write_text(json.dumps(frame) + '\n')
        broker.request_read = os.open(incoming, os.O_RDONLY)
        broker.response_write = os.open(outgoing, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
        broker.serve()
        return json.loads(outgoing.read_text())

    def test_echoed_credentials_never_reach_trial_archive_transcript_or_logs(self):
        broker = self.broker()
        stdout, stderr = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr), \
                patch.object(broker.opener, 'open', return_value=Response((self.token + ' SENTINEL-ACCOUNT').encode())):
            response = self.serve_frame(broker, self.frame())
        trial_content = base64.b64decode(response['body']).decode()
        session = self.root / 'session'
        output = session / 'output'
        output.mkdir(parents=True)
        (session / 'project').mkdir()
        host = {'exitCode': 0, 'finalMessage': trial_content}
        for name, content in {'host.json': json.dumps(host), 'activity.jsonl': '', 'mcp-transcript.jsonl': '',
                              'client-tools.json': '[]', 'bridge-status.json': '{"errors":[]}'}.items():
            (output / name).write_text(content)
        (self.archive / 'host.stdout.log').write_text(json.dumps({'source': 'host-summary', 'payload': host}) + '\n')
        (self.archive / 'host.stderr.log').write_text(stderr.getvalue())
        export({'archive': str(self.archive), 'session': str(session)})
        for path in list(self.archive.rglob('*')) + list(session.rglob('*')) + [self.root / 'trial-response.jsonl']:
            if path.is_file():
                for secret in (self.token, 'SENTINEL-ACCESS-TOKEN', 'SENTINEL-ACCOUNT', 'SENTINEL-REFRESH', 'SENTINEL-ID', 'UNUSED-API-KEY'):
                    self.assertNotIn(secret.encode(), path.read_bytes(), str(path))
        self.assertEqual('', stdout.getvalue())
        self.assertEqual('', stderr.getvalue())
        self.assertEqual([], broker.errors)
        self.assertEqual('[redacted] [redacted]', trial_content)

    def test_backend_401_and_transport_failures_use_safe_pipe_diagnostics(self):
        for failure in ('401', 'network', 'expired'):
            with self.subTest(failure=failure):
                broker = self.broker()
                if failure == '401':
                    error = urllib.error.HTTPError('https://chatgpt.com/' + self.token, 401, self.token, {}, io.BytesIO(self.token.encode()))
                elif failure == 'network':
                    error = urllib.error.URLError(self.token)
                else:
                    self.login(expiry=1)
                    error = None
                with patch.object(broker.opener, 'open', side_effect=error) as opened:
                    response = self.serve_frame(broker, self.frame())
                self.assertEqual(503, response['status'])
                message = json.loads(base64.b64decode(response['body']))['error']['message']
                self.assertEqual('Inference upstream request failed' if failure == 'network' else LOGIN_FAILURE, message)
                self.assertEqual([message], broker.failures)
                self.assertEqual([], broker.errors)
                self.assertNotIn(self.token, json.dumps(response))
                if failure == 'expired':
                    opened.assert_not_called()
                self.login()

    def test_truncated_upstream_stream_is_cloud_failure_without_credentials(self):
        broker = self.broker()
        response = Response()
        with patch.object(response, 'read', side_effect=OSError(self.token)), \
                patch.object(broker.opener, 'open', return_value=response):
            result = self.serve_frame(broker, self.frame())
        self.assertEqual(503, result['status'])
        self.assertEqual(['Inference upstream response failed'], broker.failures)
        self.assertEqual([], broker.errors)
        self.assertNotIn(self.token.encode(), base64.b64decode(result['body']))


class HostPlanAuthTests(unittest.TestCase):
    def test_plan_modes_survive_sanitized_environment_without_credentials(self):
        repo = Path(__file__).resolve().parents[2]
        module = repo / 'evals/tools/HostPlan.psm1'
        script = "Import-Module '" + str(module).replace("'", "''") + "'; " + '''
$arm=@{host='codex';model='gpt-6-luna';effort='high'}
$arm.authMode=Get-ABInferenceAuthMode $arm
Remove-Item Env:MOTIF_INFERENCE_CODEX_AUTH -ErrorAction SilentlyContinue
New-ABHostPlan $arm 'Read the workspace.' @{turns=10} $null | ConvertTo-Json -Depth 20
'''
        for mode, expected in [('chatgpt', 'chatgpt-plan'), ('api-key', 'api-key')]:
            with self.subTest(mode=mode):
                env = dict(os.environ, MOTIF_INFERENCE_CODEX_AUTH=mode, MOTIF_INFERENCE_OPENAI_KEY='PLAN-SENTINEL',
                           CODEX_HOME='/controller/PLAN-SENTINEL')
                process = subprocess.run(['pwsh', '-NoProfile', '-Command', script], env=env,
                                         stdin=subprocess.DEVNULL, capture_output=True, timeout=30)
                self.assertEqual(0, process.returncode, process.stderr.decode())
                plan = json.loads(process.stdout)
                self.assertEqual(expected, plan['authMode'])
                self.assertEqual('/workspace/config/codex', plan['environment']['CODEX_HOME'])
                self.assertIn('model_providers.isolated.requires_openai_auth=false', plan['arguments'])
                self.assertNotIn('MOTIF_INFERENCE_CODEX_AUTH', plan['environment'])
                self.assertNotIn(b'PLAN-SENTINEL', process.stdout + process.stderr)
        self.assertNotIn('MOTIF_INFERENCE_CODEX_AUTH', environment())

    def test_claude_subscription_plan_uses_owner_config_without_api_key_fallback(self):
        repo = Path(__file__).resolve().parents[2]
        module = repo / 'evals/tools/HostPlan.psm1'
        script = "Import-Module '" + str(module).replace("'", "''") + "'; " + '''
$arm=@{host='claude';model='claude-haiku-5-5';effort='high';authMode='claude-plan'}
New-ABHostPlan $arm 'Read the project.' @{turns=10} $null | ConvertTo-Json -Depth 20
'''
        process = subprocess.run(['pwsh', '-NoProfile', '-Command', script], stdin=subprocess.DEVNULL,
                                 capture_output=True, timeout=30)
        self.assertEqual(0, process.returncode, process.stderr.decode())
        plan = json.loads(process.stdout)
        self.assertEqual('claude-plan', plan['authMode'])
        self.assertEqual('http://127.0.0.1:8181', plan['environment']['ANTHROPIC_BASE_URL'])
        self.assertEqual('/workspace/config/claude', plan['environment']['CLAUDE_CONFIG_DIR'])
        self.assertNotIn('ANTHROPIC_API_KEY', plan['environment'])
        self.assertNotIn('--bare', plan['arguments'])
        self.assertIn('--model', plan['arguments'])
        self.assertIn('claude-haiku-5-5', plan['arguments'])

    @unittest.skipUnless(os.environ.get('MOTIF_TEST_GRAMMARS'), 'Dry run requires the external pinned grammar root')
    def test_controller_dry_run_records_plan_billing_mode_without_login(self):
        repo = Path(__file__).resolve().parents[2]
        env = dict(os.environ, MOTIF_INFERENCE_CODEX_AUTH='chatgpt', CODEX_HOME='/missing/DRY-RUN-SENTINEL',
                   MOTIF_INFERENCE_OPENAI_KEY='DRY-RUN-SENTINEL')
        process = subprocess.run(['pwsh', '-NoProfile', '-File', str(repo / 'evals/Invoke-ABQuestion.ps1'),
                                  '-Question', 'evals/questions/default-vs-lean.yaml', '-Trials', '1',
                                  '-Tasks', 't0-diagnose-noun-slot-order', '-DryRun'], cwd=repo, env=env, stdin=subprocess.DEVNULL,
                                 capture_output=True, timeout=60)
        self.assertEqual(0, process.returncode, process.stderr.decode())
        self.assertEqual(2, process.stdout.count(b'auth chatgpt-plan'))
        self.assertNotIn(b'DRY-RUN-SENTINEL', process.stdout + process.stderr)

    def test_controller_report_keeps_billing_mode_for_setup_failures_without_secrets(self):
        import shutil
        source = Path(__file__).resolve().parents[2]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            repo = root / 'repo'
            tools = repo / 'evals/tools'
            tools.mkdir(parents=True)
            for path in (source / 'evals/tools').glob('*.psm1'):
                shutil.copyfile(path, tools / path.name)
            shutil.copyfile(source / 'evals/tools/Invoke-ABTrial.ps1', tools / 'Invoke-ABTrial.ps1')
            shutil.copyfile(source / 'evals/Invoke-ABQuestion.ps1', repo / 'evals/Invoke-ABQuestion.ps1')
            arms = repo / 'evals/arms'
            arms.mkdir()
            (repo / 'evals/profile.json').write_text('{"tools":[]}')
            (repo / 'CONTEXT.md').write_text('Fixture glossary.\n')
            (repo / 'build.ps1').write_text('exit 0\n')
            product = repo / 'bin/Debug'
            product.mkdir(parents=True)
            fake_executable = product / 'motif'
            fake_executable.write_text('#!/bin/sh\nexit 1\n')
            fake_executable.chmod(0o755)
            fake_parser = product / 'pangloss'
            fake_parser.write_text('#!/bin/sh\nexit 1\n')
            fake_parser.chmod(0o755)
            for name in ('a', 'b'):
                (arms / (name + '.yaml')).write_text(json.dumps({'id': name, 'host': 'codex', 'model': 'gpt-6-luna',
                    'effort': 'high', 'server': {'path': str(fake_executable), 'profile': 'profile.json'}}))
            (repo / 'evals/question.yaml').write_text(json.dumps({'question': 'Offline report regression',
                'arm_a': 'a', 'arm_b': 'b', 'tasks': ['offline'], 'trials': 1, 'primary_metric': 'task-success'}))
            task = root / 'public/set/tasks/offline'
            task.mkdir(parents=True)
            (task / 'task.yaml').write_text(json.dumps({'id': 'offline', 'set': 'set', 'prompt': 'prompt.md',
                'family': 'diagnose', 'tier': 'T0', 'limits': {'turns': 1, 'wall_seconds': 1}}))
            env = dict(os.environ, MOTIF_INFERENCE_CODEX_AUTH='chatgpt', CODEX_HOME='/controller/REPORT-SENTINEL',
                       MOTIF_INFERENCE_OPENAI_KEY='REPORT-SENTINEL', MOTIF_TRIAL_ROOT=str(root / 'staging'),
                       MOTIF_TEST_GRAMMARS=str(root / 'public'), MOTIF_PANGLOSS_EXE=str(fake_parser))
            quote = lambda value: "'" + str(value).replace("'", "''") + "'"
            prepare_stamp = f"""
$ErrorActionPreference='Stop'
Import-Module {quote(tools / 'ABHarness.psm1')} -Force
Import-Module {quote(tools / 'HostPlan.psm1')} -Force
$arms=@(foreach ($name in @('a','b')) {{
    $arm=Read-ABJson (Join-Path {quote(arms)} ($name + '.yaml'))
    $arm.authMode=Get-ABInferenceAuthMode $arm
    $arm.server.executable=[IO.Path]::GetFullPath($arm.server.path)
    $arm.server.buildRoot=Split-Path $arm.server.executable
    $profile=[IO.Path]::GetFullPath((Join-Path {quote(repo / 'evals')} 'profile.json'))
    $arm.server.profilePath=$profile
    $arm.server.gradeProfilePath=$profile
    $arm
}})
$fingerprint=Get-ABRepositoryFingerprint {quote(repo)} 'Debug' $arms
Write-ABJson (Join-Path {quote(repo / 'evals/results')} '.validity.json') @{{fingerprint=$fingerprint}}
"""
            prepared = subprocess.run(['pwsh', '-NoProfile', '-Command', prepare_stamp], env=env,
                                      cwd=repo, stdin=subprocess.DEVNULL, capture_output=True, timeout=30)
            self.assertEqual(0, prepared.returncode, prepared.stderr.decode())
            process = subprocess.run(['pwsh', '-NoProfile', '-File', str(repo / 'evals/Invoke-ABQuestion.ps1'),
                '-Question', 'evals/question.yaml', '-Trials', '1', '-Parallel', '1'],
                env=env, cwd=repo, stdin=subprocess.DEVNULL, capture_output=True, timeout=60)
            self.assertEqual(0, process.returncode, process.stderr.decode())
            summaries = list((repo / 'evals/results').rglob('summary.json'))
            self.assertEqual(1, len(summaries), process.stdout.decode())
            summary = json.loads(summaries[0].read_text(encoding='utf-8-sig'))
            self.assertEqual(2, summary['integrity']['counts']['isolation_failure'])
            self.assertEqual(2, summary['integrity']['failureClasses']['harness_defect'])
            self.assertEqual(['chatgpt-plan'] * 2, [row['authMode'] for row in summary['arms']])
            self.assertEqual(['chatgpt-plan'] * 2, [row['authMode'] for row in summary['trials']])
            report = summaries[0].with_name('report.md').read_text(encoding='utf-8-sig')
            self.assertEqual(2, report.count('| chatgpt-plan |'))
            for path in (repo / 'evals/results').rglob('*'):
                if path.is_file():
                    self.assertNotIn(b'REPORT-SENTINEL', path.read_bytes(), str(path))
            self.assertNotIn(b'REPORT-SENTINEL', process.stdout + process.stderr)
