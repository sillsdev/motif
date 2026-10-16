"""The Agent budget counts turns, tool calls and tokens; wall time is only a safety kill."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / 'evals/tools'))
from AgentBoundary import ToolBudget
MODULE = REPO / 'evals/tools/IsolatedHost.psm1'


def quote(value):
    return "'" + str(value).replace("'", "''") + "'"


def budgeted(child, budget, transcript, record=None):
    """Runs a Python child under the Agent budget and returns the run's outcome."""
    script = (f"Import-Module {quote(MODULE)} -Force -DisableNameChecking; "
              f"$budget = @{{ turns = {budget['turns']}; toolCalls = {'$null' if budget['toolCalls'] is None else budget['toolCalls']}; "
              f"tokens = {budget['tokens']}; safetySeconds = {budget['safetySeconds']} }}; "
              f"$run = Invoke-ABBudgetedAgent {quote(sys.executable)} @('-c', {quote(child)}) "
              f"{quote(Path(transcript).parent)} $budget {quote(transcript)} {quote(record or Path(transcript).parent / 'none.json')}; "
              "$run | Select-Object FailureClass, Failure, ExitCode, ToolCalls, Tokens, Turns | ConvertTo-Json -Compress")
    result = subprocess.run(['pwsh', '-NoProfile', '-Command', script], capture_output=True, text=True, timeout=120)
    if result.returncode:
        raise AssertionError(result.stderr or result.stdout)
    return json.loads(result.stdout.strip().splitlines()[-1])


def call_line(tool, arguments, request_id):
    payload = {'jsonrpc': '2.0', 'method': 'tools/call', 'id': request_id, 'params': {'name': tool, 'arguments': arguments}}
    return json.dumps({'atUtc': 0, 'source': 'mcp-client', 'direction': 'sent', 'payload': payload})


BUDGET = {'turns': 60, 'toolCalls': None, 'tokens': 1500000, 'safetySeconds': 60}


class AgentBudgetTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.transcript = Path(self.directory.name) / 'mcp-transcript.jsonl'
        self.transcript.write_text('')

    def tearDown(self):
        self.directory.cleanup()

    def test_a_slow_silent_agent_is_not_a_failure(self):
        run = budgeted("import time; time.sleep(4); print('done')", BUDGET, self.transcript)
        self.assertIsNone(run['FailureClass'], run['Failure'])
        self.assertEqual(0, run['ExitCode'])

    def test_the_safety_kill_is_a_harness_defect(self):
        run = budgeted("import time; time.sleep(60)", dict(BUDGET, safetySeconds=2), self.transcript)
        self.assertEqual('harness_defect', run['FailureClass'])
        self.assertIn('safety wall kill', run['Failure'])

    def test_tool_calls_beyond_the_budget_are_an_agent_failure(self):
        lines = [call_line('motif_grammar', {}, number) for number in range(3)]
        child = f"open({str(self.transcript)!r}, 'a').write({chr(10).join(lines) + chr(10)!r})"
        run = budgeted(child, dict(BUDGET, toolCalls=2), self.transcript)
        self.assertEqual('agent_failure', run['FailureClass'])
        self.assertIn('2 tool calls', run['Failure'])

    def test_a_run_with_no_tool_call_budget_is_never_failed_for_tool_calls(self):
        lines = [call_line('motif_grammar', {}, number) for number in range(200)]
        child = f"open({str(self.transcript)!r}, 'a').write({chr(10).join(lines) + chr(10)!r}); print('done')"
        run = budgeted(child, BUDGET, self.transcript)
        self.assertIsNone(run['FailureClass'], run['Failure'])

    def test_waiting_on_a_running_job_does_not_spend_the_tool_call_budget(self):
        lines = [call_line('motif_trial', {'proposal': 'p'}, 1)] + \
            [call_line('motif_trial', {'proposal': 'p', 'job': 'j'}, number) for number in range(2, 6)]
        child = f"open({str(self.transcript)!r}, 'a').write({chr(10).join(lines) + chr(10)!r}); print('done')"
        run = budgeted(child, dict(BUDGET, toolCalls=1), self.transcript)
        self.assertIsNone(run['FailureClass'], run['Failure'])
        self.assertEqual(1, run['ToolCalls'])

    def test_tokens_count_cached_input_the_same_way_for_every_host(self):
        event = {'type': 'assistant', 'message': {'id': 'm1', 'usage': {
            'input_tokens': 100, 'cache_read_input_tokens': 5000, 'cache_creation_input_tokens': 400, 'output_tokens': 10}}}
        child = f"print({json.dumps(event)!r}, flush=True)"
        run = budgeted(child, dict(BUDGET, tokens=1000), self.transcript)
        self.assertEqual('agent_failure', run['FailureClass'])
        self.assertIn('1000 tokens', run['Failure'])
        self.assertEqual(5510, run['Tokens'])

    def test_a_bridge_refusal_is_the_agent_failure_and_the_agent_ends_on_its_own(self):
        record = Path(self.directory.name) / 'budget.json'
        evidence = json.dumps({'exhausted': 'The Agent budget of 2 tool calls was exhausted.'})
        child = f"open({str(record)!r}, 'w').write({evidence!r}); print('done')"
        run = budgeted(child, BUDGET, self.transcript, record)
        self.assertEqual('agent_failure', run['FailureClass'])
        self.assertIn('2 tool calls', run['Failure'])
        self.assertEqual(124, run['ExitCode'])

    def test_turns_beyond_the_budget_are_an_agent_failure(self):
        event = {'type': 'system', 'num_turns': 4}
        child = f"print({json.dumps(event)!r}, flush=True)"
        run = budgeted(child, dict(BUDGET, turns=3), self.transcript)
        self.assertEqual('agent_failure', run['FailureClass'])
        self.assertIn('3 turns', run['Failure'])


class BridgeBudgetTests(unittest.TestCase):
    def call(self, request_id, **arguments):
        return {'jsonrpc': '2.0', 'id': request_id, 'method': 'tools/call', 'params': {'name': 'motif_trial', 'arguments': arguments}}

    def test_the_call_after_the_budget_gets_an_error_result_and_job_waits_are_free(self):
        budget = ToolBudget(2)
        self.assertIsNone(budget.refusal(self.call(1, proposal='p')))
        self.assertIsNone(budget.refusal(self.call(2, proposal='p', job='j')))
        self.assertIsNone(budget.refusal({'jsonrpc': '2.0', 'id': 3, 'method': 'tools/list'}))
        self.assertIsNone(budget.refusal(self.call(4, proposal='p')))
        refused = budget.refusal(self.call(5, proposal='p'))
        self.assertEqual(5, refused['id'])
        self.assertTrue(refused['result']['isError'])
        self.assertIn('2 tool calls', refused['result']['content'][0]['text'])
        self.assertEqual('The Agent budget of 2 tool calls was exhausted.', budget.exhausted)
        self.assertEqual(2, budget.used)

    def test_without_a_budget_nothing_is_refused(self):
        budget = ToolBudget(None)
        for number in range(200):
            self.assertIsNone(budget.refusal(self.call(number)))


class BudgetResolutionTests(unittest.TestCase):
    def resolve(self, limits, environment=''):
        script = (f"{environment}Import-Module {quote(MODULE)} -Force -DisableNameChecking; "
                  f"Get-ABAgentBudget ('{json.dumps(limits)}' | ConvertFrom-Json -AsHashtable) | ConvertTo-Json -Compress")
        return subprocess.run(['pwsh', '-NoProfile', '-Command', script], capture_output=True, text=True, timeout=60)

    def test_defaults_and_the_safety_kill_floor(self):
        result = self.resolve({'turns': 60, 'wall_seconds': 1800})
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual({'turns': 60, 'toolCalls': None, 'tokens': 1500000, 'safetySeconds': 3600},
                         json.loads(result.stdout))

    def test_task_limits_win_and_a_long_wall_raises_the_kill(self):
        result = self.resolve({'turns': 10, 'tool_calls': 12, 'tokens': 9000, 'wall_seconds': 7200})
        self.assertEqual({'turns': 10, 'toolCalls': 12, 'tokens': 9000, 'safetySeconds': 7200},
                         json.loads(result.stdout))

    def test_time_is_not_an_agent_budget(self):
        result = self.resolve({'turns': 60, 'time_seconds': 600})
        self.assertNotEqual(0, result.returncode)
        self.assertIn('time_seconds', result.stderr + result.stdout)


if __name__ == '__main__':
    unittest.main()
