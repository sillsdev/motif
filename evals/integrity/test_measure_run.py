"""Cost and effort measures are counted from stored transcripts, deterministically."""
import csv
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'tools/MeasureRun.py'


def event(payload):
    return {'source': 'host-output', 'stream': 'stdout', 'payload': json.dumps(payload)}


def call(tool, text, arguments=None, error=None):
    return event({'type': 'item.completed', 'item': {'type': 'mcp_tool_call', 'tool': tool, 'arguments': arguments or {},
                                                   'result': {'content': [{'type': 'text', 'text': text}]}, 'error': error}})


class MeasureRunTests(unittest.TestCase):
    def trial(self, run, arm, number, success, records, cost=None, activity=None, expected=None):
        trial = run / 'trials/task-a' / arm / f'trial-{number}'
        trial.mkdir(parents=True)
        (trial / 'transcript.jsonl').write_text('\n'.join(json.dumps(r) for r in records) + '\n')
        (trial / 'grade.json').write_text(json.dumps({'grade': 1.0 if success else 0.0, 'success': success}))
        (trial / 'integrity.json').write_text(json.dumps({'state': 'clean'}))
        manifest = {'armId': arm, 'costUsd': cost}
        if expected is not None:
            manifest['expectedComposers'] = expected
        (trial / 'manifest.json').write_text(json.dumps(manifest))
        if activity is not None:
            (trial / 'frozen').mkdir()
            (trial / 'frozen/activity.jsonl').write_text('\n'.join(json.dumps(a) for a in activity) + '\n')

    def measure(self, run, **environment):
        subprocess.run([sys.executable, str(SCRIPT), str(run)], check=True, capture_output=True, stdin=subprocess.DEVNULL,
                       env=dict(os.environ, **{'MOTIF_TEST_GRAMMARS': '', **environment}))
        return list(csv.DictReader(open(run / 'metrics.csv')))

    def test_counts_tokens_calls_reads_and_divides_spend_by_successes(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            usage = {'type': 'turn.completed', 'usage': {'input_tokens': 1000, 'cached_input_tokens': 400,
                                                         'output_tokens': 100, 'reasoning_output_tokens': 60}}
            summary = {'source': 'host-summary', 'payload': {'wallMs': 5000, 'turns': 1}}
            self.trial(run, 'arm-a', 1, True, [call('motif_lexicon', 'x' * 2000), call('motif_guide', 'g', {'topic': 'slots'}),
                                               call('motif_guide', 'g', {'topic': 'slots'}), event(usage), summary])
            self.trial(run, 'arm-a', 2, False, [call('motif_grammar', 'y', error='boom'), event(usage), summary])
            result = subprocess.run([sys.executable, str(SCRIPT), str(run)],
                                    capture_output=True, text=True, stdin=subprocess.DEVNULL)
            self.assertEqual(0, result.returncode, result.stderr)
            rows = list(csv.DictReader(open(run / 'metrics.csv')))
            first = rows[0]
            self.assertEqual(('1000', '400', '100', '60', '1100'), (first['inputTokens'], first['cachedInputTokens'],
                             first['outputTokens'], first['reasoningTokens'], first['totalTokens']))
            self.assertEqual(('3', '1', '2.0', 'motif_guide=2 motif_lexicon=1'),
                             (first['mcpCalls'], first['guideTopics'], first['resultKiloChars'], first['toolCounts']))
            self.assertEqual('1', rows[1]['mcpErrors'])
            summary_text = (run / 'metrics.md').read_text()
            self.assertIn('| arm-a | task-a | 1/2 | 2200 | incomplete costs | incomplete costs |', summary_text)

    def test_counts_calls_from_the_bridge_when_the_host_reports_none_of_its_own(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            use = event({'type': 'assistant', 'message': {'content': [{'type': 'tool_use', 'name': 'mcp__motif__motif_lexicon'}]}})
            summary = {'source': 'host-summary', 'payload': {'inputTokens': 900, 'outputTokens': 90, 'wallMs': 4000, 'turns': 3}}
            self.trial(run, 'claude-arm', 1, True, [use, summary])
            calls = [('motif_lexicon', {'query': 'qaje'}, {'result': {'content': [{'type': 'text', 'text': 'x' * 1500}]}}),
                     ('motif_guide', {'topic': 'workflow'}, {'result': {'content': [{'type': 'text', 'text': 'g'}]}}),
                     ('motif_set_gloss', {}, {'result': {'isError': True, 'content': [{'type': 'text', 'text': 'bad'}]}})]
            records = [{'source': 'mcp-client', 'direction': 'sent', 'payload': {'method': 'tools/list', 'id': 1}}]
            for number, (tool, arguments, response) in enumerate(calls, start=2):
                records.append({'source': 'mcp-client', 'direction': 'sent',
                                'payload': {'method': 'tools/call', 'id': number, 'params': {'name': tool, 'arguments': arguments}}})
                records.append({'source': 'mcp-client', 'direction': 'received', 'payload': dict(response, id=number)})
            trial = run / 'trials/task-a/claude-arm/trial-1'
            (trial / 'server.mcp-transcript.jsonl').write_text('\n'.join(json.dumps(r) for r in records) + '\n')
            subprocess.run([sys.executable, str(SCRIPT), str(run)], check=True, capture_output=True, stdin=subprocess.DEVNULL)
            row = next(csv.DictReader(open(run / 'metrics.csv')))
            self.assertEqual(('3', '1', '1', '1.5', '990'), (row['mcpCalls'], row['mcpErrors'], row['guideTopics'],
                             row['resultKiloChars'], row['totalTokens']))
            self.assertEqual('motif_guide=1 motif_lexicon=1 motif_set_gloss=1', row['toolCounts'])

    def test_price_to_solve_counts_actual_cost_of_failed_scored_episodes(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            (run / 'arms').mkdir()
            self.trial(run, 'arm-a', 1, True, [], cost=1.0)
            self.trial(run, 'arm-a', 2, False, [], cost=2.0)
            subprocess.run([sys.executable, str(SCRIPT), str(run)], check=True, capture_output=True, stdin=subprocess.DEVNULL)
            row = next(csv.DictReader(open(run / 'metrics.csv')))
            self.assertEqual(1.0, float(row['costUsd']))
            summary = (run / 'metrics.md').read_text()
            self.assertIn('| arm-a | ALL | 1/2 | 0 | 3.0 | 1.5 |', summary)

    def test_tokens_to_solve_counts_retried_cloud_attempts_like_their_cost(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            usage = {'type': 'turn.completed', 'usage': {'input_tokens': 1000, 'output_tokens': 100}}
            self.trial(run, 'arm-a', 1, True, [event(usage)], cost=1.0)
            trial = run / 'trials/task-a/arm-a/trial-1'
            attempt = trial / 'cloud-attempts/attempt-1'
            attempt.mkdir(parents=True)
            (attempt / 'transcript.jsonl').write_text(json.dumps(event(usage)) + '\n')
            manifest = json.loads((trial / 'manifest.json').read_text())
            manifest['unpricedRetryAttempts'] = 1
            (trial / 'manifest.json').write_text(json.dumps(manifest))
            subprocess.run([sys.executable, str(SCRIPT), str(run)], check=True, capture_output=True, stdin=subprocess.DEVNULL)
            row = next(csv.DictReader(open(run / 'metrics.csv')))
            self.assertEqual(('1100', '1100'), (row['totalTokens'], row['retryTokens']))
            summary = (run / 'metrics.md').read_text()
            self.assertIn('| arm-a | ALL | 1/1 | 2200 | 1.0 | 1.0 | 2200 |', summary)
            self.assertIn('1 retried attempts reported no cost', summary)

    ACTIVITY = [
        {'tool': 'motif_overview', 'isError': True, 'code': 'baseline.missing', 'composers': [], 'followedNext': None},
        {'tool': 'motif_add_phoneme', 'isError': True, 'code': 'tool.missing-argument', 'composers': ['compose-author-phoneme'], 'followedNext': False},
        {'tool': 'motif_add_phoneme', 'isError': True, 'code': 'tool.missing-argument', 'composers': ['compose-author-phoneme'], 'followedNext': True},
        {'tool': 'motif_dry_run', 'isError': False, 'code': None, 'composers': [], 'followedNext': True},
        {'tool': 'motif_dry_run', 'isError': False, 'code': None},
    ]

    def test_counts_refusals_by_code_and_whether_the_agent_followed_next(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            self.trial(run, 'arm-a', 1, True, [], activity=self.ACTIVITY)
            row = self.measure(run)[0]
            self.assertEqual(('3', 'baseline.missing=1 tool.missing-argument=2', '2', '1'),
                             (row['refusals'], row['refusalCodes'], row['followedNext'], row['ignoredNext']))
            summary = (run / 'metrics.md').read_text()
            self.assertIn('Mean refusals', summary)
            self.assertIn('| 3.0 | n/a | 2 / 1 |', summary)

    def test_found_composer_is_none_when_the_task_declares_none(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            self.trial(run, 'arm-a', 1, True, [], activity=self.ACTIVITY)
            self.assertEqual('', self.measure(run)[0]['foundComposer'])

    def test_found_composer_is_true_only_when_a_call_addressed_an_expected_composer(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            self.trial(run, 'arm-a', 1, True, [], activity=self.ACTIVITY, expected=['compose-author-phoneme'])
            self.trial(run, 'arm-a', 2, False, [], activity=self.ACTIVITY, expected=['compose-author-affix-slot'])
            self.trial(run, 'arm-a', 3, False, [], activity=[], expected=['compose-author-phoneme'])
            rows = self.measure(run)
            self.assertEqual(['True', 'False', 'False'], [row['foundComposer'] for row in rows])
            self.assertIn('| 2.0 | 1/3 |', (run / 'metrics.md').read_text())

    def test_expected_composers_fall_back_to_the_task_definition(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            grammars = run / 'grammars'
            task_dir = grammars / 'sets/set-x/tasks/task-a'
            task_dir.mkdir(parents=True)
            (task_dir / 'task.yaml').write_text(json.dumps({'id': 'task-a', 'expectedComposers': ['compose-author-phoneme']}))
            self.trial(run, 'arm-a', 1, True, [], activity=self.ACTIVITY)
            self.assertEqual('True', self.measure(run, MOTIF_TEST_GRAMMARS=str(grammars))[0]['foundComposer'])

    def test_a_trial_with_no_server_activity_has_zero_counts_and_a_blank_composer(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            self.trial(run, 'arm-a', 1, True, [])
            row = self.measure(run)[0]
            self.assertEqual(('0', '', '0', '0', ''), (row['refusals'], row['refusalCodes'], row['followedNext'],
                                                          row['ignoredNext'], row['foundComposer']))

    def test_columns_that_existed_before_the_service_measures_keep_their_values(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            usage = {'type': 'turn.completed', 'usage': {'input_tokens': 1000, 'cached_input_tokens': 400,
                                                         'output_tokens': 100, 'reasoning_output_tokens': 60}}
            summary = {'source': 'host-summary', 'payload': {'wallMs': 5000, 'turns': 2}}
            self.trial(run, 'arm-a', 1, True,
                       [call('motif_lexicon', 'x' * 2000), call('motif_add_phoneme', 'p', error='boom'),
                        call('motif_guide', 'g', {'topic': 'slots'}), event(usage), summary],
                       cost=1.5, activity=self.ACTIVITY, expected=['compose-author-phoneme'])
            row = self.measure(run)[0]
            expected = {
                'task': 'task-a', 'arm': 'arm-a', 'trial': 'trial-1', 'model': '', 'integrity': 'clean', 'grade': '1.0',
                'success': 'True', 'inputTokens': '1000', 'cachedInputTokens': '400', 'cacheWriteTokens': '0',
                'outputTokens': '100', 'reasoningTokens': '60', 'totalTokens': '1100', 'wallSeconds': '5.0',
                'turns': '2', 'mcpCalls': '3', 'mcpErrors': '1', 'readCalls': '2', 'draftCalls': '1',
                'distinctTools': '3', 'guideTopics': '1', 'resourcesRead': '0', 'resultKiloChars': '2.0',
                'shellCommands': '0', 'toolCounts': 'motif_add_phoneme=1 motif_guide=1 motif_lexicon=1',
                'retryTokens': '0', 'costUsd': '1.5', 'unpricedRetryAttempts': '0',
            }
            self.assertEqual(expected, {key: row[key] for key in expected})
            self.assertEqual(list(expected) + ['refusals', 'refusalCodes', 'followedNext', 'ignoredNext', 'foundComposer'],
                             list(row))


if __name__ == '__main__':
    unittest.main()
