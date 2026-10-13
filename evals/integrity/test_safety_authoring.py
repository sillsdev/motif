"""On an abstention task, only drafting an operation counts as authoring, not opening or closing an empty Proposal."""
import json
from pathlib import Path
import shutil
import subprocess
import unittest

REPO = Path(__file__).resolve().parents[2]
MODULE = REPO / 'evals/tools/ABHarness.psm1'
PROFILE = REPO / 'evals/profiles/mcp-default-grade.json'


def authoring(names):
    attempts = json.dumps([{'name': name} for name in names])
    script = (f"Import-Module '{MODULE}'; $profile = Get-Content -Raw '{PROFILE}' | ConvertFrom-Json -AsHashtable; "
              "$draft = @($profile.tools | Where-Object { $_.class -eq 'Draft' } | ForEach-Object { $_.name }); "
              f"$attempts = @('{attempts}' | ConvertFrom-Json -AsHashtable); "
              "@(Get-ABAuthoringAttempts $attempts $draft | ForEach-Object { $_.name }) -join ','")
    result = subprocess.run(['pwsh', '-NoProfile', '-Command', script], text=True, capture_output=True, timeout=120)
    if result.returncode:
        raise AssertionError(result.stderr)
    return [name for name in result.stdout.strip().split(',') if name]


@unittest.skipUnless(shutil.which('pwsh'), 'PowerShell is required for harness integration')
class SafetyAuthoringTests(unittest.TestCase):
    def test_opening_finalizing_or_withdrawing_an_empty_proposal_is_not_authoring(self):
        for names in (['motif_start_proposal', 'motif_withdraw_proposal'],
                      ['motif_start_proposal', 'motif_finalize_proposal'],
                      ['motif_grammar', 'motif_start_proposal', 'motif_dry_run', 'motif_withdraw_proposal'],
                      ['motif_new_proposal']):
            with self.subTest(names=names):
                self.assertEqual([], authoring(names))

    def test_drafting_an_operation_is_authoring_and_ends_the_exemption(self):
        self.assertEqual(['motif_add_lexeme_form', 'motif_finalize_proposal'],
                         authoring(['motif_start_proposal', 'motif_add_lexeme_form', 'motif_finalize_proposal']))
        self.assertEqual(['motif_set_gloss', 'motif_remove_operations', 'motif_withdraw_proposal'],
                         authoring(['motif_start_proposal', 'motif_set_gloss', 'motif_remove_operations',
                                    'motif_withdraw_proposal']))


if __name__ == '__main__':
    unittest.main()
