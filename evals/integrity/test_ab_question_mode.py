import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
HARNESS = REPO / 'evals/tools/ABHarness.psm1'
REPORT = REPO / 'evals/tools/ABReport.psm1'


def quote(value):
    return "'" + str(value).replace("'", "''") + "'"


def run_powershell(script, timeout=60):
    return subprocess.run(
        ['pwsh', '-NoProfile', '-Command', script],
        cwd=REPO,
        capture_output=True,
        text=True,
        timeout=timeout,
    )


def completed_rows(a_scores, b_scores, a_wall, b_wall, a_tools, b_tools):
    rows = []
    for arm_id, scores, wall_ms, tool_calls in (
        ('fake-default.a1', a_scores, a_wall, a_tools),
        ('fake-default.a2', b_scores, b_wall, b_tools),
    ):
        for index, score in enumerate(scores):
            rows.append({
                'task': f'task-{index}',
                'set': 'set-a',
                'arm': arm_id,
                'trial': 1,
                'authMode': 'none',
                'status': 'completed',
                'integrity': {'state': 'clean', 'reasons': []},
                'failureClass': None,
                'failureEvidence': [],
                'cloudRetries': 0,
                'cloudRetryEvidence': [],
                'gradingState': 'scored',
                'judgeDisagreements': 0,
                'primaryMetric': 'grade',
                'primaryScore': score,
                'grade': score,
                'success': score >= 0.5,
                'operationCount': 0,
                'wallMs': wall_ms[index],
                'turns': 1,
                'toolCalls': tool_calls[index],
                'toolErrors': 0,
                'timeToFirstProposalMs': 0,
                'inputTokens': None,
                'outputTokens': None,
                'costUsd': 0.01,
                'agentBudget': None,
                'agentToolCalls': None,
                'trialRoot': f'/tmp/{arm_id}-{index}',
                'activity': [],
            })
    return rows


@unittest.skipUnless(shutil.which('pwsh'), 'PowerShell is required for A/A harness checks')
class AAModeTests(unittest.TestCase):
    def test_aa_arm_clones_differ_only_by_id(self):
        module_path = quote(HARNESS)
        script = f"""
Import-Module {module_path} -Force
$arm = @{{ id = 'fake-default'; host = 'fake'; model = $null; effort = $null;
    prompt = 'same prompt'; server = @{{ profile = 'default'; fingerprint = 'same' }} }}
$clones = @(New-ABAAArms $arm)
$clones | ConvertTo-Json -Depth 20 -Compress
"""
        result = run_powershell(script)
        self.assertEqual(0, result.returncode, result.stderr)
        clones = json.loads(result.stdout)
        self.assertEqual(['fake-default.a1', 'fake-default.a2'], [arm['id'] for arm in clones])
        for clone in clones:
            clone.pop('id')
        self.assertEqual(clones[0], clones[1])

    def test_arm_launch_order_alternates_by_trial_number(self):
        module_path = quote(HARNESS)
        script = f"""
Import-Module {module_path} -Force
$arms = @(@{{ id = 'arm-a' }}, @{{ id = 'arm-b' }})
[ordered]@{{ odd = @(Get-ABCounterbalancedArmOrder $arms 1 | ForEach-Object id)
    even = @(Get-ABCounterbalancedArmOrder $arms 2 | ForEach-Object id) }} |
    ConvertTo-Json -Depth 10 -Compress
"""
        result = run_powershell(script)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual({'odd': ['arm-a', 'arm-b'], 'even': ['arm-b', 'arm-a']}, json.loads(result.stdout))

    def test_aa_report_states_both_verdicts_and_metric_differences(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            tasks = [
                {'id': f'task-{index}', 'set': 'set-a', 'family': 'diagnose', 'tier': 'T0',
                 'setPath': str(root / 'set-a')}
                for index in range(2)
            ]
            arms = [
                {'id': 'fake-default.a1', 'host': 'fake', 'model': 'fake', 'effort': None,
                 'authMode': 'none', 'server': {'fingerprint': 'same'}},
                {'id': 'fake-default.a2', 'host': 'fake', 'model': 'fake', 'effort': None,
                 'authMode': 'none', 'server': {'fingerprint': 'same'}},
            ]
            payload = {
                'tasks': tasks,
                'arms': arms,
                'includeCompleted': completed_rows([1.0, 0.0], [1.0, 0.0], [1000, 2000], [1100, 1900], [2, 2], [3, 3]),
                'excludeCompleted': completed_rows([0.0, 0.0], [1.0, 1.0], [1000, 1000], [1500, 1500], [2, 2], [4, 4]),
            }
            input_path = root / 'report-input.json'
            input_path.write_text(json.dumps(payload))
            module_path = quote(REPORT)
            input_value = quote(input_path)
            result_root = quote(root / 'results')
            script = f"""
$ErrorActionPreference = 'Stop'
Import-Module {module_path} -Force
$payload = Get-Content -LiteralPath {input_value} -Raw | ConvertFrom-Json -AsHashtable
foreach ($caseName in @('include', 'exclude')) {{
    $rows = @($payload[$caseName + 'Completed'])
    $question = [ordered]@{{ mode = 'aa'; aaArm = 'fake-default'; question = 'Check identical copies.' }}
    $runRoot = Join-Path {result_root} $caseName
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    Write-ABReport $rows @($payload.tasks) @($payload.arms) 1 'grade' $question $caseName ('run-' + $caseName) $runRoot
}}
"""
            result = run_powershell(script, timeout=60)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            include_summary = json.loads((root / 'results/include/summary.json').read_text())
            include_report = (root / 'results/include/report.md').read_text()
            exclude_summary = json.loads((root / 'results/exclude/summary.json').read_text())
            exclude_report = (root / 'results/exclude/report.md').read_text()
        self.assertEqual('aa', include_summary['mode'])
        self.assertEqual('fake-default', include_summary['aa']['arm'])
        self.assertTrue(include_summary['aa']['intervalIncludesZero'])
        self.assertIn('A/A check of fake-default: two identical copies.', include_report)
        self.assertIn('The interval includes zero: the harness shows no difference where none exists.', include_report)
        self.assertAlmostEqual(0.0, include_summary['aa']['passAt1Difference'])
        self.assertAlmostEqual(0.0, include_summary['aa']['meanWallMsDifference'])
        self.assertAlmostEqual(1.0, include_summary['aa']['meanToolCallsDifference'])
        self.assertEqual('aa', exclude_summary['mode'])
        self.assertFalse(exclude_summary['aa']['intervalIncludesZero'])
        self.assertIn('The interval excludes zero: the harness reports a difference between identical Arms; do not trust A/B results until this is explained.', exclude_report)
        self.assertAlmostEqual(1.0, exclude_summary['aa']['passAt1Difference'])
        self.assertAlmostEqual(500.0, exclude_summary['aa']['meanWallMsDifference'])
        self.assertAlmostEqual(2.0, exclude_summary['aa']['meanToolCallsDifference'])


if __name__ == '__main__':
    unittest.main()
