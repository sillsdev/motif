# Read a Parsimony Report

`parsimony show` reads a previously stored Parsimony Report by id. It reads the saved Report and does not invoke PanGloss.

## Example

```powershell
motif parsimony show --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" <reportId> --json
```

The response returns the Report id, its frozen Baseline and artifact identities, its Assessment references, findings, and rendered text.
A check that could not look appears as a note: an information line in the rendered text and in `notes`, never a finding. A Report also lists authored grammar settings the parser ignores, as information only.

## Related commands

- [Review grammar parsimony](cmd:parsimony) creates a Parsimony Report.
