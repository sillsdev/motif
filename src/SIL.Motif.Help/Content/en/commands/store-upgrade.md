# Upgrade a project store

`store upgrade` carries authored work from an older Motif store into the current format. Motif keeps the old
store as a versioned backup and discards computed results, which can be recreated. The one computed result it
keeps is the saved Difference summary of each decided Proposal, because it cannot be recreated once the Proposal
is decided; the word detail under it is discarded. Summaries are kept from schema 56 on; an upgrade from an
older schema drops them, because they are in a shape this Motif cannot read.

```powershell
motif store upgrade --project "C:\FieldWorks\Projects\Koro\Koro.fwdata"
```

Opening a project also upgrades its older store automatically. The upgrade report beside the store lists the
rows carried, tables discarded, and any Proposal revisions that the current Motif cannot read.

To inspect an older store without changing it, give an existing source and a new output path:

```powershell
motif store upgrade --source old.motif.db --out upgraded.motif.db --json
```
