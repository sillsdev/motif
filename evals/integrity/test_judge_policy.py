"""The model under test is never on its own Judge, in a fresh run or a re-grade."""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
MODULE = REPO / 'evals/tools/ABHarness.psm1'


def families(arm, policy):
    script = (f"Import-Module '{MODULE}'; $arm = '{json.dumps(arm)}' | ConvertFrom-Json -AsHashtable; "
              f"try {{ @(Get-ABJudgeFamilies $arm '{policy}') -join ',' }} catch {{ 'refused: ' + $_.Exception.Message }}")
    result = subprocess.run(['pwsh', '-NoProfile', '-Command', script], text=True, capture_output=True, timeout=120)
    return result.stdout.strip()


@unittest.skipUnless(shutil.which('pwsh'), 'PowerShell is required for harness integration')
class JudgePolicyTests(unittest.TestCase):
    def test_paired_policy_refuses_an_arm_whose_model_sits_on_the_judge(self):
        for model in ('opus', 'claude-opus-5-5', 'gpt-6-sol', 'gpt-6.1-sol'):
            with self.subTest(model=model):
                outcome = families({'id': 'arm', 'model': model}, 'paired')
                self.assertTrue(outcome.startswith('refused: '), outcome)
                self.assertIn('other-family-alone', outcome)

    def test_paired_policy_keeps_the_pair_for_every_other_model(self):
        for model in ('gpt-6-luna', 'claude-haiku-5-5', 'sonnet', None, 'solar-mini', 'opusculum'):
            with self.subTest(model=model):
                self.assertEqual('opus,sol', families({'id': 'arm', 'model': model}, 'paired'))

    def test_other_family_alone_uses_the_other_family(self):
        self.assertEqual('sol', families({'id': 'arm', 'model': 'claude-opus-5-5'}, 'other-family-alone'))
        self.assertEqual('opus', families({'id': 'arm', 'model': 'gpt-6-sol'}, 'other-family-alone'))
        self.assertTrue(families({'id': 'arm', 'model': 'gpt-6-luna'}, 'other-family-alone').startswith('refused: '))

    def test_every_stored_question_resolves_a_lawful_judge_for_both_arms(self):
        for path in sorted((REPO / 'evals/questions').glob('*.yaml')):
            question = json.loads(path.read_text())
            for arm_id in (question['arm_a'], question['arm_b']):
                with self.subTest(question=path.name, arm=arm_id):
                    arm = json.loads((REPO / 'evals/arms' / (arm_id + '.yaml')).read_text())
                    outcome = families(arm, question.get('judge_policy', 'paired'))
                    self.assertFalse(outcome.startswith('refused: '), outcome)

    def test_question_with_an_opus_arm_and_default_policy_fails_before_any_trial(self):
        with tempfile.TemporaryDirectory() as directory:
            question = Path(directory) / 'opus-vs-luna.yaml'
            question.write_text(json.dumps({'question': 'Opus against Luna', 'arm_a': 'claude-opus',
                                            'arm_b': 'codex-default', 'tasks': '*', 'trials': 1, 'primary_metric': 'grade'}))
            result = subprocess.run(['pwsh', '-NoProfile', '-File', str(REPO / 'evals/Invoke-ABQuestion.ps1'),
                                     '-Question', str(question), '-DryRun'], text=True, capture_output=True,
                                    timeout=300, cwd=REPO)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('never its own Judge', result.stdout + result.stderr)

    def test_regrade_passes_policy_families_to_the_grader(self):
        text = (REPO / 'evals/Regrade-ABRun.ps1').read_text()
        self.assertIn('judgeFamilies = @(Get-ABJudgeFamilies $arm $JudgePolicy)', text)


if __name__ == '__main__':
    unittest.main()
