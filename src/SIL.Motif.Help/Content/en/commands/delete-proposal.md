# Delete a closed Proposal

`delete --proposal` deletes a declined, superseded or withdrawn Proposal that never applied, together with its [Trials](term:trial).

## When to use it

Use it to clear away Proposals nobody will apply. An applied Proposal is final: it is never deleted, and its Receipts are kept. A Proposal that is still open, or is being tried, is refused. A Proposal that a saved phonology Example Set cites is refused with `proposal.retained-by-example-set`; the set keeps the Proposal's evidence, so it stays until the set is gone.

## Example

```powershell
motif delete --proposal <id> --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

The deleted Proposal's id and how many Trials were removed with it. In the window, a closed card on **Proposals** offers **Delete** for the same.

## Related commands

- [List Proposals](cmd:proposals) lists closed Proposals with `--include-set-aside`.
