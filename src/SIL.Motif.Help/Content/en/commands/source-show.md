# Show one Source

`source show <id>` reads one Source from the current Baseline: its Context as book and chapter ranges, how many chapters that is, its status, and the folder recorded for it on this machine (or none). The id is matched ignoring case. A Removed Source can still be shown.

## Example

```text
motif source show --project Koro.fwdata WEB --json
```

The folder lives in a per-user local file and is never part of the project.

## Related commands

- [List Sources](cmd:source%20list) lists every Source.
- [Place a chapter](cmd:source%20where) says where a chapter sits.
