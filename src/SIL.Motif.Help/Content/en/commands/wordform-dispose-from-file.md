# Dispose word types from a file

`wordform dispose --from-file <path>` records the dispositions listed in a file, in one transaction, so a lane can account for thousands of names, loans and misspellings in one call. It does what [wordform dispose](cmd:wordform%20dispose) does for each row: the same categories, the same evidence rule, the same contradiction refusal and `--replace`. It writes Motif's own store, never the FieldWorks project.

## Example

```text
motif wordform dispose --from-file dispositions.tsv --project <fwdata> --dry-run
motif wordform dispose --from-file dispositions.tsv --project <fwdata> --by swh-lane
motif wordform dispose --from-file ledger.tsv --project <fwdata> --format ledger --skip-refused
```

## Files

The format is detected from the first line, or set with `--format tsv|jsonl|ledger`.

- **TSV**: a header row naming `form`, `category` and `evidence`, and optionally `intended`, in any order. Without a header the columns are form, category, intended, evidence.
- **JSONL**: one object per line with the string fields `form`, `category`, `evidence` and `intended`.
- **Ledger**: a grammar lane ledger with its eight columns. Only rows whose state is `Disposition` are read, and the others are counted and left out. The `class` column is the category. A misspelling's `intended=<form> | <evidence>` prefix is split at the first " | ", as Coverage splits it; an intended form that is not one word is refused on its row. Rows are read less strictly than Coverage reads a ledger (blank lines skipped, tokens and revision not checked). Evidence longer than the limit is cut and counted. A class that is not a category, such as `mixed`, is refused on its row.

## One transaction, with refusals

Every row is checked before anything is written. A row with a bad category, no evidence, a missing intended form, an empty form, or a word type that already stands with another disposition is refused with its line number. If any row is refused, nothing is written. Add `--skip-refused` to write the rows that are accepted and report the rest, or `--replace` to end a standing disposition that a row contradicts. A word type that appears twice with the same disposition counts once; with different dispositions, the later row is refused.

A word type that already stands as the row asks is left alone, and evidence it does not yet hold is appended to its evidence, so running the same file again changes nothing.

## Dry run and output

`--dry-run` reports the counts a real run would give and writes nothing. The text output gives the counts of added, replaced, unchanged and refused rows and the first refusals. `--json` gives the response as one object. `--format json` prints one JSON object per row, then a last object with the counts. The exit code is non-zero when rows were refused and nothing was written.

## Related commands

- [Give word types a disposition](cmd:wordform%20dispose) records a few forms given on the command line.
- [Clear word type dispositions](cmd:wordform%20undispose) removes one.
- [List word type dispositions](cmd:disposition%20list) pages through everything recorded.
