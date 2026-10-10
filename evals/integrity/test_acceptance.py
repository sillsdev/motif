"""Offline checks for route-health evidence and acceptance decisions."""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
MODULE = ROOT / 'evals/tools/ABAcceptance.psm1'


@unittest.skipUnless(shutil.which('pwsh'), 'PowerShell is required for acceptance evidence checks')
class AcceptanceTests(unittest.TestCase):
    def invoke(self, script, values):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            paths = {}
            for name, value in values.items():
                path = root / (name + '.json')
                path.write_text(json.dumps(value))
                paths[name] = path
            body = ["$ErrorActionPreference='Stop'; $ErrorView='NormalView'; Import-Module '" + str(MODULE).replace("'", "''") + "' -Force;"]
            for name, path in paths.items():
                body.append("$" + name + "=Get-Content -Raw '" + str(path).replace("'", "''") + "' | ConvertFrom-Json -AsHashtable;")
            body.append(script)
            result = subprocess.run(['pwsh', '-NoProfile', '-Command', '\n'.join(body)],
                                    stdin=subprocess.DEVNULL, capture_output=True, text=True, timeout=30)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            return json.loads(result.stdout)

    def route_row(self, arm, task, auth, host, wall=1000, retries=0, observed=None):
        allowed = ['api.anthropic.com' if auth == 'claude-plan' else 'chatgpt.com']
        return {'arm': arm, 'task': task, 'authMode': auth, 'status': 'passed', 'failureClass': None,
                'cloudRetries': retries, 'wallMs': wall,
                'integrity': {'state': 'clean', 'network': {'allowedHosts': allowed,
                    'observedHosts': observed if observed is not None else allowed, 'allowedHostsOnly': True},
                    'mcpRegistration': {'fullToolListSeen': True}}}

    def test_route_health_picks_haiku_and_records_the_median(self):
        summary = {'measurementFingerprint': 'a' * 64, 'trials': []}
        for task, wall in zip(('a', 'b', 'c'), (300, 100, 200)):
            summary['trials'] += [self.route_row('claude-haiku-subscription', task, 'claude-plan', 'api.anthropic.com', wall),
                                  self.route_row('codex-luna-chatgpt-plan', task, 'chatgpt-plan', 'chatgpt.com', wall + 10)]
        result = self.invoke("$routes=@(@{id='claude-haiku-subscription';authMode='claude-plan';allowedHost='api.anthropic.com';episodes=3},@{id='codex-luna-chatgpt-plan';authMode='chatgpt-plan';allowedHost='chatgpt.com';episodes=3}); Get-ABRouteHealthResult $summary $routes | ConvertTo-Json -Depth 30 -Compress", {'summary': summary})
        self.assertTrue(result['passed'])
        self.assertEqual('claude-haiku-subscription', result['selectedRoute'])
        self.assertEqual(200, result['routes'][0]['medianEpisodeMs'])

    def test_cloud_retry_and_unlisted_host_fail_haiku_and_select_luna(self):
        summary = {'measurementFingerprint': 'b' * 64, 'trials': []}
        for task in ('a', 'b', 'c'):
            summary['trials'] += [self.route_row('claude-haiku-subscription', task, 'claude-plan', 'api.anthropic.com', retries=1),
                                  self.route_row('codex-luna-chatgpt-plan', task, 'chatgpt-plan', 'chatgpt.com')]
        summary['trials'][0]['integrity']['network']['observedHosts'] = ['api.anthropic.com', 'evil.invalid']
        result = self.invoke("$routes=@(@{id='claude-haiku-subscription';authMode='claude-plan';allowedHost='api.anthropic.com';episodes=3},@{id='codex-luna-chatgpt-plan';authMode='chatgpt-plan';allowedHost='chatgpt.com';episodes=3}); Get-ABRouteHealthResult $summary $routes | ConvertTo-Json -Depth 30 -Compress", {'summary': summary})
        self.assertTrue(result['passed'])
        self.assertEqual('codex-luna-chatgpt-plan', result['selectedRoute'])
        self.assertIn('Cloud failure', result['fallbackReason'])

    def test_route_health_requires_three_distinct_tasks(self):
        summary = {'measurementFingerprint': 'b' * 64, 'trials': []}
        for _ in range(3):
            summary['trials'] += [self.route_row('claude-haiku-subscription', 'same-task', 'claude-plan', 'api.anthropic.com'),
                                  self.route_row('codex-luna-chatgpt-plan', 'same-task', 'chatgpt-plan', 'chatgpt.com')]
        result = self.invoke("$routes=@(@{id='claude-haiku-subscription';authMode='claude-plan';allowedHost='api.anthropic.com';episodes=3},@{id='codex-luna-chatgpt-plan';authMode='chatgpt-plan';allowedHost='chatgpt.com';episodes=3}); Get-ABRouteHealthResult $summary $routes | ConvertTo-Json -Depth 30 -Compress", {'summary': summary})
        self.assertFalse(result['passed'])
        self.assertIsNone(result['selectedRoute'])
        self.assertTrue(any('distinct tasks' in reason for route in result['routes'] for reason in route['reasons']))

    def test_five_gates_ship_only_with_paired_fresh_evidence(self):
        def row(arm, task, success, score, model='gpt-6-luna'):
            return {'arm': arm, 'task': task, 'success': success, 'primaryScore': score,
                    'measurementFingerprint': 'c' * 64, 'armFingerprint': 'd' * 64,
                    'integrity': {'state': 'clean'}, 'gradingState': 'measured', 'model': model,
                    'wallMs': 1000, 'inputTokens': 100, 'outputTokens': 100}
        def summary(trials, arms):
            return {'measurementFingerprint': 'c' * 64, 'grading': {'trialsWithJudgeDisagreement': 0},
                    'trials': trials, 'arms': arms}
        arms = [{'id': 'new', 'model': 'gpt-6-luna'}, {'id': 'old', 'model': 'gpt-6-luna'}]
        baseline = summary([row('old', 'task-a', False, 0), row('old', 'task-b', False, 0)], arms)
        screen = summary([row('new', 'task-a', True, 1), row('old', 'task-a', False, 0)], arms)
        full = summary([row('new', 'task-a', True, 1), row('new', 'task-b', True, 1)], arms)
        confirmation = summary([row('new', 'task-a', True, 1), row('new', 'task-a', True, 1),
                                row('new', 'task-a', True, 1), row('new', 'task-b', True, 1),
                                row('new', 'task-b', True, 1)] +
                               [row('old', 'task-a', False, 0), row('old', 'task-a', False, 0),
                                row('old', 'task-a', False, 0), row('old', 'task-b', False, 0),
                                row('old', 'task-b', False, 0)], arms)
        evidence = {'frozenKeysVerified': True, 'matchedConditions': True, 'criticalContractViolations': [],
                    'affectedTasks': ['task-a', 'task-b'], 'changeType': 'correctness',
                    'servedModels': ['gpt-6-luna'], 'judgeRulings': []}
        result = self.invoke("Get-ABAcceptanceResult $screen $full $confirmation $baseline $evidence 'new' 'old' | ConvertTo-Json -Depth 30 -Compress",
                             {'screen': screen, 'full': full, 'confirmation': confirmation, 'baseline': baseline, 'evidence': evidence})
        self.assertEqual('Ship', result['decision'])
        self.assertEqual(5, len(result['gates']))
        self.assertTrue(all(gate['passed'] for gate in result['gates']))
        screen['trials'][0]['measurementFingerprint'] = 'e' * 64
        result = self.invoke("Get-ABAcceptanceResult $screen $full $confirmation $baseline $evidence 'new' 'old' | ConvertTo-Json -Depth 30 -Compress",
                             {'screen': screen, 'full': full, 'confirmation': confirmation, 'baseline': baseline, 'evidence': evidence})
        comparison_gate = next(gate for gate in result['gates'] if gate['id'] == 'comparison-valid')
        self.assertFalse(comparison_gate['passed'])

    def test_big_tier_uses_the_other_family_and_routes_failures_and_changes_to_a_person(self):
        tasks = [f'task-{index:02d}' for index in range(21)]
        arms = [{'id': 'claude-opus', 'model': 'opus'},
                {'id': 'codex-sol-chatgpt-plan', 'model': 'gpt-6-sol'}]

        def summary(run_fingerprint, baseline=False):
            trials = []
            for task in tasks:
                for arm in arms:
                    family = 'sol' if arm['id'] == 'claude-opus' else 'opus'
                    trials.append({'task': task, 'arm': arm['id'], 'trial': 1,
                        'success': not (baseline and ((task == tasks[1] and arm['id'] == 'codex-sol-chatgpt-plan') or
                                                      (task == tasks[4] and arm['id'] == 'claude-opus'))),
                        'measurementFingerprint': run_fingerprint,
                        'armFingerprint': ('d' if arm['id'] == 'claude-opus' else 'e') * 64,
                        'judgeFamilies': ['opus', 'sol'] if baseline else [family],
                        'judgeVerdict': 'paired judges' if baseline else 'single judge',
                        'integrity': {'state': 'clean'}})
            return {'measurementFingerprint': run_fingerprint,
                    'tasks': [{'id': task} for task in tasks], 'trials': trials,
                    'arms': [dict(arm, meanWallMs=100, meanToolCalls=5) for arm in arms]}

        baseline = summary('a' * 64, baseline=True)
        current = summary('b' * 64)
        failed = next(row for row in current['trials'] if row['task'] == 'task-00' and row['arm'] == 'claude-opus')
        failed['success'] = False
        failed['failureClass'] = 'agent_failure'
        failed['failureEvidence'] = ['The final answer omitted the required evidence.']
        sameFailure = next(row for row in current['trials'] if row['task'] == 'task-01' and row['arm'] == 'codex-sol-chatgpt-plan')
        sameFailure['success'] = False
        changedFailure = next(row for row in current['trials'] if row['task'] == 'task-02' and row['arm'] == 'codex-sol-chatgpt-plan')
        changedFailure['success'] = False
        changedRecovery = next(row for row in current['trials'] if row['task'] == 'task-04' and row['arm'] == 'claude-opus')
        changedRecovery['success'] = True
        result = self.invoke("Get-ABBigTierRegression $current $baseline | ConvertTo-Json -Depth 30 -Compress",
                             {'current': current, 'baseline': baseline})
        self.assertTrue(result['valid'], result.get('reasons'))
        self.assertTrue(result['triggered'])
        self.assertEqual('FullComparisonRequired', result['decision'])
        self.assertEqual('single judge', result['verdict'])
        reviewed = {(item['task'], item['arm']): item for item in result['personReview']}
        self.assertEqual({('task-00', 'claude-opus'), ('task-02', 'codex-sol-chatgpt-plan'),
                          ('task-01', 'codex-sol-chatgpt-plan'), ('task-04', 'claude-opus')}, set(reviewed))
        self.assertEqual('agent_failure', reviewed[('task-00', 'claude-opus')]['failureClass'])
        self.assertIn('Episode failed', reviewed[('task-00', 'claude-opus')]['reasons'])
        self.assertIn('Outcome changed since the previous release check', reviewed[('task-00', 'claude-opus')]['reasons'])
        self.assertIn('Outcome changed since the previous release check', reviewed[('task-04', 'claude-opus')]['reasons'])
        self.assertNotEqual('Ship', result['decision'])

    def test_big_tier_incomplete_evidence_holds_the_release_check(self):
        summary = {'measurementFingerprint': 'a' * 64, 'trials': [], 'arms': [], 'tasks': []}
        result = self.invoke("Get-ABBigTierRegression $current $baseline | ConvertTo-Json -Depth 30 -Compress",
                             {'current': summary, 'baseline': summary})
        self.assertFalse(result['valid'])
        self.assertFalse(result['triggered'])
        self.assertEqual('Hold', result['decision'])
        self.assertIn('Hold', result['action'])

    def test_big_tier_missing_fields_fails_closed(self):
        summary = {'measurementFingerprint': 'a' * 64, 'arms': [], 'tasks': []}
        result = self.invoke("Get-ABBigTierRegression $current $baseline | ConvertTo-Json -Depth 30 -Compress",
                             {'current': summary, 'baseline': summary})
        self.assertFalse(result['valid'])
        self.assertFalse(result['triggered'])
        self.assertEqual('Hold', result['decision'])
        self.assertTrue(any('missing required evidence' in reason for reason in result['reasons']))

    def test_big_tier_requires_each_task_and_arm_pair(self):
        tasks = [f'task-{index:02d}' for index in range(21)]
        arms = [{'id': 'claude-opus', 'model': 'opus'},
                {'id': 'codex-sol-chatgpt-plan', 'model': 'gpt-6-sol'}]

        def summary(fingerprint):
            trials = [{'task': task, 'arm': arm['id'], 'success': True,
                       'measurementFingerprint': fingerprint, 'armFingerprint': 'd' * 64,
                       'judgeFamilies': ['sol' if arm['id'] == 'claude-opus' else 'opus'],
                       'judgeVerdict': 'single judge', 'integrity': {'state': 'clean'}}
                      for task in tasks for arm in arms]
            return {'measurementFingerprint': fingerprint, 'tasks': [{'id': task} for task in tasks],
                    'trials': trials, 'arms': [dict(arm, meanWallMs=100, meanToolCalls=5) for arm in arms]}

        baseline = summary('a' * 64)
        current = summary('b' * 64)
        current['trials'][0]['task'] = 'unlisted-task'
        result = self.invoke("Get-ABBigTierRegression $current $baseline | ConvertTo-Json -Depth 30 -Compress",
                             {'current': current, 'baseline': baseline})
        self.assertFalse(result['valid'])
        self.assertTrue(any('one Episode for each Arm and task' in reason for reason in result['reasons']))

    def test_big_tier_rejects_a_judge_from_the_model_family_under_test(self):
        tasks = [f'task-{index:02d}' for index in range(21)]
        arms = [{'id': 'claude-opus', 'model': 'opus'},
                {'id': 'codex-sol-chatgpt-plan', 'model': 'gpt-6-sol'}]
        def summary(fingerprint):
            trials = [{'task': task, 'arm': arm['id'], 'success': True,
                       'measurementFingerprint': fingerprint, 'armFingerprint': 'd' * 64,
                       'judgeFamilies': ['sol' if arm['id'] == 'claude-opus' else 'opus'],
                       'judgeVerdict': 'single judge', 'integrity': {'state': 'clean'}}
                      for task in tasks for arm in arms]
            return {'measurementFingerprint': fingerprint, 'tasks': [{'id': task} for task in tasks],
                    'trials': trials, 'arms': [dict(arm, meanWallMs=100, meanToolCalls=5) for arm in arms]}
        baseline = summary('a' * 64)
        current = summary('b' * 64)
        current['trials'][0]['judgeFamilies'] = ['opus']
        result = self.invoke("Get-ABBigTierRegression $current $baseline | ConvertTo-Json -Depth 30 -Compress",
                             {'current': current, 'baseline': baseline})
        self.assertFalse(result['valid'])
        self.assertEqual('Hold', result['decision'])
        self.assertTrue(any('claude-opus' in reason and "other family's Judge" in reason for reason in result['reasons']))

    def test_acceptance_holds_when_full_suite_has_missing_scored_evidence(self):
        def row(arm, task, success, score):
            return {'arm': arm, 'task': task, 'success': success, 'primaryScore': score,
                    'measurementFingerprint': 'c' * 64, 'armFingerprint': 'd' * 64,
                    'integrity': {'state': 'clean'}, 'gradingState': 'measured', 'model': 'gpt-6-luna',
                    'wallMs': 1000, 'inputTokens': 100, 'outputTokens': 100}
        arms = [{'id': 'new', 'model': 'gpt-6-luna'}, {'id': 'old', 'model': 'gpt-6-luna'}]
        baseline = {'measurementFingerprint': 'c' * 64, 'trials': [row('old', 'task-a', False, 0)], 'arms': arms}
        screen = {'measurementFingerprint': 'c' * 64, 'trials': [row('new', 'task-a', True, 1)]}
        full = {'measurementFingerprint': 'c' * 64, 'trials': [], 'arms': arms}
        confirmation = {'measurementFingerprint': 'c' * 64,
            'trials': [row('new', 'task-a', True, 1) for _ in range(5)] + [row('old', 'task-a', False, 0) for _ in range(5)]}
        evidence = {'frozenKeysVerified': True, 'matchedConditions': True, 'criticalContractViolations': [],
                    'affectedTasks': ['task-a'], 'changeType': 'correctness', 'servedModels': ['gpt-6-luna'],
                    'originalTask': 'task-a', 'newCounterexample': 'task-b', 'judgeRulings': []}
        result = self.invoke("Get-ABAcceptanceResult $screen $full $confirmation $baseline $evidence 'new' 'old' | ConvertTo-Json -Depth 30 -Compress",
            {'screen': screen, 'full': full, 'confirmation': confirmation, 'baseline': baseline, 'evidence': evidence})
        self.assertEqual('Hold', result['decision'])
        full_gate = next(gate for gate in result['gates'] if gate['id'] == 'full-suite')
        self.assertFalse(full_gate['passed'])

    def test_acceptance_requires_a_personal_ruling_for_each_split_episode(self):
        def row(arm, task, success, score, disagreements=0):
            return {'arm': arm, 'task': task, 'trial': 1, 'judgeDisagreements': disagreements,
                    'measurementFingerprint': 'c' * 64, 'armFingerprint': 'd' * 64,
                    'success': success, 'primaryScore': score, 'integrity': {'state': 'clean'},
                    'gradingState': 'measured', 'wallMs': 1000, 'inputTokens': 100, 'outputTokens': 100}
        arms = [{'id': 'new', 'model': 'gpt-6-luna'}, {'id': 'old', 'model': 'gpt-6-luna'}]
        baseline = {'runId': 'base', 'measurementFingerprint': 'c' * 64,
                    'trials': [row('old', 'task-a', False, 0)], 'arms': arms}
        screen = {'runId': 'screen', 'measurementFingerprint': 'c' * 64,
                  'grading': {'trialsWithJudgeDisagreement': 1},
                  'trials': [row('new', 'task-a', True, 1, 1)]}
        full = {'runId': 'full', 'measurementFingerprint': 'c' * 64, 'trials': [], 'arms': arms}
        confirmation = {'runId': 'confirm', 'measurementFingerprint': 'c' * 64,
            'trials': [row('new', 'task-a', True, 1) for _ in range(5)] + [row('old', 'task-a', False, 0) for _ in range(5)]}
        evidence = {'frozenKeysVerified': True, 'matchedConditions': True, 'criticalContractViolations': [],
                    'affectedTasks': ['task-a'], 'changeType': 'correctness', 'servedModels': ['gpt-6-luna'],
                    'originalTask': 'task-a', 'newCounterexample': 'task-b', 'judgeRulings': []}
        arguments = "$screen $full $confirmation $baseline $evidence 'new' 'old'"
        result = self.invoke("Get-ABAcceptanceResult " + arguments + " | ConvertTo-Json -Depth 30 -Compress",
            {'screen': screen, 'full': full, 'confirmation': confirmation, 'baseline': baseline, 'evidence': evidence})
        valid_gate = next(gate for gate in result['gates'] if gate['id'] == 'comparison-valid')
        self.assertFalse(valid_gate['passed'])
        evidence['judgeRulings'] = [{'episodeId': 'screen/task-a/new/1', 'person': 'reviewer',
                                     'ruling': 'pass', 'rationale': 'The quoted required meaning is present.'}]
        result = self.invoke("Get-ABAcceptanceResult " + arguments + " | ConvertTo-Json -Depth 30 -Compress",
            {'screen': screen, 'full': full, 'confirmation': confirmation, 'baseline': baseline, 'evidence': evidence})
        valid_gate = next(gate for gate in result['gates'] if gate['id'] == 'comparison-valid')
        self.assertTrue(valid_gate['passed'])


if __name__ == '__main__':
    unittest.main()
