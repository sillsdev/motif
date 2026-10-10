"""External-root enforcement through the PowerShell controller's actual resolver."""
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


@unittest.skipUnless(sys.platform == 'linux', 'Symbolic-link root regression uses Linux paths')
class RootTests(unittest.TestCase):
    def test_checkout_symbolic_and_unpinned_sets_roots_are_refused(self):
        repo = Path(__file__).resolve().parents[2]
        module = repo / 'evals/tools/ABHarness.psm1'
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            public = root / 'public'
            (public / 'sets').mkdir(parents=True)
            pinned = root / 'pinned'
            (pinned / 'evals/sets').mkdir(parents=True)
            stale = root / 'stale'
            (stale / 'evals/sets').mkdir(parents=True)
            git = ['git', '-c', 'user.name=t', '-c', 'user.email=t@example.invalid']
            subprocess.run(git + ['-C', str(stale), 'init', '-q'], check=True, stdin=subprocess.DEVNULL)
            subprocess.run(git + ['-C', str(stale), 'commit', '-q', '--allow-empty', '-m', 'stale'], check=True,
                           stdin=subprocess.DEVNULL)
            alias = root / 'alias'
            alias.mkdir()
            (alias / 'sets').symlink_to(public / 'sets', target_is_directory=True)

            def quote(path):
                return "'" + str(path).replace("'", "''") + "'"

            script = f'''Import-Module {quote(module)}
$env:MOTIF_TEST_GRAMMARS={quote(public)}
if ((Get-ABSetRoot {quote(repo)}) -ne {quote(public / 'sets')}) {{ throw 'External set layout did not resolve.' }}
$env:MOTIF_TEST_GRAMMARS={quote(pinned)}
if ((Get-ABSetRoot {quote(repo)}) -ne {quote(pinned / 'evals/sets')}) {{ throw 'Pinned repository layout did not resolve.' }}
foreach ($invalid in @({quote(repo)}, {quote(alias)}, {quote(stale)})) {{
 $env:MOTIF_TEST_GRAMMARS=$invalid
 $denied=$false
 try {{ [void](Get-ABSetRoot {quote(repo)}) }} catch {{ $denied=$true }}
 if (-not $denied) {{ throw 'An unsafe root was accepted.' }}
}}
'''
            result = subprocess.run(['pwsh', '-NoProfile', '-Command', script], stdin=subprocess.DEVNULL,
                                    capture_output=True, timeout=30)
            self.assertEqual(0, result.returncode, result.stderr.decode())
