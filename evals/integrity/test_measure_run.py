"""Cost and effort measures are counted from stored transcripts, deterministically."""
import csv
import json
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
    def trial(self, run, arm, number, success, records, cost=None):
        trial = run / 'trials/task-a' / arm / f'trial-{number}'
        trial.mkdir(parents=True)
        (trial / 'transcript.jsonl').write_text('\n'.join(json.dumps(r) for r in records) + '\n')
        (trial / 'grade.json').write_text(json.dumps({'grade': 1.0 if success else 0.0, 'success': success}))
        (trial / 'integrity.json').write_text(json.dumps({'state': 'clean'}))
        (trial / 'manifest.json').write_text(json.dumps({'armId': arm, 'costUsd': cost}))

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


if __name__ == '__main__':
    unittest.main()
