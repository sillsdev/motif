"""A Judge child sees a private empty home and only the login it needs, never the caller's configuration."""
import json
import base64
import os
from pathlib import Path
import shutil
import stat
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

TOOLS = Path(__file__).resolve().parents[1] / 'tools'
sys.path.insert(0, str(TOOLS))
import SemanticJudge
from InferenceBroker import CloudFailure

SENTINEL = 'SENTINEL-caller-configuration-7f3a'
ACCESS = 'sk-ant-oat01-judge-access-token'
REFRESH = 'sk-ant-ort01-owner-refresh-token'
# Python's locale coercion adds LC_CTYPE inside the recording host itself.
ALLOWED = {'LC_CTYPE', 'PATH', 'SYSTEMROOT', 'WINDIR', 'TMP', 'TEMP', 'HOME', 'USERPROFILE', 'XDG_CONFIG_HOME', 'XDG_DATA_HOME',
           'XDG_CACHE_HOME', 'XDG_STATE_HOME'}

RECORDER = '''#!{python}
import json, os, sys
from pathlib import Path
def files(root):
    found = {{}}
    if root and Path(root).is_dir():
        for path in Path(root).rglob('*'):
            if path.is_file():
                found[str(path)] = path.read_text(errors='replace')
    return found
ancestors = [str(p / name) for p in [Path.cwd(), *Path.cwd().parents] for name in ('CLAUDE.md', 'AGENTS.md')
             if (p / name).exists()]
reachable = {{}}
for variable in ('HOME', 'CLAUDE_CONFIG_DIR', 'CODEX_HOME', 'XDG_CONFIG_HOME', 'XDG_DATA_HOME'):
    reachable.update(files(os.environ.get(variable)))
Path({record!r}).write_text(json.dumps({{'argv': sys.argv[1:], 'env': dict(os.environ), 'cwd': os.getcwd(),
    'stdin': sys.stdin.read() if sys.argv[-1] == '-' else '', 'reachable': reachable, 'ancestors': ancestors}}))
if '--output-last-message' in sys.argv:
    Path(sys.argv[sys.argv.index('--output-last-message') + 1]).write_text('{{"judged": true}}')
print('{{"judged": true}}')
'''


class FakeBroker:
    def __init__(self, host, model, archive, auth_mode):
        self.failures = []
        self.failure_events = []
        self.denials = []


class JudgeIsolationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='motif-judge-isolation-')
        root = Path(self.temporary.name)
        self.home = root / 'caller-home'
        self.claude = self.home / '.claude'
        self.codex = self.home / '.codex'
        for directory in (self.claude / 'skills' / 'sentinel', self.codex, self.home / '.config'):
            directory.mkdir(parents=True)
        for path in (self.home / 'CLAUDE.md', self.claude / 'CLAUDE.md', self.codex / 'AGENTS.md',
                     self.claude / 'skills' / 'sentinel' / 'SKILL.md', self.home / '.config' / 'sentinel'):
            path.write_text(SENTINEL)
        (self.claude / 'settings.json').write_text(json.dumps({'env': {'SENTINEL': SENTINEL}, 'model': SENTINEL}))
        (self.codex / 'config.toml').write_text(f'model = "{SENTINEL}"\n')
        self.write_login(time.time() + 3600)
        self.record = root / 'record.json'
        self.host = root / 'host'
        self.host.write_text(RECORDER.format(python=sys.executable, record=str(self.record)))
        self.host.chmod(self.host.stat().st_mode | stat.S_IXUSR)
        self.caller = {'HOME': str(self.home), 'USERPROFILE': str(self.home), 'PATH': os.environ.get('PATH', ''),
                       'XDG_CONFIG_HOME': str(self.home / '.config'), 'CLAUDE_CONFIG_DIR': str(self.claude),
                       'CODEX_HOME': str(self.codex), 'MOTIF_CLAUDE_NATIVE': str(self.host),
                       'MOTIF_CODEX_NATIVE': str(self.host), 'MOTIF_INFERENCE_CODEX_AUTH': 'chatgpt',
                       'ANTHROPIC_API_KEY': 'control-key', 'MOTIF_INFERENCE_OPENAI_KEY': 'control-key',
                       'MOTIF_TEST_GRAMMARS': str(root / 'keys'), 'SENTINEL_VARIABLE': SENTINEL}

    def tearDown(self):
        self.temporary.cleanup()

    def write_login(self, expires):
        (self.claude / '.credentials.json').write_text(json.dumps({'claudeAiOauth': {
            'accessToken': ACCESS, 'refreshToken': REFRESH, 'expiresAt': int(expires * 1000), 'note': SENTINEL}}))

    def judged(self, family):
        with patch.dict(os.environ, self.caller, clear=True), patch('SemanticJudge.HttpBroker', FakeBroker):
            output = SemanticJudge.live_judge({'prompt': 'judge-prompt', 'schema': {}}, family)
        self.assertEqual({'judged': True}, json.loads(output))
        return json.loads(self.record.read_text())

    def assert_isolated(self, seen, extra):
        self.assertLessEqual(set(seen['env']), ALLOWED | extra)
        values = json.dumps([seen['env'], seen['argv'], seen['cwd']])
        for leaked in (str(self.home), SENTINEL, REFRESH, 'control-key'):
            self.assertNotIn(leaked, values)
        self.assertEqual({}, {path: text for path, text in seen['reachable'].items() if SENTINEL in text})
        self.assertNotIn(str(self.home), json.dumps(sorted(seen['reachable'])))
        self.assertEqual([], seen['ancestors'])

    def test_opus_judge_sees_only_the_access_token_and_an_empty_home(self):
        seen = self.judged('opus')
        self.assert_isolated(seen, {'CLAUDE_CONFIG_DIR', 'CLAUDE_CODE_OAUTH_TOKEN', 'DISABLE_AUTOUPDATER',
                                    'CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC'})
        self.assertEqual(ACCESS, seen['env']['CLAUDE_CODE_OAUTH_TOKEN'])
        self.assertEqual({}, seen['reachable'])
        for flag in ('--safe-mode', '--strict-mcp-config', '--disable-slash-commands', '--no-session-persistence'):
            self.assertIn(flag, seen['argv'])
        self.assertEqual('', seen['argv'][seen['argv'].index('--tools') + 1])

    def test_sol_judge_sees_an_empty_home_and_no_project_instructions(self):
        seen = self.judged('sol')
        self.assert_isolated(seen, {'CODEX_HOME'})
        for config in ('project_doc_max_bytes=0', 'skills.include_instructions=false', 'features.plugins=false',
                       'features.apps=false', 'features.hooks=false'):
            self.assertIn(config, seen['argv'])
        for flag in ('--ignore-user-config', '--ignore-rules', '--ephemeral'):
            self.assertIn(flag, seen['argv'])
        self.assertEqual('judge-prompt', seen['stdin'])

    @unittest.skipUnless(os.environ.get('MOTIF_CODEX_NATIVE') or shutil.which('codex'), 'the Codex CLI is not installed')
    def test_sol_ignores_skills_above_its_directory(self):
        root = Path(self.temporary.name)
        skill = root / '.agents' / 'skills' / 'sentinel'
        skill.mkdir(parents=True)
        (skill / 'SKILL.md').write_text(f'---\nname: sentinel\ndescription: {SENTINEL}\n---\n{SENTINEL}\n')
        (root / 'tmp').mkdir()
        claims = base64.urlsafe_b64encode(json.dumps({'exp': time.time() + 3600}).encode()).decode().rstrip('=')
        (self.codex / 'auth.json').write_text(json.dumps({'tokens': {'access_token': f'e30.{claims}.x',
                                                                      'account_id': 'account'}}))
        bodies = []

        def record(broker, request):
            bodies.append(base64.b64decode(request['body']).decode())
            return 400, {}, b'{"error":{"message":"recorded"}}'

        caller = {key: value for key, value in self.caller.items() if key != 'MOTIF_CODEX_NATIVE'}
        caller['PATH'] = os.environ.get('PATH', '')
        if os.environ.get('MOTIF_CODEX_NATIVE'):
            caller['MOTIF_CODEX_NATIVE'] = os.environ['MOTIF_CODEX_NATIVE']
        with patch.dict(os.environ, caller, clear=True), patch('tempfile.tempdir', str(root / 'tmp')), \
                patch('SemanticJudge.HttpBroker.forward', record), self.assertRaises(Exception) as raised:
            SemanticJudge.live_judge({'prompt': 'judge-prompt', 'schema': {'type': 'object'}}, 'sol')
        self.assertTrue(bodies, f'the Sol Judge sent no request: {raised.exception}')
        self.assertTrue(all('judge-prompt' in body and SENTINEL not in body for body in bodies))

    def test_unusable_claude_login_is_a_cloud_failure_before_launch(self):
        def expired():
            self.write_login(time.time() + 60)

        def symlinked():
            target = self.claude.parent / 'elsewhere.json'
            (self.claude / '.credentials.json').rename(target)
            (self.claude / '.credentials.json').symlink_to(target)

        def missing():
            (self.claude / '.credentials.json').unlink()

        def malformed():
            (self.claude / '.credentials.json').write_text('{"claudeAiOauth": {"accessToken": "a b"}}')

        for name, damage in (('expired', expired), ('symlinked', symlinked), ('missing', missing),
                             ('malformed', malformed)):
            with self.subTest(name):
                self.write_login(time.time() + 3600)
                damage()
                with patch.dict(os.environ, self.caller, clear=True), self.assertRaises(CloudFailure) as raised:
                    SemanticJudge.live_judge({'prompt': 'judge-prompt', 'schema': {}}, 'opus')
                self.assertIn('run claude on the host', str(raised.exception))
                self.assertNotIn(ACCESS, str(raised.exception))
                self.assertFalse(self.record.exists())
                (self.claude / '.credentials.json').unlink(missing_ok=True)


if __name__ == '__main__':
    unittest.main()
