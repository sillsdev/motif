"""Deterministic cost and effort measures for an A/B run, harvested from stored trial records.

Usage: MeasureRun.py <run-dir> [--grades <regrade-dir>]
Writes metrics.csv (one row per trial) and metrics.md (per arm and task) into the grades directory.
Nothing here judges an answer: token, time and actual cost values come from records the trial wrote.
"""
import argparse
import csv
import json
from collections import Counter, defaultdict
from pathlib import Path

EVALS = Path(__file__).resolve().parents[1]
LIFECYCLE = ('motif_new_proposal', 'motif_start_proposal', 'motif_finalize_proposal')


def lines(path):
    if not path.is_file():
        return
    for line in path.read_text(encoding='utf-8').splitlines():
        try:
            yield json.loads(line)
        except ValueError:
            continue


def tool_classes():
    classes = {}
    for profile in sorted((EVALS / 'profiles').glob('*.json')):
        for tool in json.loads(profile.read_text()).get('tools', []):
            classes.setdefault(tool['name'], tool.get('class', 'Read'))
    return classes


def host_events(trial):
    for record in lines(trial / 'transcript.jsonl'):
        if record.get('source') != 'host-output' or not isinstance(record.get('payload'), str):
            continue
        try:
            event = json.loads(record['payload'])
        except ValueError:
            continue
        if isinstance(event, dict):
            yield event


def bridge_calls(trial):
    requests = {}
    for record in lines(trial / 'server.mcp-transcript.jsonl'):
        payload = record.get('payload')
        if not isinstance(payload, dict) or record.get('source') != 'mcp-client':
            continue
        if record.get('direction') == 'sent' and payload.get('method') == 'tools/call':
            params = payload.get('params') or {}
            requests[payload.get('id')] = (str(params.get('name', '')), params.get('arguments'))
        elif record.get('direction') == 'received' and payload.get('id') in requests:
            name, arguments = requests.pop(payload['id'])
            yield name, arguments, payload.get('result'), payload.get('error')
    for name, arguments in requests.values():
        yield name, arguments, None, 'no response'


def measure_trial(trial, classes):
    usage = Counter()
    calls = Counter()
    errors = 0
    result_chars = 0
    commands = 0
    guide_topics = set()
    resources = set()
    host_calls = []
    for event in host_events(trial):
        if event.get('type') == 'turn.completed':
            for key, value in (event.get('usage') or {}).items():
                if isinstance(value, (int, float)):
                    usage[key] += value
        item = event.get('item') if isinstance(event.get('item'), dict) else {}
        if event.get('type') != 'item.completed':
            continue
        if item.get('type') == 'command_execution':
            commands += 1
        if item.get('type') == 'mcp_tool_call':
            host_calls.append((item.get('tool', ''), item.get('arguments'), item.get('result'), item.get('error')))
    # Only Codex reports calls as events; for other hosts the bridge's record of the same traffic counts them.
    for name, arguments, result, error in host_calls or bridge_calls(trial):
        calls[name] += 1
        if error or (result or {}).get('isError'):
            errors += 1
        for part in (result or {}).get('content') or []:
            if isinstance(part, dict) and isinstance(part.get('text'), str):
                result_chars += len(part['text'])
        if name == 'motif_guide' and isinstance(arguments, dict):
            guide_topics.add(str(arguments.get('topic', '')))
    for record in lines(trial / 'server.mcp-transcript.jsonl'):
        payload = record.get('payload')
        if record.get('direction') == 'sent' and isinstance(payload, dict) and payload.get('method') == 'resources/read':
            resources.add(str((payload.get('params') or {}).get('uri', '')))
    host = {}
    for record in lines(trial / 'transcript.jsonl'):
        if record.get('source') == 'host-summary' and isinstance(record.get('payload'), dict):
            host = record['payload']
    if not usage and host:
        usage['input_tokens'] = host.get('inputTokens') or 0
        usage['output_tokens'] = host.get('outputTokens') or 0
    reads = sum(n for name, n in calls.items() if classes.get(name, 'Read') == 'Read')
    drafts = sum(n for name, n in calls.items() if classes.get(name) == 'Draft' and name not in LIFECYCLE)
    return {
        'inputTokens': int(usage['input_tokens']), 'cachedInputTokens': int(usage['cached_input_tokens']),
        'cacheWriteTokens': int(usage['cache_write_input_tokens']), 'outputTokens': int(usage['output_tokens']), 'reasoningTokens': int(usage['reasoning_output_tokens']),
        'totalTokens': int(usage['input_tokens'] + usage['output_tokens']),
        'wallSeconds': round((host.get('wallMs') or 0) / 1000, 1), 'turns': host.get('turns') or 0,
        'mcpCalls': sum(calls.values()), 'mcpErrors': errors, 'readCalls': reads, 'draftCalls': drafts,
        'distinctTools': len(calls), 'guideTopics': len(guide_topics - {''}), 'resourcesRead': len(resources - {''}),
        'resultKiloChars': round(result_chars / 1000, 1), 'shellCommands': commands,
        'toolCounts': ' '.join(f'{name}={n}' for name, n in sorted(calls.items())),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('run')
    parser.add_argument('--grades')
    args = parser.parse_args()
    run = Path(args.run)
    grades = Path(args.grades) if args.grades else run
    classes = tool_classes()
    rows = []
    for trial in sorted(run.glob('trials/*/*/trial-*')):
        task, arm, number = trial.parts[-3], trial.parts[-2], trial.name
        manifest = json.loads((trial / 'manifest.json').read_text()) if (trial / 'manifest.json').is_file() else {}
        grade_file = grades / 'trials' / task / arm / number / 'grade.json'
        if not grade_file.is_file():
            grade_file = trial / 'grade.json'
        grade = json.loads(grade_file.read_text()) if grade_file.is_file() else {}
        integrity = json.loads((trial / 'integrity.json').read_text()).get('state') if (trial / 'integrity.json').is_file() else None
        arm_file = EVALS / 'arms' / (arm + '.yaml')
        model = json.loads(arm_file.read_text()).get('model', '') if arm_file.is_file() else ''
        row = {'task': task, 'arm': arm, 'trial': number, 'model': model, 'integrity': integrity,
               'grade': grade.get('grade'), 'success': grade.get('success')}
        row.update(measure_trial(trial, classes))
        # Retried Cloud attempts are archived beside the Episode; their tokens are spent like the cost already counted.
        retried = [measure_trial(attempt, classes) for attempt in sorted(trial.glob('cloud-attempts/attempt-*'))]
        row['retryTokens'] = sum(attempt['totalTokens'] for attempt in retried)
        row['costUsd'] = manifest.get('costUsd')
        row['unpricedRetryAttempts'] = manifest.get('unpricedRetryAttempts') or 0
        rows.append(row)
    if not rows:
        raise SystemExit('No trials found under ' + str(run))
    with open(grades / 'metrics.csv', 'w', newline='', encoding='utf-8') as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    write_summary(grades / 'metrics.md', rows)
    print('Metrics:', grades / 'metrics.csv')
    print('Summary:', grades / 'metrics.md')


def per_success(total, successes):
    return round(total / successes) if successes else None


def write_summary(path, rows):
    groups = defaultdict(list)
    for row in rows:
        if row['integrity'] == 'clean' and row['success'] is not None:
            groups[(row['arm'], row['task'])].append(row)
            groups[(row['arm'], 'ALL')].append(row)
    out = ['# Cost and effort', '',
           'Counted from stored trial records; only clean, graded Episodes. Price to solve divides the actual '
           'spend on every scored Episode by the number solved, so Agent failures count against an Arm. Average '
           'cost per Episode appears beside it; missing cost data remains incomplete.', '',
           '| Arm | Task | Solved | Tokens to solve | Price to solve (USD) | Average cost / Episode (USD) | Mean tokens | Mean wall s '
           '| Mean MCP calls | Mean reads | Mean result kchars | Guide topics | MCP errors |',
           '|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|']
    for (arm, task), items in sorted(groups.items(), key=lambda kv: (kv[0][0], kv[0][1] == 'ALL', kv[0][1])):
        n = len(items)
        solved = sum(1 for row in items if row['success'])
        tokens = sum(row['totalTokens'] + row['retryTokens'] for row in items)
        costs = [row['costUsd'] for row in items]
        cost = sum(costs) if all(c is not None for c in costs) else None
        average_cost = cost / n if cost is not None and n else None
        mean = lambda key: round(sum(row[key] for row in items) / n, 1)
        tokens_to_solve = per_success(tokens, solved)
        out.append('| {} | {} | {}/{} | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} |'.format(
            arm, task, solved, n, tokens_to_solve if tokens_to_solve is not None else 'n/a',
            round(cost / solved, 4) if cost is not None and solved else 'incomplete costs' if cost is None else 'n/a',
            round(average_cost, 4) if average_cost is not None else 'incomplete costs',
            round(tokens / n), mean('wallSeconds'), mean('mcpCalls'), mean('readCalls'), mean('resultKiloChars'),
            mean('guideTopics'), sum(row['mcpErrors'] for row in items)))
    unpriced = sum(row['unpricedRetryAttempts'] for row in rows if row['integrity'] == 'clean' and row['success'] is not None)
    out += ['', 'Costs come from the Episode manifest, including recorded Cloud retry spend; tokens to solve and mean '
            'tokens include the tokens of retried attempts. A missing value is not replaced with a token-rate estimate.']
    if unpriced:
        out.append(f'{unpriced} retried attempts reported no cost, so those prices are lower bounds.')
    path.write_text('\n'.join(out) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
