"""The inference broker counts every host's turns and tokens the same way and ends an Episode at its budget."""
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from InferenceBroker import Broker, response_usage
import test_inference_broker as base
from test_inference_broker import Response

OPENAI_STREAM = (b'event: response.created\ndata: {"type":"response.created"}\n\n'
                 b'event: response.completed\ndata: {"type":"response.completed","response":{"usage":'
                 b'{"input_tokens":900,"input_tokens_details":{"cached_tokens":800},"output_tokens":200}}}\n\n')
ANTHROPIC_STREAM = (b'event: message_start\ndata: {"type":"message_start","message":{"usage":{"input_tokens":10,'
                    b'"cache_read_input_tokens":2000,"cache_creation_input_tokens":300,"output_tokens":1}}}\n\n'
                    b'event: message_delta\ndata: {"type":"message_delta","usage":{"output_tokens":50}}\n\n')


class UsageTests(unittest.TestCase):
    def test_openai_input_already_includes_cached_tokens(self):
        self.assertEqual(1100, response_usage(OPENAI_STREAM))

    def test_anthropic_cached_input_is_added_and_output_is_the_final_count(self):
        self.assertEqual(2360, response_usage(ANTHROPIC_STREAM))

    def test_plain_json_and_unreadable_responses(self):
        self.assertEqual(30, response_usage(json.dumps({'usage': {'input_tokens': 20, 'output_tokens': 10}}).encode()))
        self.assertEqual(0, response_usage(b'not json'))


class BrokerBudgetTests(unittest.TestCase):
    setUp = base.BrokerTests.setUp
    login = base.BrokerTests.login
    broker = base.BrokerTests.broker
    frame = base.BrokerTests.frame

    def budgeted(self, **budget):
        return Broker('codex', 'gpt-6-luna', self.archive, budget=dict({'turns': 60, 'tokens': 1500000}, **budget))

    def test_the_request_after_the_token_budget_is_refused_as_an_agent_failure(self):
        broker = self.budgeted(tokens=1000)
        with patch.object(broker.opener, 'open', return_value=Response(OPENAI_STREAM, content_type='text/event-stream')) as opened:
            first = broker.forward(self.frame())
            refused = broker.forward(self.frame())
            again = broker.forward(self.frame())
        self.assertEqual(200, first[0])
        self.assertEqual(1, opened.call_count)
        self.assertEqual(400, refused[0])
        self.assertIn(b'Agent budget', refused[2])
        self.assertEqual(400, again[0])
        failures = broker.episode_failures()
        self.assertEqual(1, len(failures))
        self.assertEqual('agent_failure', failures[0]['failureClass'])
        self.assertIn('1000 tokens', failures[0]['evidence'])
        self.assertFalse(failures[0]['recovered'])
        self.assertEqual({'turns': 1, 'tokens': 1100}, broker.spent)

    def test_a_turn_is_one_model_request_and_the_budget_caps_them(self):
        broker = self.budgeted(turns=2)
        with patch.object(broker.opener, 'open', side_effect=lambda *args, **kwargs: Response(b'{}')) as opened:
            statuses = [broker.forward(self.frame())[0] for _ in range(3)]
        self.assertEqual([200, 200, 400], statuses)
        self.assertEqual(2, opened.call_count)
        self.assertIn('2 turns', broker.episode_failures()[0]['evidence'])

    def test_without_a_budget_the_broker_only_counts(self):
        broker = self.broker()
        with patch.object(broker.opener, 'open', side_effect=lambda *args, **kwargs: Response(OPENAI_STREAM)):
            for _ in range(3):
                self.assertEqual(200, broker.forward(self.frame())[0])
        self.assertEqual({'turns': 3, 'tokens': 3300}, broker.spent)
        self.assertEqual([], broker.episode_failures())


if __name__ == '__main__':
    unittest.main()
