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

    def test_confirmation_results_root_requires_an_external_unlinked_location(self):
        repo = Path(__file__).resolve().parents[2]
        module = repo / 'evals/tools/ABHarness.psm1'
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            grammar = root / 'confirmation'
            (grammar / 'sets').mkdir(parents=True)
            valid = root / 'confirmation-results'
            checkout = repo / 'evals/results-root-test'
            inside_grammar = grammar / 'sets/results'
            linked_target = root / 'linked-target'
            linked_target.mkdir()
            alias = root / 'alias'
            alias.symlink_to(linked_target, target_is_directory=True)

            def quote(path):
                return "'" + str(path).replace("'", "''") + "'"

            script = f'''$ErrorActionPreference='Stop'
Import-Module {quote(module)} -Force
$env:MOTIF_CONFIRMATION_GRAMMARS={quote(grammar)}
$env:MOTIF_CONFIRMATION_RESULTS=''
$denied=$false
try {{ [void](Get-ABConfirmationResultsRoot {quote(repo)}) }} catch {{
    $denied=$_.Exception.Message.Contains('MOTIF_CONFIRMATION_RESULTS')
}}
if (-not $denied) {{ throw 'An unset confirmation results root was not refused with its setting name.' }}
foreach ($invalid in @({quote(checkout)}, {quote(inside_grammar)}, {quote(alias / 'results')})) {{
    $env:MOTIF_CONFIRMATION_RESULTS=$invalid
    $denied=$false
    try {{ [void](Get-ABConfirmationResultsRoot {quote(repo)}) }} catch {{ $denied=$true }}
    if (-not $denied) {{ throw "An unsafe confirmation results root was accepted: $invalid" }}
}}
$env:MOTIF_CONFIRMATION_RESULTS={quote(valid)}
$resolved=Get-ABConfirmationResultsRoot {quote(repo)}
if ($resolved -ne {quote(valid)} -or -not (Test-Path -LiteralPath $resolved -PathType Container)) {{
    throw 'The external confirmation results root was not created and returned.'
}}
'''
            result = subprocess.run(['pwsh', '-NoProfile', '-Command', script], stdin=subprocess.DEVNULL,
                                    capture_output=True, text=True, timeout=30)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_confirmation_results_root_refuses_protected_roots_and_their_ancestors(self):
        repo = Path(__file__).resolve().parents[2]
        module = repo / 'evals/tools/ABHarness.psm1'
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            grammar = root / 'confirmation'
            (grammar / 'evals/sets').mkdir(parents=True)
            public = root / 'public'
            (public / 'sets').mkdir(parents=True)
            staging = root / 'staging'
            staging.mkdir()
            linked_checkout = root / 'checkout-link'
            linked_checkout.symlink_to(repo, target_is_directory=True)
            invalid = [grammar, grammar / 'results', public, public / 'results', staging, staging / 'results', root,
                       repo.parent, repo / 'evals/results-root-test']

            def quote(path):
                return "'" + str(path).replace("'", "''") + "'"

            script = f'''$ErrorActionPreference='Stop'
Import-Module {quote(module)} -Force
$env:MOTIF_CONFIRMATION_GRAMMARS={quote(grammar)}
$env:MOTIF_TEST_GRAMMARS={quote(public)}
$env:MOTIF_TRIAL_ROOT={quote(staging)}
foreach ($invalid in @({', '.join(quote(p) for p in invalid)})) {{
    $env:MOTIF_CONFIRMATION_RESULTS=$invalid
    $denied=$false
    try {{ [void](Get-ABConfirmationResultsRoot {quote(linked_checkout)}) }} catch {{ $denied=$true }}
    if (-not $denied) {{ throw "An unsafe confirmation results root was accepted: $invalid" }}
}}
$valid={quote(root / 'confirmation-results')}
$env:MOTIF_CONFIRMATION_RESULTS=$valid
if ((Get-ABConfirmationResultsRoot {quote(linked_checkout)}) -ne $valid) {{ throw 'A safe results root was refused.' }}
$env:MOTIF_TRIAL_ROOT=(Join-Path $valid 'staging')
if ((Get-ABConfirmationResultsRoot {quote(linked_checkout)}) -ne $valid) {{ throw 'Redirected staging refused its own results root.' }}
'''
            result = subprocess.run(['pwsh', '-NoProfile', '-Command', script], stdin=subprocess.DEVNULL,
                                    capture_output=True, text=True, timeout=30)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
