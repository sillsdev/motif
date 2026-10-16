"""A tool the server offered but no grade profile or task capability classifies is a Harness defect."""
import json
from pathlib import Path
import shutil
import subprocess
import unittest

REPO = Path(__file__).resolve().parents[2]
MODULE = REPO / 'evals/tools/ABHarness.psm1'


def classes(served, profile, task):
    script = (f"$ErrorActionPreference='Stop'; Import-Module '{MODULE}' -Force; "
              f"$served = '{json.dumps(served)}' | ConvertFrom-Json; "
              f"$profile = @('{json.dumps(profile)}' | ConvertFrom-Json -AsHashtable); "
              f"$task = @('{json.dumps(task)}' | ConvertFrom-Json -AsHashtable); "
              "Get-ABServedToolClasses @($served) @($profile | Where-Object { $_ }) @($task | Where-Object { $_ }) | "
              "ConvertTo-Json -Compress -AsArray")
    return subprocess.run(['pwsh', '-NoProfile', '-Command', script], stdin=subprocess.DEVNULL,
                          capture_output=True, text=True, timeout=60)


@unittest.skipUnless(shutil.which('pwsh'), 'PowerShell is required for served-tool checks')
class ServedToolTests(unittest.TestCase):
    def test_profile_and_task_tools_classify_every_served_tool(self):
        result = classes(['motif_grammar', 'motif_edit_affix_template'], [{'name': 'motif_grammar', 'class': 'Read'}],
                         [{'name': 'motif_edit_affix_template', 'class': 'Draft'}])
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual([{'name': 'motif_grammar', 'class': 'Read'}, {'name': 'motif_edit_affix_template', 'class': 'Draft'}],
                         json.loads(result.stdout))

    def test_an_unclassified_served_tool_is_refused_not_graded_read(self):
        result = classes(['motif_grammar', 'motif_mystery'], [{'name': 'motif_grammar', 'class': 'Read'}], [])
        self.assertNotEqual(0, result.returncode)
        self.assertIn('motif_mystery', result.stdout + result.stderr)


if __name__ == '__main__':
    unittest.main()
