import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
from TrialBoundary import boundary, client_registration, environment, external_transcript, hashes, monitor, run
from AgentBoundary import agent_boundary
from InferenceBroker import Broker


class BoundaryTests(unittest.TestCase):
    def server_trace(self, root, files):
        parent = Path(root) / 'syscalls.100'
        parent.write_text('execve("/usr/bin/bwrap", ["bwrap", "--chdir", "/session/output"], []) = 0\n'
                          'clone(flags=SIGCHLD) = 1\n')
        return {'auditFiles': [str(parent)] + [str(path) for path in files]}

    def test_agent_mounts_cannot_expose_project_manifest_or_developer_home(self):
        with patch('AgentBoundary.Path.exists', return_value=True):
            args = agent_boundary({'environment': {'HOME': '/workspace/home', 'USERPROFILE': '/workspace/home', 'CODEX_HOME': '/workspace/config/codex'}}, ['/opt/host/client'])
        mounts = [args[index + 1] for index, value in enumerate(args) if value in ('--ro-bind', '--bind')]
        self.assertNotIn('/session', mounts)
        self.assertNotIn('/session/project', mounts)
        self.assertNotIn('/session/state', mounts)
        self.assertNotIn('/input', mounts)
        self.assertNotIn('/opt/product', mounts)
        self.assertNotIn('/home', mounts)
        writable = [args[index + 1] for index, value in enumerate(args) if value == '--bind']
        self.assertEqual(['/session/agent'], writable)
        self.assertIn('--clearenv', args)

    def test_missing_or_wrong_client_tool_list_is_a_harness_failure(self):
        with tempfile.TemporaryDirectory() as root:
            session = Path(root)
            output = session / 'output'
            output.mkdir()
            with self.assertRaises(RuntimeError):
                client_registration(session, ['motif_read'], 'codex')
            (output / 'bridge-status.json').write_text('{"errors":[]}')
            (output / 'client-tools.json').write_text('[{"name":"other"}]')
            with self.assertRaises(RuntimeError):
                client_registration(session, ['motif_read'], 'codex')
            (output / 'client-tools.json').write_text('[{"name":"motif_read"}]')
            with self.assertRaises(RuntimeError):
                client_registration(session, ['motif_read'], 'codex')
            (output / 'mcp-registration.json').write_text('{"exitCode":0}')
            self.assertTrue(client_registration(session, ['motif_read'], 'codex')['clientToolsList'])
            (output / 'bridge-status.json').write_text('{"errors":["connection lost"]}')
            with self.assertRaises(RuntimeError):
                client_registration(session, ['motif_read'], 'codex')

    def test_absent_auditor_is_unscored_failure(self):
        with tempfile.TemporaryDirectory() as root, patch("TrialBoundary.shutil.which", return_value=None):
            result = run({"archive": root, "host": "fake"})
            self.assertEqual("isolation_failure", result["state"])
            self.assertIn("dependency", result["reasons"][0]["reason"])
            self.assertTrue((Path(root) / "integrity.json").exists())

    def test_lost_external_audit_prevents_launch(self):
        with tempfile.TemporaryDirectory() as root, patch("TrialBoundary.shutil.which", return_value="available"), patch("TrialBoundary.audited_run", return_value={"exitCode": 0, "auditComplete": False}) as execution:
            result = run({"archive": root, "host": "fake", "protected": [], "session": str(Path(root) / "session"), "mounts": []})
            self.assertEqual("isolation_failure", result["state"])
            self.assertEqual(1, execution.call_count)

    def test_store_access_follows_server_and_agent_threads(self):
        with tempfile.TemporaryDirectory() as root:
            parent = Path(root) / "syscalls.1"
            child = Path(root) / "syscalls.2"
            thread = Path(root) / "syscalls.3"
            parent.write_text('execve("/opt/powershell/pwsh", [], []) = 0\nclone(flags=SIGCHLD) = 2\n')
            child.write_text('execve("/opt/product/motif", [], []) = 0\nclone(flags=CLONE_THREAD) = 3\n')
            thread.write_text('openat(AT_FDCWD, "/session/project/project.motif.db", O_RDWR) = 7\n')
            result = self.server_trace(root, [parent, child, thread])
            self.assertEqual("clean", monitor(result, [], [], [], "nonce")["state"])
            parent.write_text('execve("/usr/bin/bwrap", ["bwrap", "--chdir", "/workspace/work"], []) = 0\nclone(flags=SIGCHLD) = 2\n')
            self.assertEqual("invalid", monitor(result, [], [], [], "nonce")["state"])

    def test_environment_drops_control_secrets_and_paths(self):
        with patch.dict(os.environ, {"OPENAI_API_KEY": "private", "MOTIF_TEST_GRAMMARS": "/private/sets", "SSH_AUTH_SOCK": "/socket"}):
            result = environment()
            self.assertNotIn("OPENAI_API_KEY", result)
            self.assertNotIn("MOTIF_TEST_GRAMMARS", result)
            self.assertNotIn("SSH_AUTH_SOCK", result)
            self.assertEqual("/session/state/choice.json", result["MOTIF_ADVANCED_AI_MODE_PATH"])

    def test_mounts_are_explicit_and_namespaces_private(self):
        with tempfile.TemporaryDirectory() as root:
            args = boundary({"mounts": [["/temporary/product", "/opt/product"]], "session": str(Path(root) / 'session')}, ["/opt/product/motif"])
            identity = Path(root) / 'system/server/passwd'
            self.assertEqual(1, len(identity.read_text().splitlines()))
            self.assertIn('Workspace:/session/state/home:', identity.read_text())
            self.assertIn('--ro-bind', args)
        self.assertIn("--unshare-all", args)
        self.assertIn("--clearenv", args)
        self.assertIn("--remount-ro", args)
        self.assertNotIn("--share-net", args)
        self.assertNotIn("/home", args)
        self.assertNotIn("/", [args[index + 1] for index, value in enumerate(args) if value == "--ro-bind"])

    def test_successful_protected_read_is_invalid_and_denial_needs_review(self):
        with tempfile.TemporaryDirectory() as root:
            audit = Path(root) / "events"
            for status, state in [("3", "invalid"), ("-1 ENOENT (No such file)", "review")]:
                audit.write_text('openat(AT_FDCWD, "/private/data", O_RDONLY) = ' + status + "\n")
                result = monitor({"auditFiles": [str(audit)]}, ["/private"], [], [], "nonce")
                self.assertEqual(state, result["state"])
                self.assertEqual(state == "review", result["reasons"][0]["denied"])

    def test_agent_project_read_is_review_but_server_reads_are_allowed(self):
        with tempfile.TemporaryDirectory() as root:
            audit = Path(root) / 'syscalls.1'
            audit.write_text('execve("/usr/bin/python3", [], []) = 0\nopenat(AT_FDCWD, "/session/project/project.fwdata", O_RDONLY) = -1 ENOENT\n')
            self.assertEqual('review', monitor({'auditFiles': [str(audit)]}, [], [], [], 'nonce')['state'])
            audit.write_text('execve("/opt/product/motif", [], []) = 0\nopenat(AT_FDCWD, "/session/project/project.fwdata", O_RDONLY) = 7\n')
            self.assertEqual('clean', monitor(self.server_trace(root, [audit]), [], [], [], 'nonce')['state'])

    def test_runtime_state_lookups_are_clean_but_agent_state_probe_is_review(self):
        with tempfile.TemporaryDirectory() as root:
            audit = Path(root) / 'syscalls.1'
            lookups = ('newfstatat(AT_FDCWD, "/session/state/home/.local/lib/python3.14/site-packages", 0x1, 0) = -1 ENOENT\n'
                       'stat("/session/state/data/powershell/Modules", 0x1) = -1 ENOENT\n')
            audit.write_text('execve("/usr/bin/python3", [], []) = 0\ngetcwd("/session/output", 1024) = 16\n' + lookups)
            self.assertEqual('clean', monitor(self.server_trace(root, [audit]), [], [], [], 'nonce')['state'])
            audit.write_text(lookups)
            self.assertEqual('clean', monitor(self.server_trace(root, [audit]), [], [], [], 'nonce')['state'])
            audit.write_text('execve("/usr/bin/python3", [], []) = 0\ngetcwd("/workspace/work", 1024) = 16\n' + lookups)
            self.assertEqual('review', monitor({'auditFiles': [str(audit)]}, [], [], [], 'nonce')['state'])
            audit.write_text('execve("/usr/bin/python3", [], []) = 0\ngetcwd("/workspace/work", 1024) = 16\n'
                             'openat(AT_FDCWD, "/session/project/project.fwdata", O_RDONLY) = 7\n')
            self.assertEqual('invalid', monitor({'auditFiles': [str(audit)]}, [], [], [], 'nonce')['state'])

    def test_exec_argument_path_is_not_access_but_actual_protected_access_is_classified(self):
        with tempfile.TemporaryDirectory() as root:
            audit = Path(root) / 'syscalls.1'
            execution = 'execve("/usr/bin/python3", ["python3", "-c", "open(\'/private/gold\').read()"], []) = 0\n'
            audit.write_text(execution)
            self.assertEqual('clean', monitor({'auditFiles': [str(audit)]}, ['/private'], [], [], 'nonce')['state'])
            for status, expected in [('-1 ENOENT', 'review'), ('3', 'invalid')]:
                audit.write_text(execution + 'openat(AT_FDCWD, "/private/gold", O_RDONLY) = ' + status + '\n')
                result = monitor({'auditFiles': [str(audit)]}, ['/private'], [], [], 'nonce')
                self.assertEqual(expected, result['state'])
                self.assertEqual(1, len(result['reasons']))
            audit.write_text('execve("/private/tool", [], []) = 0\n')
            self.assertEqual('invalid', monitor({'auditFiles': [str(audit)]}, ['/private'], [], [], 'nonce')['state'])

    def test_bubblewrap_setup_is_not_agent_access_and_failed_metadata_does_not_escalate(self):
        with tempfile.TemporaryDirectory() as root:
            parent, child = Path(root) / 'syscalls.1', Path(root) / 'syscalls.2'
            parent.write_text('execve("/usr/bin/bwrap", ["bwrap", "--chdir", "/workspace/work"], []) = 0\nclone(flags=SIGCHLD) = 2\n')
            child.write_text('openat(AT_FDCWD, "/private/input", O_RDONLY) = 7\nexecve("/opt/powershell/pwsh", [], []) = 0\n')
            result = {'auditFiles': [str(parent), str(child)]}
            self.assertEqual('clean', monitor(result, ['/private'], [], [], 'nonce')['state'])
            child.write_text('execve("/usr/bin/python3", [], []) = 0\nreadlink("/private/data", 0x1, 1024) = -1 EINVAL\n')
            self.assertEqual('review', monitor(result, ['/private'], [], [], 'nonce')['state'])

    def test_awareness_is_clean_but_hidden_tool_is_invalid(self):
        with tempfile.TemporaryDirectory() as root:
            result = {"auditFiles": []}
            transcript = [{"source": "host-summary", "payload": {"finalMessage": "This looks like a test."}}]
            self.assertEqual("clean", monitor(result, [], [], transcript, "nonce")["state"])
            transcript.append({"source": "mcp-client", "direction": "sent", "payload": {"method": "tools/call", "params": {"name": "motif_apply"}}})
            self.assertEqual("invalid", monitor(result, [], [], transcript, "nonce")["state"])

    def test_handoff_refuses_links_and_special_files(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "link"
            path.symlink_to("/private")
            with self.assertRaises(ValueError):
                hashes(root)
            path.unlink()
            os.mkfifo(path)
            with self.assertRaises(ValueError):
                hashes(root)

    def test_external_transcript_preserves_calls_and_requires_close(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "host.stdout.log"
            path.write_text(json.dumps({"source": "host-output", "payload": json.dumps({"source": "mcp-client", "direction": "sent", "payload": {"method": "tools/call"}})}) + "\n")
            with self.assertRaises(RuntimeError):
                external_transcript(root)
            with path.open("a") as output:
                output.write(json.dumps({"source": "host-summary", "payload": {}}) + "\n")
            self.assertEqual("mcp-client", external_transcript(root)[0]["source"])

    def test_broker_refuses_other_routes_and_models(self):
        broker = Broker.__new__(Broker)
        broker.count = 0
        broker.paths = {"/v1/responses"}
        broker.denials = []
        broker.model = "declared-model"
        import base64
        frame = {"method": "POST", "path": "https://elsewhere.invalid/", "body": base64.b64encode(b'{}').decode()}
        self.assertEqual(403, broker.forward(frame)[0])
        frame.update(path="/v1/responses", body=base64.b64encode(b'{"model":"other"}').decode())
        self.assertEqual(403, broker.forward(frame)[0])
        self.assertEqual(2, len(broker.denials))


if __name__ == "__main__":
    unittest.main()
