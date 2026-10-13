"""Confirmation-specific task inputs stay on their own root."""
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


@unittest.skipUnless(sys.platform == 'linux', 'PowerShell confirmation path check uses Linux paths')
class ConfirmationTests(unittest.TestCase):
    def test_confirmation_staging_redirects_checkout_paths_before_creation(self):
        repo = Path(__file__).resolve().parents[2]
        module = repo / 'evals/tools/ABHarness.psm1'
        with tempfile.TemporaryDirectory() as directory:
            results = Path(directory) / 'results'
            results.mkdir()
            expected = results / 'staging'
            checkout_staging = repo / 'bin/.cache/confirmation-staging-test'

            def quote(path):
                return "'" + str(path).replace("'", "''") + "'"

            script = f'''$ErrorActionPreference='Stop'
Import-Module {quote(module)} -Force
$env:MOTIF_TRIAL_ROOT={quote(checkout_staging)}
Set-ABConfirmationTrialRoot {quote(repo)} {quote(results)}
if ($env:MOTIF_TRIAL_ROOT -ne {quote(expected)} -or (Test-Path -LiteralPath {quote(expected)})) {{
    throw 'Confirmation staging was not redirected before its directory was created.'
}}
'''
            result = subprocess.run(['pwsh', '-NoProfile', '-Command', script], stdin=subprocess.DEVNULL,
                                    capture_output=True, text=True, timeout=30)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_confirmation_validity_requires_both_task_local_scripts(self):
        repo = Path(__file__).resolve().parents[2]
        module = repo / 'evals/tools/ABHarness.psm1'
        with tempfile.TemporaryDirectory() as directory:
            task = Path(directory) / 'task'
            validity = task / 'validity'
            validity.mkdir(parents=True)
            gold = validity / 'gold.jsonl'
            wrong = validity / 'wrong.jsonl'
            gold.write_text('{"finalMessage":"gold"}\n')
            wrong.write_text('{"finalMessage":"wrong"}\n')

            def quote(path):
                return "'" + str(path).replace("'", "''") + "'"

            script = f'''$ErrorActionPreference='Stop'
Import-Module {quote(module)} -Force
$task=@{{ id='t0-confirmation'; taskPath={quote(task)} }}
$scripts=Get-ABConfirmationValidityScripts $task
if ($scripts.gold -ne {quote(gold)} -or $scripts.wrong -ne {quote(wrong)}) {{
    throw 'Confirmation validity did not return both task-local scripts.'
}}
Remove-Item -LiteralPath {quote(wrong)}
$denied=$false
try {{ [void](Get-ABConfirmationValidityScripts $task) }} catch {{
    $denied=$_.Exception.Message.Contains('t0-confirmation')
}}
if (-not $denied) {{ throw 'A missing validity script was not refused with its task ID.' }}
'''
            result = subprocess.run(['pwsh', '-NoProfile', '-Command', script], stdin=subprocess.DEVNULL,
                                    capture_output=True, text=True, timeout=30)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_confirmation_trial_uses_its_required_gold_script_by_default(self):
        repo = Path(__file__).resolve().parents[2]
        module = repo / 'evals/tools/ABHarness.psm1'
        with tempfile.TemporaryDirectory() as directory:
            task = Path(directory) / 'task'
            validity = task / 'validity'
            validity.mkdir(parents=True)
            gold = validity / 'gold.jsonl'
            gold.write_text('{"finalMessage":"confirmation gold"}\n')

            def quote(path):
                return "'" + str(path).replace("'", "''") + "'"

            script = f'''$ErrorActionPreference='Stop'
Import-Module {quote(module)} -Force
$manifest=@{{ confirmation=$true; taskPath={quote(task)}; task=@{{ id='t0-confirmation' }} }}
$selected=Get-ABFakeScriptPath {quote(repo)} $manifest
if ($selected -ne {quote(gold)}) {{ throw "Wrong fake script selected: $selected" }}
$manifest.taskPath={quote(Path(directory) / 'missing-task')}
$denied=$false
try {{ [void](Get-ABFakeScriptPath {quote(repo)} $manifest) }} catch {{
    $denied=$_.Exception.Message.Contains('t0-confirmation')
}}
if (-not $denied) {{ throw 'A missing confirmation gold script was not refused with its task ID.' }}
'''
            result = subprocess.run(['pwsh', '-NoProfile', '-Command', script], stdin=subprocess.DEVNULL,
                                    capture_output=True, text=True, timeout=30)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
