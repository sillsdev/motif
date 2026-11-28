# Call-log practice

The call log records which interfaces are actually called, and how well. The reference is
[docs/call-log.md](../../../../docs/call-log.md); the writer is `src/SIL.Motif.Host/CallLog.cs`.
Do not restate fields here; read that file.

## What writes it

Every surface writes: each CLI invocation, each MCP tool call, and each window action (argument
names only). One JSON line per call, in a shard under `<worker root>/calls/`, named
`calls-YYYY-MM-DD-<pid>.jsonl` (one file per UTC day and process). Query with the glob `calls-*.jsonl`.
Normally the folder is `~/.local/share/SIL/Motif/calls/`.

## Settings and tags

| Variable | Use |
|---|---|
| `MOTIF_CALL_LOG` | a folder to write in, `off` to write nothing, `full` to also record argument values (never for real grammar work) |
| `MOTIF_LANE` | the lane that made the call; `unknown` when unset |
| `MOTIF_AGENT` | the agent that made the call; `unknown` when unset |
| `MOTIF_SESSION` | session id for CLI calls, since each CLI call is its own process; MCP uses one id per server |

The herdr lane launcher exports the lane and agent tags. When you run a rig or a test by hand, set
them so your calls do not read `unknown`.

## Outcome classes

`outcome` is one of `ok`, `user_error`, `refused`, `internal_error`, `transient`, derived from the
refusal reason. A bot's mistake is `user_error`; a policy gate is `refused`; a Motif bug is
`internal_error`.

## Queries

Run the DuckDB queries in docs/call-log.md: usage per lane and tool, error classes by tool, and the
misuse rate per lane. `motif activity --log <file or calls folder>` reads the same files.

## The evidence rule

Adding or keeping a default-profile tool needs call-log evidence after the first week: its call
count, its misuse rate and whether agents followed its `next`. A tool with no calls in a week of
real lane work is a retirement candidate ([retiring.md](retiring.md)). The log is local and its
`argHash` is guessable for low-entropy values, so never paste raw lines outside the team; share
aggregates.
