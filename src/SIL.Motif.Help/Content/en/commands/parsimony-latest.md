# Find the newest Report

`parsimony latest` names the newest stored Parsimony Report for a project and the bundle it was measured from. It reads the project's store only and does not invoke PanGloss.

## Example

```powershell
motif parsimony latest --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

The response returns `reportId` and `bundleId`. Pass the Report id to [Read a Parsimony Report](cmd:parsimony%20show) to read its findings.

When the project has no Parsimony Report yet, the command is refused with `parsimony.no-report`. A stored Report that cannot be read is refused with `parsimony.report-damaged`.

## Related commands

- [Review grammar parsimony](cmd:parsimony) creates a Parsimony Report.
- [Read a Parsimony Report](cmd:parsimony%20show) reads one by id.
