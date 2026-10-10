"""Re-grading retains trial evidence and integrity while replacing report scores."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

from test_semantic_grader import TOOLS, FIXTURES, fake_environment
sys.path.insert(0, str(TOOLS))
from CalibrateMeaning import calibrate


@unittest.skipUnless(os.name == 'posix' and shutil.which('pwsh'), 'Re-grade executable fixture requires Unix and PowerShell')
class RegradeTests(unittest.TestCase):
    def fixture(self, root):
        repo = root / 'repo'
        tools = repo / 'evals/tools'
        tools.mkdir(parents=True)
        for source in TOOLS.glob('*'):
            if source.is_file(): shutil.copyfile(source, tools / source.name)
        shutil.copyfile(TOOLS.parent / 'Regrade-ABRun.ps1', repo / 'evals/Regrade-ABRun.ps1')
        arms = repo / 'evals/arms'
        arms.mkdir()
        profiles = repo / 'evals/profiles'
        profiles.mkdir()
        for arm in ('left', 'right'):
            (arms / (arm + '.yaml')).write_text(json.dumps({'server': {'grade_profile': 'profiles/test.json'}}))
        (profiles / 'test.json').write_text(json.dumps({'tools': [], 'hiddenTools': []}))
        builder = repo / 'bin/Debug/SIL.Motif.EvalSets'
        builder.parent.mkdir(parents=True)
        builder.write_text('#!' + sys.executable + '\nimport json\nprint(json.dumps({"proposal":{"operations":[]},"parserRows":[],"lexiconBefore":None,"lexiconAfter":None,"proposalFailure":None}))\n')
        builder.chmod(0o755)
        grammar = root / 'grammars'
        task = grammar / 'set/tasks/diagnose'
        task.mkdir(parents=True)
        (task / 'prompt.md').write_text('What is wrong?')
        (task / 'answer.yaml').write_text(json.dumps({'category': 'x', 'object': 'y', 'meaning': {
            'required': ['plural before locative'], 'mustNot': ['propose a new affix']}}))
        (task / 'task.yaml').write_text(json.dumps({'id': 'diagnose', 'set': 'set', 'family': 'diagnose',
            'tier': 'T0', 'start': 'gold', 'prompt': 'prompt.md',
            'graders': [{'type': 'answer', 'key': 'answer.yaml', 'pass': 1, 'weight': 1}]}))
        source_run = root / 'run'
        trials = []
        for arm in ('left', 'right'):
            trial = source_run / 'trials/diagnose' / arm / 'trial-1'
            frozen = trial / 'frozen'
            frozen.mkdir(parents=True)
            files = {'host.json': json.dumps({'finalMessage': 'The template needs plural before locative.'}),
                     'activity.jsonl': '', 'transcript.jsonl': '', 'intent.json': 'null'}
            for name, content in files.items():
                (frozen / name).write_text(content)
                if name != 'intent.json': (trial / name).write_text(content)
            integrity = {'state': 'clean' if arm == 'left' else 'invalid', 'closedUtc': 1, 'reasons': [],
                         'bundleHashes': {n: hashlib.sha256(c.encode()).hexdigest() for n, c in files.items()}}
            grade = {'grade': 0, 'primaryScore': 0 if arm == 'left' else None,
                     'success': False if arm == 'left' else None, 'operationCount': 0, 'graders': []}
            manifest = {'integrity': integrity, 'status': 'failed' if arm == 'left' else 'invalid'}
            (trial / 'grade.json').write_text(json.dumps(grade))
            (trial / 'integrity.json').write_text(json.dumps(integrity))
            (trial / 'manifest.json').write_text(json.dumps(manifest))
            (trial / 'server.client-tools.json').write_text('[]')
            (trial / 'proposals.json').write_text('{"proposal":{"operations":[]}}')
            trials.append({'task': 'diagnose', 'set': 'set', 'arm': arm, 'trial': 1, 'authMode': 'none',
                'status': manifest['status'], 'integrity': integrity,
                'grade': grade['grade'], 'primaryScore': grade['primaryScore'], 'success': grade['success'],
                'operationCount': 0, 'wallMs': 1, 'turns': 1, 'toolCalls': 0, 'toolErrors': 0,
                'timeToFirstProposalMs': None, 'inputTokens': None, 'outputTokens': None, 'costUsd': None,
                'files': {'grade': str(trial / 'grade.json')}})
        summary = {'runId': 'original', 'questionId': 'question', 'question': 'Does right do better?',
            'measurementFingerprint': 'a' * 64,
            'primaryMetric': 'task-success', 'tasks': [{'id': 'diagnose', 'set': 'set'}], 'trials': trials,
            'arms': [{'id': a, 'host': 'fake', 'model': None, 'effort': None, 'authMode': 'none', 'requestedTrials': 1}
                     for a in ('left', 'right')]}
        (source_run / 'summary.json').write_text(json.dumps(summary))
        (source_run / 'report.md').write_text('original report')
        return repo, grammar, source_run

    def test_regrade_writes_sibling_report_preserving_originals_and_integrity(self):
        self.regrade('pass', 1)

    def test_regrade_harness_defect_keeps_original_clean_integrity_unscored(self):
        self.regrade('malformed', None)

    def test_clean_pairs_and_judge_splits_reach_shared_report(self):
        self.regrade('pass', 1, right_clean=True)

    def test_judge_disagreement_remains_unscored_after_regrade(self):
        self.regrade('split', None, right_clean=True)

    def test_changed_frozen_bundle_is_unscored_without_redeciding_integrity(self):
        self.regrade('pass', None, tamper=True)

    def regrade(self, mode, expected, right_clean=False, tamper=False):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            repo, grammar, source = self.fixture(root)
            if right_clean:
                summary = json.loads((source / 'summary.json').read_text())
                summary['trials'][1]['integrity']['state'] = 'clean'
                (source / 'summary.json').write_text(json.dumps(summary))
                trial_root = source / 'trials/diagnose/right/trial-1'
                for name in ('integrity.json', 'manifest.json'):
                    record = json.loads((trial_root / name).read_text())
                    if name == 'manifest.json': record['integrity']['state'] = 'clean'
                    else: record['state'] = 'clean'
                    (trial_root / name).write_text(json.dumps(record))
            if tamper:
                (source / 'trials/diagnose/left/trial-1/frozen/host.json').write_text('{}')
            original = {p: p.read_bytes() for p in source.rglob('*') if p.is_file()}
            process = subprocess.run(['pwsh', '-NoProfile', '-File', str(repo / 'evals/Regrade-ABRun.ps1'),
                '-RunDirectory', str(source)], env=dict(fake_environment(mode), MOTIF_TEST_GRAMMARS=str(grammar),
                MOTIF_TRIAL_ROOT=str(root / 'staging')), stdin=subprocess.DEVNULL, capture_output=True, timeout=60)
            self.assertEqual(0, process.returncode, process.stdout.decode() + process.stderr.decode())
            outputs = list(source.glob('regrade-*'))
            self.assertEqual(1, len(outputs))
            result = json.loads((outputs[0] / 'summary.json').read_text(encoding='utf-8-sig'))
            for path, data in original.items(): self.assertEqual(data, path.read_bytes(), str(path))
            self.assertEqual(expected, result['trials'][0]['primaryScore'], json.dumps(result['trials'][0], indent=2))
            self.assertEqual('clean', result['trials'][0]['integrity']['state'])
            self.assertEqual('clean' if right_clean else 'invalid', result['trials'][1]['integrity']['state'])
            if right_clean:
                if mode == 'split':
                    self.assertIsNone(result['trials'][1]['primaryScore'])
                    self.assertEqual(0, len(result['pairs']))
                    self.assertIsNone(result['confidenceInterval95'])
                    self.assertEqual(2, result['grading']['trialsWithJudgeDisagreement'])
                else:
                    self.assertEqual(1, result['trials'][1]['primaryScore'])
                    self.assertEqual(1, len(result['pairs']))
                    self.assertEqual(0, result['meanPairedDifferenceBMinusA'])
                    self.assertEqual(0, result['confidenceInterval95']['lower'])
            else:
                self.assertIsNone(result['trials'][1]['primaryScore'])
            self.assertEqual(2, result['integrity']['attempted'])
            self.assertTrue(result['integrityReused'])
            self.assertEqual(str(source), result['sourceRun'])
            self.assertEqual(int(mode == 'malformed' or tamper), result['grading']['harnessDefects'])
            for row in result['trials']:
                self.assertTrue(all(Path(p).exists() for p in row['files'].values()))
            if expected is None:
                self.assertIsNone(result['arms'][0]['passAt1'])
                self.assertIsNone(result['arms'][0]['meanPrimaryScore'])

    def test_calibration_runs_the_judge_pair_on_stored_and_known_controls(self):
        from unittest.mock import patch
        from SemanticJudge import strict_json
        def command(request, family=None):
            context = strict_json('{' + request['prompt'].split('\n{', 1)[1])
            answer = context['finalMessage']
            met = 'plural before locative' in answer
            violated = '-zz' in answer
            return json.dumps({'required': [{'index': 0, 'status': 'met' if met else 'unmet', 'quote': answer if met else None}],
                'mustNot': [{'index': 0, 'status': 'violated' if violated else 'not_violated', 'quote': answer if violated else None}]})
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            _, grammar, source = self.fixture(root)
            output = root / 'calibration'
            output.mkdir()
            with patch('SemanticJudge.live_judge', side_effect=command):
                self.assertEqual(0, calibrate(source, grammar, output))
            result = json.loads((output / 'calibration.json').read_text())
            self.assertEqual(4, len(result['results']))
            self.assertEqual(1, len(result['skipped']))
            self.assertTrue(all(len(r['judge']['judges']) == 2 for r in result['results']))
            self.assertEqual([1, 1, 0, 0], [r['judge']['score'] for r in result['results']])
