# Discard a Draft

`discard-draft` deletes a Draft that was never finalized, with its Trial jobs and the evidence they recorded, and frees its name. It leaves no Rationale, history row or ledger entry. A Draft produced by `reopen` is reverted instead, and its Proposal stays at its committed revision.

It is an Autonomous-mode command: set `MOTIF_AUTONOMOUS=1` or pass `--autonomous`. It is CLI only and never an MCP tool.

## Example

```text
motif discard-draft --project <fwdata> --draft probe-1 --revision <r>
```

The pending-changes Draft is a person's own and is refused with `draft.discard-pending-changes`; apply or discard it in the window. A running Trial, pinned or cited evidence, and a `--revision` that no longer matches also keep the Draft.

A probe loop that leaves no Draft behind is `new`, a compose verb, read the result, then `discard-draft`.

## Related commands

- [Discard empty drafts](cmd:discard-empty-drafts) removes every never-finalized Draft that holds no changes.
- [Start a Proposal](cmd:new) makes the Draft.
