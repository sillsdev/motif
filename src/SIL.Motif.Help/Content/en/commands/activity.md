# Summarize agent activity

`activity` reads an activity log and reports how Motif's MCP server served an AI agent: the calls by tool, how many came back as errors, the refusals by code, and how often the agent followed the advice it was given.

The log exists only when the server was started with `motif mcp --activity-log <file>`; without that option nothing is written. The file stays on your machine, and `activity` only reads it. Nothing is sent anywhere.

## When to use it

Use it after an agent session to see where the agent was refused and whether it acted on each response's next step. "Followed" means the call used the tool the previous response's next step named; its arguments are not compared. The first call of a session counts as neither followed nor ignored.

## Example

```powershell
motif activity --log "C:\Motif\session-activity.jsonl" --json
```

## What it prints

The response counts all calls, the calls that came back as errors, calls per tool, refusals per refusal code, and the calls that followed or ignored the previous next step. Lines written by an older Motif that lack the next-step field are still read and counted as neither. A line that is not a log entry is skipped. When the file is missing or unreadable, the command refuses and says how to start a session that writes one.
