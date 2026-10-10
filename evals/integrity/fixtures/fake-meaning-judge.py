"""Offline command seam: explicit fixtures, plus scripted validity answers."""
import json
import os
from pathlib import Path
import sys

request = json.load(sys.stdin)
context = json.loads('{' + request['prompt'].split('\n{', 1)[1])
answer = context['finalMessage']
mode = os.environ.get('MOTIF_FAKE_JUDGE_MODE', 'pass')
if mode == 'malformed':
    print('not JSON')
    sys.exit(0)
if mode == 'exit':
    sys.exit(1)
if mode == 'fixtures':
    rows = json.loads((Path(__file__).parent / 'semantic-finals.json').read_text())
    row = next(r for r in rows if r['finalMessage'] == answer)
    met = row['task'].find('diagnose') >= 0
elif mode == 'validity':
    rows = json.loads((Path(__file__).parent / 'validity-finals.json').read_text())
    row = next((r for r in rows if r['required'] == context['meaning']['required']), None)
    met = row is not None and row['gold'] == answer
else:
    met = bool(answer) and mode != 'fail'
if mode == 'split' and request['family'] == 'sol':
    met = False
violated = mode == 'violation'
print(json.dumps({
    'required': [{'index': i, 'status': 'met' if met else 'unmet', 'quote': answer if met else None}
                 for i, _ in enumerate(context['meaning']['required'])],
    'mustNot': [{'index': i, 'status': 'violated' if violated else 'not_violated', 'quote': answer if violated else None}
                for i, _ in enumerate(context['meaning'].get('mustNot', []))]}))
