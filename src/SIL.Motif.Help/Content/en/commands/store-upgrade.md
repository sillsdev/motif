# Upgrade a project store

`store upgrade` carries authored work from an older Motif store into the current format. Motif keeps the old
store as a versioned backup and discards computed results, which can be recreated.

```powershell
motif store upgrade --project "C:\FieldWorks\Projects\Koro\Koro.fwdata"
```

Opening a project also upgrades its older store automatically. The upgrade report beside the store lists the
rows carried, tables discarded, and any Proposal revisions that the current Motif cannot read.

To inspect an older store without changing it, give an existing source and a new output path:

```powershell
motif store upgrade --source old.motif.db --out upgraded.motif.db --json
```
