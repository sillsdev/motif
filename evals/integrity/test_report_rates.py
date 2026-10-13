"""Pass rates compare Arms over one task set, and undecided or unpriced rows never pose as failures or full prices."""
import json
from pathlib import Path
import shutil
import tempfile
import unittest

from test_ab_question_mode import REPORT, completed_rows, quote, run_powershell


def arms():
    return [{'id': arm, 'host': 'fake', 'model': 'fake', 'effort': None, 'authMode': 'none',
             'server': {'fingerprint': 'same'}} for arm in ('fake-default.a1', 'fake-default.a2')]


def report(rows, task_count):
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        tasks = [{'id': f'task-{index}', 'set': 'set-a', 'family': 'diagnose', 'tier': 'T0',
                  'setPath': str(root / 'set-a')} for index in range(task_count)]
        payload = root / 'input.json'
        payload.write_text(json.dumps({'rows': rows, 'tasks': tasks, 'arms': arms()}))
        script = f"""
$ErrorActionPreference = 'Stop'
Import-Module {quote(REPORT)} -Force
$payload = Get-Content -LiteralPath {quote(payload)} -Raw | ConvertFrom-Json -AsHashtable
$question = [ordered]@{{ question = 'Rates' }}
Write-ABReport @($payload.rows) @($payload.tasks) @($payload.arms) 1 'grade' $question 'rates' 'run' {quote(root)}
"""
        result = run_powershell(script, timeout=120)
        if result.returncode:
            raise AssertionError(result.stdout + result.stderr)
        return json.loads((root / 'summary.json').read_text()), (root / 'report.md').read_text()


@unittest.skipUnless(shutil.which('pwsh'), 'PowerShell is required for harness report checks')
class ReportRateTests(unittest.TestCase):
    def test_a_task_excluded_for_one_arm_leaves_the_pass_rates_of_both(self):
        rows = completed_rows([1.0, 0.0, 1.0], [1.0, 0.0, 0.0], [1, 1, 1], [1, 1, 1], [1, 1, 1], [1, 1, 1])
        rows[2].update(failureClass='cloud_failure', primaryScore=None, success=None)
        summary, text = report(rows, 3)
        left, right = summary['arms']
        self.assertEqual((2, 2), (left['eligibleTasks'], right['eligibleTasks']))
        self.assertEqual((0.5, 0.5), (left['passAt1'], right['passAt1']))
        self.assertIn('Both use the 2 tasks every Arm has scored', text)

    def test_undecided_success_is_neither_a_failure_nor_a_priced_episode(self):
        rows = completed_rows([1.0, 0.4], [1.0, 1.0], [1, 1], [1, 1], [1, 1], [1, 1])
        rows[1]['success'] = None
        summary, _ = report(rows, 2)
        left = summary['arms'][0]
        self.assertEqual(1, left['eligibleTasks'])
        self.assertEqual(1.0, left['passAt1'])
        self.assertEqual(1, left['costCoverage']['scoredEpisodes'])
        self.assertAlmostEqual(0.01, left['priceToSolveUsd'])
        self.assertEqual(2, len(summary['pairs']))

    def test_boundary_excluded_row_with_a_stale_score_is_not_scored(self):
        rows = completed_rows([1.0, 0.0], [1.0, 1.0], [1, 1], [1, 1], [1, 1], [1, 1])
        rows[1]['failureClass'] = 'harness_defect'
        summary, _ = report(rows, 2)
        self.assertEqual(1, len(summary['pairs']))
        self.assertEqual(1.0, summary['arms'][0]['meanPrimaryScore'])

    def test_unpriced_retry_keeps_the_recorded_price_and_says_it_is_a_lower_bound(self):
        rows = completed_rows([1.0, 1.0], [1.0, 1.0], [1, 1], [1, 1], [1, 1], [1, 1])
        rows[0]['unpricedRetryAttempts'] = 1
        summary, text = report(rows, 2)
        left = summary['arms'][0]
        self.assertTrue(left['costCoverage']['complete'])
        self.assertEqual(1, left['costCoverage']['unpricedRetryAttempts'])
        self.assertAlmostEqual(0.01, left['priceToSolveUsd'])
        self.assertIn('1 retried attempts reported no cost, so those prices are lower bounds', text)


if __name__ == '__main__':
    unittest.main()
