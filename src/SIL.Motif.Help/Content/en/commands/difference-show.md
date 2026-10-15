# Read a saved comparison

`difference show` reads a comparison saved by `difference produce`. It always starts with the totals, the before and after table and the count of words in each category, then lists one page of words.

Choose the comparison with `--difference`, or give `--proposal` to read that Proposal's newest. Narrow the words with `--could-be-bad`, `--bucket` (a category code such as `delta.regression` or `delta.improved.unique`), `--before` and `--after` (an approval state such as `approved-unique`, `approved-with-alternatives`, `no-approved-match` or `no-parse`). Page with `--offset` and `--limit`; the default page holds 25 words and the maximum is 200.

Categories are advice about parses and recorded decisions. A word with no recorded approval shows only that its parses changed, and a search that did not finish never proves that a reading disappeared.

```powershell
motif difference show --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --proposal <proposalId> --could-be-bad --json
```
