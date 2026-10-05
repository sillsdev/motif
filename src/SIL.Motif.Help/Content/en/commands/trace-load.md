# Load a saved trace

`trace --load` reads a saved PanGloss diagnostic without opening a project or starting the parser. It uses the same reader as a live trace.

## Example

```powershell
motif trace --load "C:\Temp\mirusi.trace.json"
```

Add `--json` to get the typed response, including its shared `reading` and recorded provenance. Motif keeps the producer diagnostic unchanged; a clean process exit does not establish whether a trace reached its search limit, so completion can remain unknown.

## Related commands

- [Trace a word](cmd:trace) runs PanGloss against a project's current Baseline.
- [Prepare an AI Handoff](cmd:handoff) includes a selected word's trace with its summary.
