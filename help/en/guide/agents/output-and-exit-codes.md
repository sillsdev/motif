# Output and exit codes

Use the process exit code as the success signal. Successful text output goes to stdout; handled failures go to stderr. With `--json`, successful responses are usually the command’s JSON object itself, using camelCase fields, not a common success wrapper. Some enqueue-only commands return a bare job ID even with `--json`.

Handled failures requested with `--json` write a failure object to stderr and nothing to stdout. Its shape is:

```json
{
  "ok": false,
  "reason": "Busy",
  "message": "<human-readable reason>",
  "detail": { "project": "<project path>" }
}
```

`code` and `detail` are optional. The `reason` is a stable failure class; `message` is for a person. Exit codes are `1` for an invalid invocation, `2` for a refusal or missing item, `3` when the request cannot be attempted now (such as a busy project), and `4` for an inconsistent store or unexpected failure. Retry a `3` later; do not retry a refusal unchanged.

There is one current exception to the JSON failure shape: an unexpected exception escaping the top-level command handler prints plain `error: ...` to stderr and exits `4`, even when `--json` was supplied. Treat any nonzero exit as failure and keep stdout and stderr separate.
