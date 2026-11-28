# Output and exit codes

Use the process exit code as the success signal. Successful text output goes to stdout; handled failures go to stderr. With `--json`, successful responses are usually the command’s JSON object itself, using camelCase fields, not a common success wrapper. Some enqueue-only commands return a bare job ID even with `--json`.

Every write verb accepts `--json`. Handled failures requested with `--json` write a failure object to stderr and nothing to stdout. Its shape is:

```json
{
  "ok": false,
  "reason": "Busy",
  "message": "<human-readable reason>",
  "next": "Motif is busy. Wait a few seconds and repeat the same request.",
  "detail": { "project": "<project path>" }
}
```

`code` and `detail` are optional. The `reason` is a stable failure class; `message` is for a person. `next` is always present and says what to do instead, the same steering the MCP tools return in their `Next:` line, with any command written as a `motif` line. Exit codes are `1` for an invalid invocation, `2` for a refusal, missing item, or cancellation, `3` when the request cannot be attempted now (such as a busy project), `4` for an inconsistent store or unexpected failure, `5` when an Assessment stops at its whole-run time limit, and `6` when a waited Trial finishes with its gate at `loss` (a measured result, not a failure to run: the Trial output is on stdout). Retry a `3` later; do not retry a refusal unchanged.

The `help` command has its own plain stderr errors and exits `2` for invalid help syntax or an unknown entry; it does not use the failure object. An unexpected exception escaping the top-level command handler exits `4`; with `--json` it writes the failure object with code `cli.unexpected`, and without it a plain `error: ...` line. A command stopped by Ctrl+C exits `2` with code `cli.cancelled`, as a failure object with `--json` or a plain `cli.cancelled: ...` line without it. Treat any nonzero exit as failure and keep stdout and stderr separate.

With `--json`, stdout is exactly one JSON document, or empty when the command refused; progress, warnings and `error:` lines go to stderr. A script that merges the streams with `2>&1` must not parse the merge. `--pick <path,...>` keeps only the named fields of that document, so no script is needed: `motif proposal trial --project P ID --json --pick gate.state,summary.wordforms` prints `{"gate.state": ..., "summary.wordforms": ...}`. A path is names joined by dots; `items[].name` takes `name` from every element of an array and `items[2]` takes one element. An element that lacks the field gives `null`. A path that matches nothing is refused with `cli.pick-path`, which lists the names that exist; so is `--pick` without `--json`. A verb that streams its output to stdout, such as `evidence export` without `--out`, has no single document to pick from.
