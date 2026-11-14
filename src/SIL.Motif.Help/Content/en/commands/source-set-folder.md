# Record a Source's folder

`source set-folder` records the Paratext project folder of a Source on this machine, in the local file (`scripture-local.json`, per user, never synced). It is local and instant: no Draft, no Apply, and FieldWorks may be open. The Source must be in the project's applied Source list and the folder must exist.

## Example

```text
motif source set-folder --project Koro.fwdata WEB /data/Paratext/WEB
```

## Related commands

- [Show one Source](cmd:source%20show) shows the recorded folder.
- [Forget a folder](cmd:source%20clear-folder) removes it.
