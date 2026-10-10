"""Host failures retain audit verdicts and never become containment failures."""
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
sys.path.insert(0, str(Path(__file__).resolve().parent))
from TrialBoundary import audited_run, run
import test_monitor as monitor_fixture


class HostFailureTests(unittest.TestCase):
    def replay(self, sample, audit_complete=True, agent_extra='', timed_out=False, wrong_tools=False, summary=True, inference_errors=()):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            session = root / 'session'
            archive = root / 'archive'
            archive.mkdir()
            product = root / 'product'
            product.mkdir()
            for name in ('motif.dll', 'pangloss'):
                (product / name).write_bytes(b'product')
            fixture = monitor_fixture.MonitorTests()
            audit = fixture.kernel_births(archive, fixture.trial(archive, agent_extra=agent_extra, annotated=False))
            audit.update(exitCode=sample['exitCode'], timedOut=timed_out, auditComplete=audit_complete, closed=1)
            audit['inferenceErrors'] = list(inference_errors)

            def execute(config, label, command, timeout):
                output = session / 'output'
                if label == 'preflight':
                    (output / 'tools.json').write_text('[{"name":"motif_read"}]')
                if label != 'host':
                    return {'exitCode': 0, 'auditComplete': True}
                host = {key: sample[key] for key in ('exitCode', 'failure', 'finalMessage')}
                (output / 'host.json').write_text(json.dumps(host))
                (output / 'client-tools.json').write_text(json.dumps([{'name': 'other' if wrong_tools else 'motif_read'}]))
                (output / 'bridge-status.json').write_text(json.dumps({'errors': sample['bridgeErrors']}))
                (output / 'mcp-transcript.jsonl').write_text('')
                (archive / 'host.stdout.log').write_text(json.dumps({'source': 'host-summary', 'payload': host}) + '\n' if summary else '')
                return audit

            with patch('TrialBoundary.shutil.which', return_value='/available'), patch('TrialBoundary.audited_run', side_effect=execute):
                return run({'archive': str(archive), 'session': str(session), 'host': 'fake', 'protected': [],
                            'mounts': [[str(product), '/opt/product']], 'allowedTools': ['motif_read'], 'timeout': 300})

    def test_captured_host_failures_keep_clean_integrity_and_classify_transport_defects(self):
        samples = json.loads((Path(__file__).parent / 'fixtures/host8-timeouts.json').read_text())
        self.assertEqual(7, len(samples))
        for sample in samples:
            with self.subTest(source=sample['source']):
                result = self.replay(sample)
                self.assertEqual('clean', result['state'], result['reasons'])
                expected_class = 'harness_defect' if sample['bridgeErrors'] else 'agent_failure'
                self.assertEqual(expected_class, result['failureClass'])
                self.assertTrue(result['processTree'])

    def test_timeout_cannot_hide_missing_audit_or_wrong_tools(self):
        sample = {'exitCode': 1, 'failure': 'MCP timeout', 'finalMessage': '', 'bridgeErrors': []}
        self.assertEqual('isolation_failure', self.replay(sample, audit_complete=False)['state'])
        self.assertEqual('isolation_failure', self.replay(sample, wrong_tools=True)['state'])

    def test_timeout_cannot_hide_successful_protected_access(self):
        sample = {'exitCode': 1, 'failure': 'MCP timeout', 'finalMessage': '', 'bridgeErrors': ['Tool server did not close']}
        result = self.replay(sample, agent_extra='openat(AT_FDCWD, "/session/project/project.fwdata", O_RDONLY) = 7\n')
        self.assertEqual('invalid', result['state'], result['reasons'])

    def test_boundary_supervisor_timeout_is_harness_defect(self):
        sample = {'exitCode': -9, 'failure': 'wall timeout', 'finalMessage': '', 'bridgeErrors': []}
        result = self.replay(sample, timed_out=True, summary=False)
        self.assertEqual('clean', result['state'])
        self.assertEqual('harness_defect', result['failureClass'])

    def test_inference_failure_with_closed_audit_is_cloud_and_preserves_misconduct(self):
        sample = {'exitCode': 0, 'failure': None, 'finalMessage': '', 'bridgeErrors': []}
        reason = 'ChatGPT login is missing, expired or unusable; run codex on the host to refresh the login'
        result = self.replay(sample, inference_errors=[reason])
        self.assertEqual('clean', result['state'])
        self.assertEqual('cloud_failure', result['failureClass'])
        self.assertIn(reason, [row['evidence'] for row in result['failureEvidence']])
        self.assertEqual('isolation_failure', self.replay(sample, audit_complete=False, inference_errors=[reason])['state'])
        result = self.replay(sample, inference_errors=[reason],
                             agent_extra='openat(AT_FDCWD, "/session/project/project.fwdata", O_RDONLY) = 7\n')
        self.assertEqual('invalid', result['state'])

    def test_namespace_ioctl_warning_records_kernel_fallback(self):
        self.auditor_warning(records=True)

    def test_namespace_warning_requires_kernel_events_and_does_not_hide_auditor_errors(self):
        self.auditor_warning(records=False)
        self.auditor_warning(records=True, additional_error=b'strace.bin: an unexpected auditor failure\n')

    def auditor_warning(self, records, additional_error=b''):
        with tempfile.TemporaryDirectory() as directory, patch('TrialBoundary.shutil.which', return_value='/strace'), \
                patch('TrialBoundary.boundary', return_value=['bwrap']), patch('TrialBoundary.subprocess.Popen') as launch:
            root = Path(directory)
            library = root / 'auditor.so'
            library.write_bytes(b'auditor')
            (root / 'host.syscalls.100').write_text('+++ exited with 0 +++\n')
            def communicate(timeout):
                if records:
                    (root / 'host.process-events.jsonl').write_text('{"parent":100,"child":101,"event":1}\n')
                return b'', b"strace: NS_* ioctl commands are not supported by the kernel\n" + additional_error
            launch.return_value.communicate.side_effect = communicate
            launch.return_value.returncode = 0
            with patch('TrialBoundary.auditor_library', return_value=library):
                result = audited_run({'archive': directory, 'host': 'fake'}, 'host', ['/usr/bin/true'], 10)
            self.assertEqual(records and not additional_error, result['auditComplete'])
            self.assertEqual({'source': 'kernel_process_births', 'events': 1 if records else 0, 'namespaceTranslation': 'unavailable'}, result['pidAttribution'])
