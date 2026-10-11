# List Proposals

Lists open Proposals and Drafts, including each identity, label and status. Add --all to include closed ones, or --status to list one status.

## Example

```powershell
motif list --project "C:\FieldWorks\Projects\Koro\Koro.fwdata"
motif list --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --all
motif list --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --status rejected --json
```

## What it prints

One line per Proposal: its id, its status and its label. The statuses are `draft`, `proposed`, `deferred`, `applied`, `rejected`, `superseded` and `withdrawn`; the last four are closed and are listed only with `--all` or `--status`.
