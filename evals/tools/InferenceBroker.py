"""A control-side, fixed-destination inference broker. No credential crosses the pipes."""
import base64
import binascii
import hashlib
import http.client
import json
import math
import os
from pathlib import Path
import threading
import time
import urllib.error
import urllib.request
import uuid

LIMIT = 16 * 1024 * 1024
PROVIDERS = {
    "codex": ("https://api.openai.com", "MOTIF_INFERENCE_OPENAI_KEY", {"/v1/responses", "/v1/responses/compact"}),
    "codex-chatgpt": ("https://chatgpt.com/backend-api/codex", None, {"/v1/responses"}),
    "claude": ("https://api.anthropic.com", "MOTIF_INFERENCE_ANTHROPIC_KEY", {"/v1/messages", "/v1/messages?beta=true"}),
}


class InferenceFailure(RuntimeError):
    """A control-side inference failure with a credential-free diagnostic."""


class CloudFailure(InferenceFailure):
    """A provider login or model-service failure that may be retried."""


LOGIN_FAILURE = "ChatGPT login is missing, expired or unusable; run codex on the host to refresh the login"


def inference_upstream(host, auth_mode=None):
    if host == "fake":
        return None, "none"
    if host == "claude":
        if auth_mode not in (None, "api-key", "claude-plan"):
            raise InferenceFailure("Unsupported inference auth mode for Claude")
        return host, auth_mode or "api-key"
    if host != "codex":
        raise InferenceFailure("Unsupported inference host")
    if auth_mode is None:
        selection = os.environ.get("MOTIF_INFERENCE_CODEX_AUTH") or "api-key"
        if selection not in ("api-key", "chatgpt"):
            raise InferenceFailure("MOTIF_INFERENCE_CODEX_AUTH must be api-key or chatgpt")
        auth_mode = "chatgpt-plan" if selection == "chatgpt" else selection
    if auth_mode not in ("api-key", "chatgpt-plan"):
        raise InferenceFailure("MOTIF_INFERENCE_CODEX_AUTH must be api-key or chatgpt")
    return ("codex-chatgpt" if auth_mode == "chatgpt-plan" else "codex"), auth_mode


def chatgpt_credentials():
    try:
        auth_home = Path(os.environ["CODEX_HOME"]) if os.environ.get("CODEX_HOME") else Path.home() / ".codex"
        tokens = json.loads((auth_home / "auth.json").read_text(encoding="utf-8"))["tokens"]
        token, account = tokens["access_token"], tokens["account_id"]
        if not all(isinstance(value, str) and value and value.isascii() and value.isprintable()
                   and not any(c.isspace() for c in value)
                   for value in (token, account)):
            raise ValueError()
        parts = token.split(".")
        if len(parts) != 3:
            raise ValueError()
        claims = json.loads(base64.b64decode(parts[1] + "=" * (-len(parts[1]) % 4), altchars=b"-_", validate=True))
        expiry = claims["exp"]
        if isinstance(expiry, bool) or not isinstance(expiry, (int, float)) or not math.isfinite(expiry) or not expiry > time.time():
            raise ValueError()
        return token, account
    except (OSError, ValueError, KeyError, TypeError, OverflowError, binascii.Error):
        raise CloudFailure(LOGIN_FAILURE) from None


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class Broker:
    def __init__(self, host, model, archive, auth_mode=None):
        self.upstream, self.auth_mode = inference_upstream(host, auth_mode)
        self.origin, variable, self.paths = PROVIDERS[self.upstream]
        if self.upstream == "codex-chatgpt":
            chatgpt_credentials()
        elif host == "claude" and self.auth_mode == "claude-plan":
            self.key = None
        else:
            self.key = os.environ.get(variable)
            if not self.key:
                raise CloudFailure("Live inference requires the control-only " + variable)
        self.host = host
        self.model = model
        self.archive = Path(archive)
        self.errors = []
        self.failures = []
        self.failure_events = []
        self.denials = []
        self.count = 0
        self.session_id = str(uuid.uuid4())
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
        self.start_channel()

    def start_channel(self):
        request_read, request_write = os.pipe()
        response_read, response_write = os.pipe()
        self.request_read = os.dup(request_read)
        self.response_write = os.dup(response_write)
        for descriptor in (request_read, request_write, response_read, response_write):
            if descriptor not in (request_write, response_read):
                os.close(descriptor)
        os.dup2(request_write, 3)
        os.dup2(response_read, 4)
        for descriptor in (request_write, response_read):
            if descriptor not in (3, 4):
                os.close(descriptor)
        self.thread = threading.Thread(target=self.serve, daemon=True)
        self.thread.start()

    def started(self):
        os.close(3)
        os.close(4)

    def forward(self, frame):
        self.count += 1
        body = base64.b64decode(frame.get("body", ""), validate=True)
        path = frame.get("path")
        method = frame.get("method")
        if path not in self.paths or method != "POST" or len(body) > LIMIT or self.count > 1000:
            self.denials.append("inference route or request limit refused")
            return 403, {}, b'{"error":{"message":"Route refused by the model service"}}'
        document = json.loads(body)
        if document.get("model") != self.model:
            self.denials.append("unconfigured model refused")
            return 403, {}, b'{"error":{"message":"Model refused by the model service"}}'
        headers = {"Content-Type": "application/json"}
        destination = self.origin + path
        if self.upstream == "codex-chatgpt":
            token, account = chatgpt_credentials()
            secrets = (token, account)
            headers.update({"Authorization": "Bearer " + token, "chatgpt-account-id": account,
                            "OpenAI-Beta": "responses=experimental", "originator": "codex_cli_rs",
                            "session_id": self.session_id})
            document.update(stream=True, store=False)
            body = json.dumps(document).encode()
            destination = self.origin + "/responses"
        elif self.host == "codex":
            secrets = (self.key,)
            headers["Authorization"] = "Bearer " + self.key
        elif self.auth_mode == "claude-plan":
            incoming = {key.lower(): value for key, value in frame.get("headers", {}).items()}
            authorization = incoming.get("authorization", "")
            if not authorization.startswith("Bearer ") or not authorization[7:].strip():
                raise CloudFailure("Claude subscription login is missing or unusable")
            token = authorization[7:]
            secrets = (token,)
            headers["Authorization"] = authorization
            headers["anthropic-version"] = incoming.get("anthropic-version", "2023-06-01")
            if "anthropic-beta" in incoming:
                headers["anthropic-beta"] = incoming["anthropic-beta"]
        else:
            secrets = (self.key,)
            headers["x-api-key"] = self.key
            incoming = {key.lower(): value for key, value in frame.get("headers", {}).items()}
            headers["anthropic-version"] = incoming.get("anthropic-version", "2023-06-01")
            if "anthropic-beta" in incoming:
                headers["anthropic-beta"] = incoming["anthropic-beta"]
        if len(body) > LIMIT:
            raise InferenceFailure("Provider request exceeded the transport limit")
        request = urllib.request.Request(destination, body, headers, method="POST")
        try:
            response = self.opener.open(request, timeout=180)
        except urllib.error.HTTPError as error:
            response = error
        except (OSError, urllib.error.URLError):
            raise CloudFailure("Inference upstream request failed") from None
        try:
            with response:
                content = response.read(LIMIT + 1)
                if len(content) > LIMIT:
                    raise InferenceFailure("Provider response exceeded the transport limit")
                metadata = {"Content-Type": response.headers.get("Content-Type", "application/json")}
                status = response.code
        except (OSError, http.client.HTTPException):
            raise CloudFailure("Inference upstream response failed") from None
        for secret in secrets:
            content = content.replace(secret.encode(), b"[redacted]")
            metadata["Content-Type"] = metadata["Content-Type"].replace(secret, "[redacted]")
        with (self.archive / "network.jsonl").open("a") as log:
            log.write(json.dumps({"at": time.time(), "destination": destination, "status": status, "authMode": self.auth_mode,
                                  "requestHash": hashlib.sha256(body).hexdigest()}) + "\n")
        if self.upstream == "codex-chatgpt" and status == 401:
            raise CloudFailure(LOGIN_FAILURE)
        if status >= 400:
            evidence = "Inference upstream returned HTTP " + str(status)
            self.failures.append(evidence)
            self.failure_events.append({"failureClass": "cloud_failure", "evidence": evidence})
        return status, metadata, content

    def serve(self):
        try:
            with os.fdopen(self.request_read, "rb") as incoming, os.fdopen(self.response_write, "wb") as outgoing:
                while True:
                    line = incoming.readline(LIMIT * 2)
                    if not line:
                        break
                    if len(line) >= LIMIT * 2 or not line.endswith(b"\n"):
                        raise RuntimeError("Malformed inference channel frame")
                    try:
                        status, headers, body = self.forward(json.loads(line))
                    except InferenceFailure as error:
                        evidence = str(error)
                        self.failures.append(evidence)
                        failure_class = "cloud_failure" if isinstance(error, CloudFailure) else "harness_defect"
                        self.failure_events.append({"failureClass": failure_class, "evidence": evidence})
                        status, headers = 503, {"Content-Type": "application/json"}
                        body = json.dumps({"error": {"message": evidence}}).encode()
                    outgoing.write(json.dumps({"status": status, "headers": headers, "body": base64.b64encode(body).decode()}).encode() + b"\n")
                    outgoing.flush()
        except Exception as error:
            self.errors.append(type(error).__name__)

    def closed(self):
        self.thread.join(timeout=190)
        return not self.thread.is_alive() and not self.errors
