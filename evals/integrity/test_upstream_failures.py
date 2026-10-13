"""Upstream HTTP failures are classified by cause, and every request leaves network evidence."""
import base64
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import time
import unittest
from unittest.mock import patch
import urllib.error

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
sys.path.insert(0, str(Path(__file__).resolve().parent))
from InferenceBroker import Broker, CloudFailure, status_failure_class
import test_host_failures


def error_body(kind, message):
    return json.dumps({'type': 'error', 'error': {'type': kind, 'message': message}}).encode()


class Response(io.BytesIO):
    def __init__(self, content=b'{}', code=200):
        super().__init__(content)
        self.code = code
        self.headers = {'Content-Type': 'application/json'}


class StatusClassTests(unittest.TestCase):
    def test_service_login_and_rate_errors_are_cloud_failures(self):
        for status in (401, 403, 408, 409, 425, 429, 500, 502, 503, 529):
            with self.subTest(status=status):
                self.assertEqual('cloud_failure', status_failure_class(status, {}))

    def test_context_exhaustion_is_an_agent_failure(self):
        for status, error in ((400, {'type': 'invalid_request_error', 'message': 'prompt is too long: 210000 tokens'}),
                              (400, {'code': 'context_length_exceeded', 'message': 'Your input exceeds the window'}),
                              (413, {'type': 'request_too_large', 'message': 'Request exceeds the maximum size'})):
            with self.subTest(status=status, error=error):
                self.assertEqual('agent_failure', status_failure_class(status, error))

    def test_other_rejected_requests_are_harness_defects(self):
        for status, error in ((400, {'type': 'invalid_request_error', 'message': 'Unsupported parameter: store'}),
                              (400, {}), (404, {'type': 'not_found_error', 'message': 'model: claude-x'}),
                              (422, {'message': 'tools.0.name: invalid'})):
            with self.subTest(status=status, error=error):
                self.assertEqual('harness_defect', status_failure_class(status, error))


class NetworkEvidenceTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.archive = Path(temporary.name)
        environment = patch.dict(os.environ, MOTIF_INFERENCE_ANTHROPIC_KEY='SENTINEL-KEY')
        environment.start()
        self.addCleanup(environment.stop)
        channel = patch.object(Broker, 'start_channel')
        channel.start()
        self.addCleanup(channel.stop)
        self.broker = Broker('claude', 'claude-haiku-5-5', self.archive)

    def frame(self, path='/v1/messages', model='claude-haiku-5-5'):
        body = json.dumps({'model': model, 'messages': []}).encode()
        return {'method': 'POST', 'path': path, 'headers': {}, 'body': base64.b64encode(body).decode()}

    def rows(self):
        return [json.loads(line) for line in (self.archive / 'network.jsonl').read_text().splitlines()]

    def test_http_errors_record_status_class_and_credential_free_provider_error(self):
        responses = [urllib.error.HTTPError('https://api.anthropic.com', 400, 'Bad', {}, io.BytesIO(
                         error_body('invalid_request_error', 'prompt is too long; key SENTINEL-KEY'))),
                     urllib.error.HTTPError('https://api.anthropic.com', 529, 'Overloaded', {}, io.BytesIO(
                         error_body('overloaded_error', 'Overloaded')))]
        with patch.object(self.broker.opener, 'open', side_effect=responses):
            self.assertEqual(400, self.broker.forward(self.frame())[0])
            self.assertEqual(529, self.broker.forward(self.frame())[0])
        self.assertEqual(['agent_failure', 'cloud_failure'], [e['failureClass'] for e in self.broker.failure_events])
        self.assertIn('prompt is too long', self.broker.failure_events[0]['evidence'])
        rows = self.rows()
        self.assertEqual([400, 529], [row['status'] for row in rows])
        self.assertEqual('overloaded_error', rows[1]['providerError']['type'])
        self.assertNotIn('SENTINEL-KEY', (self.archive / 'network.jsonl').read_text())
        self.assertNotIn('SENTINEL-KEY', json.dumps(self.broker.failure_events))

    def test_refused_and_failed_requests_leave_evidence(self):
        with patch.object(self.broker.opener, 'open', side_effect=urllib.error.URLError('down')) as opened:
            self.assertEqual(403, self.broker.forward(self.frame(path='/v1/models'))[0])
            self.assertEqual(403, self.broker.forward(self.frame(model='claude-opus-5-5'))[0])
            with self.assertRaises(CloudFailure):
                self.broker.forward(self.frame())
        self.assertEqual(1, opened.call_count)
        rows = self.rows()
        self.assertEqual(['refused', 'refused', 'transport_failure'], [row['outcome'] for row in rows])
        self.assertEqual([None, None, 'https://api.anthropic.com/v1/messages'], [row['destination'] for row in rows])
        self.assertEqual('/v1/models', rows[0]['requestedPath'])
        self.assertTrue(all(row['requestHash'] for row in rows))

    def test_an_error_followed_by_a_success_is_marked_recovered(self):
        responses = [urllib.error.HTTPError('https://api.anthropic.com', 429, 'Slow down', {}, io.BytesIO(b'{}')),
                     Response(), urllib.error.HTTPError('https://api.anthropic.com', 500, 'Down', {}, io.BytesIO(b'{}'))]
        with patch.object(self.broker.opener, 'open', side_effect=responses):
            for _ in responses:
                self.broker.forward(self.frame())
        self.assertEqual([True, False], [event['recovered'] for event in self.broker.episode_failures()])


class EpisodeClassTests(unittest.TestCase):
    def replay(self, exit_code, events):
        sample = {'exitCode': exit_code, 'failure': None if exit_code == 0 else 'host stopped', 'finalMessage': 'done',
                  'bridgeErrors': []}
        replayer = test_host_failures.HostFailureTests()
        return replayer.replay(sample, inference_errors=events)

    def test_a_completed_host_that_retried_past_a_service_error_stays_scored(self):
        event = {'failureClass': 'cloud_failure', 'evidence': 'Inference upstream returned HTTP 429', 'status': 429,
                 'request': 1, 'recovered': True}
        result = self.replay(0, [event])
        self.assertEqual('clean', result['state'])
        self.assertIsNone(result['failureClass'])
        self.assertEqual([{'class': 'cloud_failure', 'evidence': event['evidence']}], result['recoveredInferenceErrors'])

    def test_unrecovered_or_fatal_errors_decide_the_episode_class(self):
        cloud = {'failureClass': 'cloud_failure', 'evidence': 'HTTP 503', 'status': 503, 'request': 4, 'recovered': False}
        context = {'failureClass': 'agent_failure', 'evidence': 'HTTP 400 prompt is too long', 'status': 400,
                   'request': 2, 'recovered': True}
        harness = {'failureClass': 'harness_defect', 'evidence': 'Provider response exceeded the transport limit',
                   'request': 1, 'recovered': True}
        self.assertEqual('cloud_failure', self.replay(0, [cloud])['failureClass'])
        self.assertEqual('agent_failure', self.replay(1, [context])['failureClass'])
        self.assertEqual('harness_defect', self.replay(0, [harness])['failureClass'])
        self.assertEqual('agent_failure', self.replay(1, [dict(cloud, recovered=True)])['failureClass'])

    def test_a_recovered_error_cannot_turn_an_agent_failure_into_a_retried_cloud_failure(self):
        event = {'failureClass': 'cloud_failure', 'evidence': 'Inference upstream returned HTTP 429', 'status': 429,
                 'request': 1, 'recovered': True}
        result = self.replay(1, [event])
        self.assertEqual('agent_failure', result['failureClass'])
        self.assertEqual(1, len(result['recoveredInferenceErrors']))


if __name__ == '__main__':
    unittest.main()
