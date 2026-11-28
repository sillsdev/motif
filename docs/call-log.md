# Call log

Every CLI invocation and every MCP tool call appends one JSON line to a daily file, so a week later we can ask which tools bots used and whether they used them well. It is on by default, never fails or slows a call (IO errors are swallowed), and the file never leaves the machine.

| Setting | Effect |
|---|---|
| (unset) | `<worker root>/calls/calls-YYYY-MM-DD-<pid>.jsonl`, normally `~/.local/share/SIL/Motif/calls/`; one file per UTC day and process, so concurrent processes never overwrite each other (query with the glob `calls-*.jsonl`) |
| `MOTIF_CALL_LOG=<dir>` | write the day files in `<dir>` |
| `MOTIF_CALL_LOG=off` | write nothing |
| `MOTIF_CALL_LOG=full` | as the default, and also record argument values (`args`); never use for real grammar work |
| `MOTIF_LANE`, `MOTIF_AGENT` | who made the call; `unknown` when unset. The lane launcher exports them |
| `MOTIF_SESSION` | session id for CLI calls (each CLI call is its own process); MCP uses one id per server |

Files older than 30 days are deleted when a process first writes.  `motif mcp --activity-log` is gone; `motif activity --log <file or calls folder>` reads this log.

Privacy: `argHash` is an unsalted truncated sha256, so a low-entropy value (one word, yes/no) can be guessed by dictionary; do not share the files outside the team. An unregistered MCP tool name and an unknown CLI command log as `unknown` with no argument keys.

## Fields

`v` schema version (1); `ts` UTC time; `id`; `parent` previous call id in the same process (null for the first); `session`; `lane`; `agent`; `surface` `cli`, `mcp` or `window` (a window action: argument names only, outcome `unknown`); `tool` MCP tool or catalog command (`help`/`unknown` when the CLI could not name one); `profile` (MCP); `argKeys`, `argSizes` (bytes per key), `argHash` (first 16 hex of sha256 over the sorted `key=value` text); `args` (full mode only; every value is a string, numbers, booleans and objects as JSON text); `outcome`; `code` refusal code; `reason` `FailureReason`; `exit` CLI exit code; `durMs`; `resultBytes` (MCP); `jobId`; `proposalId` (from a `proposal` argument); `nextNamed` the tool this call's `next` advice named (MCP); `followedPrev` whether the previous call's advice named this tool. Null fields are omitted. `grammarId`, `revBefore` and `revAfter` are not written yet.

`outcome` is derived from `FailureReason`: `ok`; `user_error` (InvalidArgument, NotFound, a failed precondition); `refused` (policy gates: `apply.*`, `command.*`, `agent.*` codes); `internal_error` (StoreInconsistent, `tool.failed`, out of memory, an unhandled exception); `transient` (Busy, Cancelled, StoppedByBudget). A CLI failure with no captured refusal is classed from its exit code alone.

## Queries (DuckDB)

Usage per lane and tool:

```sql
SELECT lane, tool, count(*) n, round(avg(durMs)) ms
FROM read_json_auto('~/.local/share/SIL/Motif/calls/calls-*.jsonl')
GROUP BY ALL ORDER BY lane, n DESC;
```

Error classes by tool:

```sql
SELECT tool, count(*) n,
  count(*) FILTER (outcome='user_error') user_err,
  count(*) FILTER (outcome='internal_error') internal
FROM read_json_auto('~/.local/share/SIL/Motif/calls/calls-*.jsonl') GROUP BY tool ORDER BY user_err DESC;
```

Misuse rate per lane (a user error, a blind retry of the same arguments after one, a repeat with no change, or ignored `next` advice after a failure):

```sql
WITH c AS (
  SELECT *, lag(outcome) OVER w prev_out, lag(argHash) OVER w prev_hash, lag(tool) OVER w prev_tool
  FROM read_json_auto('~/.local/share/SIL/Motif/calls/calls-*.jsonl')
  WINDOW w AS (PARTITION BY session ORDER BY ts))
SELECT lane, count(*) calls,
  round(100.0*count(*) FILTER (
      outcome='user_error'
   OR (prev_out='user_error' AND argHash=prev_hash)
   OR (tool=prev_tool AND argHash=prev_hash)
   OR (followedPrev=false AND prev_out<>'ok')) / count(*), 1) misuse_pct
FROM c GROUP BY lane ORDER BY misuse_pct DESC;
```
