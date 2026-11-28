# Summarize agent activity

`activity` reads Motif's call log and reports how its tools were used: the calls by tool, how many came back as errors, the refusals by code, and how often the agent followed the advice it was given.

Motif writes the call log by itself, one file a day under its worker root in a `calls` folder (see `MOTIF_CALL_LOG`). Pass one day file or the folder with `--log`. The files stay on your machine, and `activity` only reads them. Nothing is sent anywhere.

## When to use it

Use it after an agent session to see where the agent was refused and whether it acted on each response's next step. "Followed" means the call used the tool the previous response's next step named; its arguments are not compared. The first call of a session counts as neither followed nor ignored.

## Example

```powershell
motif activity --log "$HOME\.local\share\SIL\Motif\calls" --json
```

## What it prints

The response counts all calls, the calls that came back as errors, calls per tool, refusals per refusal code, and the calls that followed or ignored the previous next step. A line that is not a log entry is skipped. When the file or folder is missing or unreadable, the command refuses.
