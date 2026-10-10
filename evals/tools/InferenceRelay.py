"""A trial-local HTTP facade over the bounded inference pipe channel."""
import base64
from http.server import BaseHTTPRequestHandler, HTTPServer
import json
import os
import subprocess
import sys
import threading

LIMIT = 16 * 1024 * 1024
incoming = os.fdopen(4, "rb", closefd=False)
outgoing = os.fdopen(3, "wb", closefd=False)


class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        size = int(self.headers.get("Content-Length", "0"))
        if size <= 0 or size > LIMIT:
            self.send_error(413)
            return
        body = self.rfile.read(size)
        frame = {"method": "POST", "path": self.path, "headers": dict(self.headers), "body": base64.b64encode(body).decode()}
        outgoing.write(json.dumps(frame).encode() + b"\n")
        outgoing.flush()
        line = incoming.readline(LIMIT * 2)
        if not line or len(line) >= LIMIT * 2:
            self.send_error(502)
            return
        result = json.loads(line)
        content = base64.b64decode(result["body"], validate=True)
        self.send_response(result["status"])
        for key, value in result["headers"].items():
            self.send_header(key, value)
        self.send_header("Content-Length", str(len(content)))
        self.end_headers()
        self.wfile.write(content)

    def log_message(self, *args):
        pass


server = HTTPServer(("127.0.0.1", 8181), Handler)
threading.Thread(target=server.serve_forever, daemon=True).start()
try:
    result = subprocess.run(sys.argv[1:], stdin=subprocess.DEVNULL, close_fds=True)
finally:
    server.shutdown()
    incoming.close()
    outgoing.close()
sys.exit(result.returncode)
