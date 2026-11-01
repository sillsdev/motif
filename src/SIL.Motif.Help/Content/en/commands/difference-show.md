# Read a saved comparison

`difference show` reads a comparison saved by `difference produce`. It always starts with the totals, the before and after table and the count of words in each category, then lists one page of words.

Choose the comparison with `--difference`, or give `--proposal` to read that Proposal's newest. A Proposal keeps one comparison per Text and reference, so add `--text` (a Text GUID) and `--reference human|source-validated` to read a particular one; they are refused without `--proposal`. The output says which Text and reference the comparison belongs to, and lists the Proposal's other saved comparisons the same way, newest first and at most 50 (the JSON has `Saved` and `SavedTotal`). A saved comparison whose stored summary can no longer be read is left out of that list and counted, with a line saying how many could not be read (`SavedUnreadable` in the JSON), and the others still show. Narrow the words with `--could-be-bad`, `--bucket` (a category code such as `delta.regression` or `delta.improved.unique`), `--before` and `--after` (an approval state such as `approved-unique`, `approved-with-alternatives`, `no-approved-match` or `no-parse`). Use `--limitation` to list only the words one side of which a limit held back (a code such as `capped`, `timed-out`, `projection-unavailable` or `parser-refused`); a word counts for a limitation that touched either side, as the totals do. Page with `--offset` and `--limit`; the default page holds 25 words and the maximum is 200.

Give `--word` to list one exact word together with the places in the Text where it occurs, a page at a time: `--occurrence-offset` and `--occurrence-limit` (default 25, maximum 200), which are refused without `--word`. Each place has the Text, the line, the line's number and the word's position in the line. The places are word detail, so they are kept only while the Proposal is undecided and not deferred.

When the Proposal has been applied, rejected, withdrawn, superseded or deferred, the word list is no longer kept; for a deferred Proposal, `difference produce` makes the comparison again to see the words. The command still succeeds: it shows the summary, no words, and says that word detail is no longer kept. In the JSON, `WordDetailKept` is `false`.

Categories are advice about parses and recorded decisions. A word with no recorded approval shows only that its parses changed, and a search that did not finish never proves that a reading disappeared.

```powershell
motif difference show --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --proposal <proposalId> --could-be-bad --json
```
