# List decision evidence

`evidence list` lists the decisions programs staged from cited sources through Applied Proposals. It starts with how many records each tier holds, then shows one page of records.

Narrow the list with `--tier` (for example `misspelled-typo`). Page with `--offset` and `--limit`; the default page holds 50 records and the maximum is 500. Each record is labelled source-validated and is never a person's decision.

```powershell
motif evidence list --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --tier misspelled-typo --json
```
