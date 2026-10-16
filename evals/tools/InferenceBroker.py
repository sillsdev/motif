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
SERVICE_STATUSES = {401, 403, 408, 409, 425, 429}
# "prompt is too long" is Anthropic's whole context-overflow message; every other marker names the context.
CONTEXT_EVIDENCE = ("context_length_exceeded", "context length", "context window", "maximum context",
                    "prompt is too long")


def provider_error(content, secrets):
    """The provider's own error type, code and message from an error body, short and credential-free."""
    try:
        error = json.loads(content).get("error")
    except (ValueError, AttributeError):
        error = None
    if isinstance(error, str):
        error = {"message": error}
    if not isinstance(error, dict):
        return {}
    found = {key: str(error[key])[:200] for key in ("type", "code", "message") if error.get(key) is not None}
    for secret in secrets:
        found = {key: value.replace(secret, "[redacted]") for key, value in found.items()}
    return found


def status_failure_class(status, error):
    """Policy for an upstream HTTP error: the service's fault, the Agent's context, or the harness's request."""
    if status in SERVICE_STATUSES or status >= 500:
        return "cloud_failure"
    text = " ".join(error.values()).lower()
    if status in (400, 413, 422) and any(marker in text for marker in CONTEXT_EVIDENCE):
        return "agent_failure"
    return "harness_defect"


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


def response_usage(content):
    """Tokens one model response used: input with cached input counted once, plus output, as each provider reports it."""
    documents = []
    for line in content.splitlines():
        if line.startswith(b"data:"):
            documents.append(line[5:].strip())
    if not documents:
        documents = [content]
    input_tokens = output_tokens = 0

    def visit(node):
        nonlocal input_tokens, output_tokens
        if isinstance(node, dict):
            usage = node.get("usage")
            if isinstance(usage, dict):
                # Anthropic reports cached input apart from input_tokens; OpenAI's input_tokens already includes it.
                total = sum(usage.get(name) or 0 for name in
                            ("input_tokens", "cache_read_input_tokens", "cache_creation_input_tokens")
                            if isinstance(usage.get(name), int))
                input_tokens = max(input_tokens, total)
                if isinstance(usage.get("output_tokens"), int):
                    output_tokens = max(output_tokens, usage["output_tokens"])
            for value in node.values():
                visit(value)
        elif isinstance(node, list):
            for value in node:
                visit(value)

    for document in documents:
        try:
            visit(json.loads(document))
        except ValueError:
            continue
    return input_tokens + output_tokens


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class Broker:
    def __init__(self, host, model, archive, auth_mode=None, budget=None):
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
        self.last_success = 0
        self.budget = budget
        self.spent = {"turns": 0, "tokens": 0}
        self.budget_exhausted = None
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
            self.record(None, body, outcome="refused", reason="route or request limit", requestedPath=str(path)[:200])
            return 403, {}, b'{"error":{"message":"Route refused by the model service"}}'
        document = json.loads(body)
        if document.get("model") != self.model:
            self.denials.append("unconfigured model refused")
            self.record(None, body, outcome="refused", reason="unconfigured model", requestedPath=path)
            return 403, {}, b'{"error":{"message":"Model refused by the model service"}}'
        exhausted = self.exhausted_budget()
        if exhausted:
            if not self.budget_exhausted:
                self.budget_exhausted = exhausted
                self.failure_events.append({"failureClass": "agent_failure", "evidence": exhausted, "request": self.count})
            self.record(None, body, outcome="refused", reason="agent budget")
            return 400, {"Content-Type": "application/json"}, json.dumps({"error": {"message": exhausted}}).encode()
        headers = {"Content-Type": "application/json"}
        destination = self.origin + path
        if self.upstream == "codex-chatgpt":
            try:
                token, account = chatgpt_credentials()
            except CloudFailure:
                self.record(None, body, outcome="login_unusable")
                raise
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
                self.record(None, body, outcome="login_unusable")
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
            self.record(destination, body, outcome="transport_failure")
            raise CloudFailure("Inference upstream request failed") from None
        try:
            with response:
                content = response.read(LIMIT + 1)
                if len(content) > LIMIT:
                    raise InferenceFailure("Provider response exceeded the transport limit")
                metadata = {"Content-Type": response.headers.get("Content-Type", "application/json")}
                status = response.code
        except (OSError, http.client.HTTPException):
            self.record(destination, body, outcome="response_failure")
            raise CloudFailure("Inference upstream response failed") from None
        self.spent["turns"] += 1
        self.spent["tokens"] += response_usage(content)
        for secret in secrets:
            content = content.replace(secret.encode(), b"[redacted]")
            metadata["Content-Type"] = metadata["Content-Type"].replace(secret, "[redacted]")
        error = provider_error(content, secrets) if status >= 400 else {}
        self.record(destination, body, status=status, **({"providerError": error} if error else {}))
        if self.upstream == "codex-chatgpt" and status == 401:
            raise CloudFailure(LOGIN_FAILURE)
        if status >= 400:
            evidence = "Inference upstream returned HTTP " + str(status)
            if error:
                evidence += ": " + " ".join(error[key] for key in ("type", "code", "message") if key in error)
            self.failures.append(evidence)
            self.failure_events.append({"failureClass": status_failure_class(status, error), "evidence": evidence,
                                        "status": status, "request": self.count})
        else:
            self.last_success = self.count
        return status, metadata, content

    def exhausted_budget(self):
        if not self.budget:
            return None
        if self.spent["turns"] >= self.budget["turns"]:
            return "The Agent budget of %d turns was exhausted." % self.budget["turns"]
        if self.spent["tokens"] >= self.budget["tokens"]:
            return "The Agent budget of %d tokens was exhausted." % self.budget["tokens"]
        return None

    def record(self, destination, body, status=None, **details):
        row = {"at": time.time(), "destination": destination, "status": status, "authMode": self.auth_mode,
               "requestHash": hashlib.sha256(body).hexdigest()}
        row.update(details)
        with (self.archive / "network.jsonl").open("a") as log:
            log.write(json.dumps(row) + "\n")

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
                        self.failure_events.append({"failureClass": failure_class, "evidence": evidence,
                                                    "request": self.count})
                        status, headers = 503, {"Content-Type": "application/json"}
                        body = json.dumps({"error": {"message": evidence}}).encode()
                    outgoing.write(json.dumps({"status": status, "headers": headers, "body": base64.b64encode(body).decode()}).encode() + b"\n")
                    outgoing.flush()
        except Exception as error:
            self.errors.append(type(error).__name__)

    def episode_failures(self):
        """Failure events, each marked recovered when the service answered a later request successfully."""
        return [dict(event, recovered=event["request"] < self.last_success) for event in self.failure_events]

    def closed(self):
        self.thread.join(timeout=190)
        return not self.thread.is_alive() and not self.errors
