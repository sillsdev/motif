# Approve an Occurrence of a Source

`source approve` stages the choice of an analysis for one word of a Paratext Source. To Approve an Occurrence is to choose the analysis it has, and that also approves the analysis. Until then the Occurrence shows a guess. Nothing is written to the project until the pending changes are applied; add `--apply` to apply every pending change straight after staging.

Name the word by its location, such as `MAT 5:3#2` (the second word of verse 3). Choose the analysis with `--analysis <id>`, a stored analysis of the word's wordform. When FieldWorks holds no analysis yet and only a parser reading exists, give `--wordform <id>` and `--reading <json>`: the analysis is added from the parser and approved in the same Draft, and Apply writes both or neither.

- When the analysis has an Unknown Opinion, approving the Occurrence also stages Opinion Approved. A Disapproved Opinion is left as it is, and the command says which Occurrences conflict with it.
- An Occurrence holds one approved analysis. A newer approval replaces the older one; [clear it](cmd:source%20clear) to return it to a guess.
- `--bulk` also approves the same wordform elsewhere in the Source, across its chapters. It leaves alone Occurrences already approved with another analysis, Occurrences whose earlier approval is stale, and those already approved with this analysis. The command shows the count before anything is applied, for example `Approve 37 Occurrences in 12 chapters of Source WEB.`
- An approval carries over only while the verse text, the word number and the analysis are unchanged. Otherwise it is shown as stale, is not counted as approved, and is never approved again automatically.

## Example

```text
motif source approve --project Koro.fwdata WEB "MAT 5:3#2" --analysis <analysis id> --bulk
```

## Related commands

- [Read a chapter](cmd:source%20passage) shows each word as approved, stale or a guess.
- [Clear an Occurrence](cmd:source%20clear) returns it to a guess.
- [Apply the pending changes](cmd:apply%20--all-pending) writes the staged change to the project.
