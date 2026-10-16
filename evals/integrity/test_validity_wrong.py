"""Every active public task carries a tempting-wrong validity case."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
MODULE = REPO / 'evals/tools/ABHarness.psm1'


def quote(path):
    return "'" + str(path).replace("'", "''") + "'"


def wrong_case(task_id, finals_path):
    script = f'''$ErrorActionPreference='Stop'
Import-Module {quote(MODULE)} -Force
$finals=@(Get-Content -Raw {quote(finals_path)} | ConvertFrom-Json -AsHashtable)
$case=Get-ABValidityWrongCase {quote(REPO)} @{{ id='{task_id}' }} $finals
$case | ConvertTo-Json -Compress
'''
    return subprocess.run(['pwsh', '-NoProfile', '-Command', script], stdin=subprocess.DEVNULL,
                          capture_output=True, text=True, timeout=60)


@unittest.skipUnless(shutil.which('pwsh'), 'PowerShell is required for validity case checks')
class ValidityWrongCaseTests(unittest.TestCase):
    def test_a_scripted_wrong_answer_is_preferred_and_a_final_fixture_is_used_otherwise(self):
        with tempfile.TemporaryDirectory() as directory:
            finals = Path(directory) / 'finals.json'
            finals.write_text(json.dumps([{'task': 't0-diagnose-noun-slot-order', 'required': ['x'],
                                           'gold': 'g', 'wrong': 'the tempting wrong final'}]))
            scripted = wrong_case('t0-lexicon-correct-gloss', finals)
            self.assertEqual(0, scripted.returncode, scripted.stdout + scripted.stderr)
            self.assertEqual(str(REPO / 'evals/fake-scripts/wrong/t0-lexicon-correct-gloss.jsonl'),
                             json.loads(scripted.stdout)['script'])
            final = wrong_case('t0-diagnose-noun-slot-order', finals)
            self.assertEqual(0, final.returncode, final.stdout + final.stderr)
            self.assertEqual('the tempting wrong final', json.loads(final.stdout)['finalMessage'])

    def test_an_active_task_without_a_wrong_case_is_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            finals = Path(directory) / 'finals.json'
            finals.write_text('[]')
            result = wrong_case('t9-no-such-task', finals)
            self.assertNotEqual(0, result.returncode)
            self.assertIn('t9-no-such-task', result.stdout + result.stderr)

    @unittest.skipUnless(os.environ.get('MOTIF_TEST_GRAMMARS'), 'Needs the pinned test grammars in MOTIF_TEST_GRAMMARS')
    def test_every_active_public_task_has_a_wrong_case(self):
        finals = {row['task'] for row in json.loads((REPO / 'evals/integrity/fixtures/validity-finals.json').read_text())}
        scripted = {path.stem for path in (REPO / 'evals/fake-scripts/wrong').glob('*.jsonl')}
        missing = []
        for path in sorted(Path(os.environ['MOTIF_TEST_GRAMMARS']).glob('evals/sets/*/tasks/*/task.yaml')):
            task = json.loads(path.read_text())
            if task.get('status') == 'future' or task.get('availability') == 'future':
                continue
            if task['id'] not in finals and task['id'] not in scripted:
                missing.append(task['id'])
        self.assertEqual([], missing)


if __name__ == '__main__':
    unittest.main()
