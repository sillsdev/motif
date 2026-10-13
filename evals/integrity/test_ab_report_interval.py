import json
from pathlib import Path
import random
import shutil
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
MODULE = REPO / 'evals/tools/ABReport.psm1'


def synthetic_runs(effect, count, seed):
    rng = random.Random(seed)
    runs = []
    for _ in range(count):
        pairs = []
        for task_index in range(21):
            probability_a = rng.random()
            probability_b = min(1.0, probability_a + effect)
            for trial in range(3):
                passed_a = rng.random() < probability_a
                passed_b = rng.random() < probability_b
                pairs.append({
                    'set': 'set-a',
                    'language': 'language-a',
                    'task': f'task-{task_index}',
                    'trial': trial + 1,
                    'difference': float(passed_b) - float(passed_a),
                })
        runs.append(pairs)
    return runs


def paired_rows(task_values):
    rows = []
    for task_index, values in enumerate(task_values):
        for trial_index, value in enumerate(values):
            rows.append({
                'set': 'set-a',
                'language': 'language-a',
                'task': f'task-{task_index}',
                'trial': trial_index + 1,
                'difference': value,
            })
    return rows


@unittest.skipUnless(shutil.which('pwsh'), 'PowerShell is required for report interval calibration')
class PairedIntervalCalibrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        single_cluster = paired_rows([[0.05, 0.15], [0.15, 0.25], [0.25, 0.35]])
        one_unit = paired_rows([[0.1, 0.3]])
        clustered = [
            {'set': 'set-a', 'language': 'language-a', 'task': 'task-a', 'difference': 0.0},
            {'set': 'set-a', 'language': 'language-a', 'task': 'task-b', 'difference': 0.2},
            {'set': 'set-b', 'language': 'language-b', 'task': 'task-c', 'difference': 0.2},
            {'set': 'set-b', 'language': 'language-b', 'task': 'task-d', 'difference': 0.4},
        ]
        payload = {
            'singleCluster': single_cluster,
            'oneUnit': one_unit,
            'clustered': clustered,
            'aaRuns': synthetic_runs(0.0, 200, 6059),
            'effectRuns': synthetic_runs(0.3, 200, 6060),
        }
        cls.temp = tempfile.TemporaryDirectory()
        input_path = Path(cls.temp.name) / 'interval-input.json'
        input_path.write_text(json.dumps(payload))
        module_path = str(MODULE).replace("'", "''")
        input_value = str(input_path).replace("'", "''")
        script = f"""
$ErrorActionPreference = 'Stop'
Import-Module '{module_path}' -Force
$payload = Get-Content -LiteralPath '{input_value}' -Raw | ConvertFrom-Json -AsHashtable
$aaDetected = 0
foreach ($run in $payload.aaRuns) {{
    $result = Get-ABPairedInterval @($run)
    if (-not $result.intervalIncludesZero) {{ $aaDetected++ }}
}}
$effectDetected = 0
foreach ($run in $payload.effectRuns) {{
    $result = Get-ABPairedInterval @($run)
    if (-not $result.intervalIncludesZero) {{ $effectDetected++ }}
}}
[ordered]@{{
    singleCluster = Get-ABPairedInterval @($payload.singleCluster)
    oneUnit = Get-ABPairedInterval @($payload.oneUnit)
    clustered = Get-ABPairedInterval @($payload.clustered)
    aaDetected = $aaDetected
    aaRuns = @($payload.aaRuns).Count
    effectDetected = $effectDetected
    effectRuns = @($payload.effectRuns).Count
}} | ConvertTo-Json -Depth 20 -Compress
"""
        result = subprocess.run(
            ['pwsh', '-NoProfile', '-Command', script],
            cwd=REPO,
            capture_output=True,
            text=True,
            timeout=60,
        )
        if result.returncode:
            raise RuntimeError(result.stdout + result.stderr)
        cls.results = json.loads(result.stdout)

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def test_single_cluster_uses_task_means_and_the_known_t_interval(self):
        result = self.results['singleCluster']
        interval = result['confidenceInterval95']
        self.assertAlmostEqual(0.2, result['mean'], places=12)
        self.assertAlmostEqual(-0.048433820832296, interval['lower'], places=10)
        self.assertAlmostEqual(0.448433820832296, interval['upper'], places=10)
        self.assertEqual('Student-t 95% interval over task means (n = 3).', interval['method'])

    def test_one_unit_has_no_zero_width_interval(self):
        self.assertIsNone(self.results['oneUnit']['confidenceInterval95'])
        self.assertIsNone(self.results['oneUnit']['intervalIncludesZero'])

    def test_multiple_clusters_use_one_mean_per_cluster(self):
        result = self.results['clustered']
        interval = result['confidenceInterval95']
        self.assertAlmostEqual(0.2, result['mean'], places=12)
        self.assertAlmostEqual(-1.0706, interval['lower'], places=10)
        self.assertAlmostEqual(1.4706, interval['upper'], places=10)
        self.assertEqual('Student-t 95% interval over set/language cluster means (n = 2).', interval['method'])

    def test_aa_false_positive_rate_is_calibrated(self):
        rate = self.results['aaDetected'] / self.results['aaRuns']
        self.assertGreaterEqual(rate, 0.01, f'A/A false-positive rate was {rate:.1%}')
        self.assertLessEqual(rate, 0.10, f'A/A false-positive rate was {rate:.1%}')

    def test_positive_effect_is_detected_at_least_sixty_percent_of_the_time(self):
        rate = self.results['effectDetected'] / self.results['effectRuns']
        self.assertGreaterEqual(rate, 0.60, f'+0.3 detection rate was {rate:.1%}')


if __name__ == '__main__':
    unittest.main()
