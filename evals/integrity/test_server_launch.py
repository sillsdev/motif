"""The harness launches Motif's MCP server with arguments the server accepts, and the trial project is Known."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
BUILD = REPO / 'bin' / os.environ.get('MOTIF_TEST_CONFIGURATION', 'Debug')
MOTIF = BUILD / ('motif.exe' if sys.platform == 'win32' else 'motif')
BUILDER = BUILD / ('SIL.Motif.EvalSets.exe' if sys.platform == 'win32' else 'SIL.Motif.EvalSets')


def quote(value):
    return "'" + str(value).replace("'", "''") + "'"


def grammar_sets():
    root = os.environ.get('MOTIF_TEST_GRAMMARS')
    if not root:
        return None
    for candidate in (Path(root) / 'evals/sets', Path(root) / 'sets'):
        if candidate.is_dir():
            return candidate
    return None


@unittest.skipUnless(shutil.which('pwsh') and MOTIF.exists() and BUILDER.exists() and grammar_sets(),
                     'Server launch needs PowerShell, a built motif and EvalSets, and MOTIF_TEST_GRAMMARS')
class ServerLaunchTests(unittest.TestCase):
    def test_harness_arguments_start_the_server_and_its_project_is_known(self):
        modules = [REPO / 'evals/tools/HostPlan.psm1', REPO / 'evals/tools/McpClient.psm1', REPO / 'evals/tools/IsolatedHost.psm1']
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            env = dict(os.environ)
            env.update({'MOTIF_WORKER_ROOT': str(root / 'work'), 'MOTIF_RUNNER_NAMESPACE': 'launch-' + root.name,
                        'MOTIF_WRITING_SYSTEM_REPOSITORY_PATH': str(root / 'writing-systems'),
                        'MOTIF_TEST_SLDR_CACHE_PATH': str(root / 'sldr'), 'MOTIF_TEST_SLDR_OFFLINE': '1',
                        'MOTIF_ADVANCED_AI_MODE_PATH': str(root / 'choice.json')})
            (root / 'choice.json').write_text('{"advancedAiMode": true}')
            prepared = subprocess.run([str(BUILDER), 'build-project', '--set', str(grammar_sets() / 'eval-t0-qaxu'),
                                       '--start', 'gold-grammar', '--out', str(root / 'seed')],
                                      env=env, stdin=subprocess.DEVNULL, capture_output=True, timeout=300)
            self.assertEqual(0, prepared.returncode, prepared.stderr.decode())
            seeded = Path(json.loads(prepared.stdout)['projectPath'])
            project = root / 'session/project'
            shutil.copytree(seeded.parent, project)
            fwdata = project / 'project.fwdata'
            (project / seeded.name).rename(fwdata)
            script = '\n'.join([f'Import-Module {quote(module)}' for module in modules] + [
                "$ErrorActionPreference = 'Stop'",
                f'$registered = Invoke-ABProcess {quote(MOTIF)} (Get-ABProjectRegistrationArguments {quote(fwdata)}) '
                f'{quote(root)} @{{}} 300000',
                "if ($registered.ExitCode -or $registered.Stderr -match 'could not be recorded') { throw $registered.Stderr }",
                f'$session = New-ABMcpSession {quote(MOTIF)} (Get-ABServerArguments default $null) {quote(root)} @{{}} '
                f'{quote(root / "transcript.jsonl")}',
                "try { $listed = Invoke-ABMcpTool $session 'motif_list_projects' @{}",
                '@{ tools = @($session.Tools | ForEach-Object { $_.name }); projects = $listed.result.content[0].text;'
                ' name = (Get-ABProjectName) } | ConvertTo-Json -Compress',
                '} finally { [void](Close-ABMcpSession $session) }'])
            run = subprocess.run(['pwsh', '-NoProfile', '-Command', script], env=env, cwd=root,
                                 stdin=subprocess.DEVNULL, capture_output=True, timeout=300)
            self.assertEqual(0, run.returncode, run.stderr.decode() + run.stdout.decode())
            result = json.loads(run.stdout.decode().strip().splitlines()[-1])
            self.assertIn('motif_grammar', result['tools'])
            self.assertIn(str(fwdata), result['projects'])
            self.assertEqual('project', result['name'])


if __name__ == '__main__':
    unittest.main()
