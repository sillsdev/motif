# Read the Current chapters

`current get` reads the Current chapter of each Source from this machine's local file (`scripture-local.json`, per user, never synced). Each row carries where that chapter sits against the Source's Context. Nothing set is a normal, empty answer.

## Example

```text
motif current get --project Koro.fwdata --source WEB --json
```

## Related commands

- [Place a chapter](cmd:source%20where) places any chapter.
- [List Sources](cmd:source%20list) lists the Sources.
