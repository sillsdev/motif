# Discard empty drafts

`discard-empty-drafts` removes every Draft that was never finalized and holds no changes, such as the ones a failed compose leaves after a `new`. A Draft with changes, the pending changes and any Draft that was finalized or reopened are left alone. A Draft with a Rationale or Comments, or one that cannot be discarded, is kept and listed with the reason.

It is an Autonomous-mode command: set `MOTIF_AUTONOMOUS=1` or pass `--autonomous`. It is CLI only and never an MCP tool.

## Example

```text
motif discard-empty-drafts --project <fwdata> --prefix probe-
```

## Related commands

- [Discard a Draft](cmd:discard-draft) deletes one named Draft.
