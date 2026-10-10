"""Host-only calibration of meaning judgments on stored finals and known controls."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import time

from SemanticJudge import judge


def calibrate(source_root, set_root, output_root):
    summary = json.loads((source_root / 'summary.json').read_text(encoding='utf-8-sig'))
    results = []
    skipped = []
    controls = set()
    for trial in summary['trials']:
        if trial['integrity']['state'] != 'clean':
            skipped.append({'task': trial['task'], 'arm': trial['arm'], 'trial': trial['trial'],
                            'reason': 'original integrity is not clean'})
            continue
        task_root = set_root / trial['set'] / 'tasks' / trial['task']
        task = json.loads((task_root / 'task.yaml').read_text(encoding='utf-8-sig'))
        grader = next((g for g in task['graders'] if g['type'] in ('answer', 'meaning')), None)
        if not grader:
            continue
        key = json.loads((task_root / grader.get('key', 'answer.yaml')).read_text(encoding='utf-8-sig'))
        if 'meaning' not in key:
            skipped.append({'task': trial['task'], 'reason': 'key has no meaning; deterministic grader applies'})
            continue
        frozen = source_root / 'trials' / trial['task'] / trial['arm'] / ('trial-' + str(trial['trial']))
        integrity = json.loads((frozen / 'integrity.json').read_text(encoding='utf-8-sig'))
        if integrity['state'] != 'clean' or not 0 < integrity['closedUtc'] < time.time():
            raise ValueError('Calibration requires a closed, clean trial')
        for name, expected_hash in integrity['bundleHashes'].items():
            if hashlib.sha256((frozen / 'frozen' / name).read_bytes()).hexdigest() != expected_hash:
                raise ValueError('The frozen trial bundle changed')
        answer = json.loads((frozen / 'frozen/host.json').read_text(encoding='utf-8-sig'))['finalMessage']
        prompt = (task_root / task['prompt']).read_text()
        cases = [('stored', answer, None)]
        if trial['task'] not in controls:
            controls.add(trial['task'])
            positive = '. '.join(key['meaning']['required']) + '.'
            cases += [('positive-control', positive, 1), ('negative-control', 'I cannot identify the problem.', 0),
                      ('must-not-control', positive + ' I propose a new affix or allomorph: -zz.', 0)]
        for name, message, expected in cases:
            result = judge(prompt, message, key['meaning'])
            results.append({'task': trial['task'], 'arm': trial['arm'], 'trial': trial['trial'], 'case': name,
                            'finalMessage': message, 'expectedScore': expected, 'judge': result})
            (output_root / 'calibration.json').write_text(json.dumps({'sourceRun': str(source_root),
                'results': results, 'skipped': skipped}, ensure_ascii=False, indent=2) + '\n')
            print(f"{trial['task']} / {trial['arm']} / {trial['trial']} / {name}: {result['score']}, split={result['disagreement']}", flush=True)
    (output_root / 'calibration.json').write_text(json.dumps({'sourceRun': str(source_root),
        'results': results, 'skipped': skipped}, ensure_ascii=False, indent=2) + '\n')
    failures = [r for r in results if r['judge']['state'] != 'measured' or
                (r['expectedScore'] is not None and r['expectedScore'] != r['judge']['score'])]
    splits = sum(r['judge']['disagreement'] for r in results)
    report = [f"Calibration produced {len(results)} judgments, {len(failures)} failed controls or infrastructure failures, and {splits} recorded splits.",
              '', 'Each judgment retains three independent samples. Review every stored answer and split against the rubric before accepting calibration.',
              '', 'Known controls:', '', '| Task | Control | Expected | Majority | Split |', '|---|---|---:|---:|---|']
    for row in results:
        if row['expectedScore'] is not None:
            report.append(f"| {row['task']} | {row['case']} | {row['expectedScore']} | {row['judge']['score']} | {row['judge']['disagreement']} |")
    (output_root / 'report.md').write_text('\n'.join(report) + '\n')
    return 1 if failures or not results else 0


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--run-directory', type=Path, required=True)
    parser.add_argument('--set-root', type=Path, required=True)
    args = parser.parse_args()
    output = args.run_directory / ('calibration-' + datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S-%f'))
    output.mkdir()
    print('Calibration: ' + str(output), flush=True)
    raise SystemExit(calibrate(args.run_directory, args.set_root, output))
