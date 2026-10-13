"""A live run's results are copied outside the checkout, so removing a worktree cannot lose the evidence."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
MODULE = REPO / 'evals/tools/ABHarness.psm1'


def archive(run_root, archive_root, repo=REPO):
    script = (f"Import-Module '{MODULE}'; try {{ Copy-ABRunArchive '{repo}' '{run_root}' 'question' 'run-1' }} "
              "catch { 'refused: ' + $_.Exception.Message }")
    environment = dict(os.environ, MOTIF_AB_ARCHIVE=str(archive_root))
    result = subprocess.run(['pwsh', '-NoProfile', '-Command', script], text=True, capture_output=True,
                            timeout=120, env=environment)
    return result.stdout.strip()


@unittest.skipUnless(shutil.which('pwsh'), 'PowerShell is required for harness integration')
class RunArchiveTests(unittest.TestCase):
    def test_run_is_copied_whole_and_never_overwritten(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            run = root / 'results/question/run-1'
            (run / 'trials/task/arm/trial-1').mkdir(parents=True)
            (run / 'summary.json').write_text('{"runId": "run-1"}')
            (run / 'trials/task/arm/trial-1/transcript.jsonl').write_text('{"turn": 1}\n')
            destination = archive(run, root / 'archive')
            self.assertEqual(str(root / 'archive/question/run-1'), destination)
            self.assertEqual({'runId': 'run-1'}, json.loads((Path(destination) / 'summary.json').read_text()))
            self.assertTrue((Path(destination) / 'trials/task/arm/trial-1/transcript.jsonl').is_file())
            self.assertTrue(archive(run, root / 'archive').startswith('refused: '))

    def test_archive_inside_the_checkout_is_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            outcome = archive(Path(directory), REPO / 'evals/results/archive')
        self.assertTrue(outcome.startswith('refused: '), outcome)
        self.assertIn('outside the checkout', outcome)
        self.assertFalse((REPO / 'evals/results/archive').exists())

    def question(self, arm, archive_root):
        environment = dict(os.environ, MOTIF_AB_ARCHIVE=str(archive_root),
                           MOTIF_TEST_GRAMMARS=os.environ.get('MOTIF_TEST_GRAMMARS', '/home/johnm/work/motif-test-grammars'))
        with tempfile.TemporaryDirectory() as directory:
            question = Path(directory) / 'archive-check.yaml'
            question.write_text(json.dumps({'question': 'Archive check', 'arm_a': arm, 'arm_b': arm,
                                            'tasks': '*', 'trials': 1, 'primary_metric': 'grade'}))
            result = subprocess.run(['pwsh', '-NoProfile', '-File', str(REPO / 'evals/Invoke-ABQuestion.ps1'),
                                     '-Question', str(question), '-Tasks', 'no-such-task'], text=True,
                                    capture_output=True, timeout=300, cwd=REPO, env=environment)
        return result.stdout + result.stderr

    def test_a_live_run_refuses_an_archive_inside_the_checkout_before_any_work(self):
        self.assertIn('outside the checkout', self.question('codex-default', REPO / 'evals/results/archive'))

    def test_a_fake_run_is_not_archived(self):
        output = self.question('fake-default', REPO / 'evals/results/archive')
        self.assertNotIn('outside the checkout', output)
        self.assertIn('did not select any eval tasks', output)


if __name__ == '__main__':
    unittest.main()
