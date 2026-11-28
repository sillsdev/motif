# Generate many cells at once

`generate --batch` runs exact synthesis for a file of rows in one PanGloss run. Each input line names an entry or a sense and, optionally, a cell of affixes. For every root analysis and cell combination it writes one row to the `--out` file: each surface PanGloss generated with the entry, analysis and allomorph ids and the rules that fired, or `none` with its cause. Standard output is a bounded summary, so a run of thousands of rows stays readable.

```powershell
motif generate --batch rows.jsonl --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --out rows-out.jsonl --json
```

## Input rows

One JSON object per line. A blank line is skipped and still counts, so row numbers match your file.

```json
{"id":"r1","entry":"LATZ3P_3QsKF7LmA9uF1Ag","cell":["-s"]}
{"id":"r2","sense":"7a1f0c52-0000-4000-8000-000000000043","cell":[["-s"],["-ing","-s"]]}
{"id":"r3","entry":"LATZ3P_3QsKF7LmA9uF1Ag:LATZ3P_3QsKF7LmA9uF1Ah"}
```

- `entry` is an entry guid or Motif id, or `entry:msa` for one stem analysis. `sense` is a sense guid or Motif id. A row names at least one; a sense gives its entry and its analysis.
- Without an analysis, every stem analysis of the entry is generated, one output row each.
- `cell` names affix analyses the way `forms explain --with` does (`entry:msa`, an id, or a form in the lexicon), in generation order, which is not surface order. An array of names is one combination; an array of arrays is one combination each, at most 10,000. Without a `cell` the root is generated alone.
- A row Motif cannot send, such as an unknown sense or an unresolvable affix, becomes an `error` row with its cause and never stops the batch.

## Output rows

Each line of the `--out` file is one row with `line` and `id`, `status` (`ok`, `none` or `error`), `cause` (null when `ok`), `target` (entry, sense and stem analysis ids), `cell`, `comboKey` and `cellIndex` when the row had a cell, `complete` and `stopReason`, and `surfaces`. Every surface lists its `allomorphs` (the root, then each affix, with entry, analysis and form ids) and its rule `steps` in application order. A surface two choices of allomorph both produce appears under each. `none` causes are `generated_empty` (every choice ran and made no surface), `cap` (a work cap or deadline stopped it first) and `no_stem_msa`.

## Summary

The summary lists the artifact path, the row and surface counts, rows per status and cause, the first five `none` rows and the first five `error` rows. `complete` is true only when no row stopped on a cap and none is an error, so a missing surface in an incomplete run is not read as impossible. `--timeout-ms` and `--max-work` apply to each input row, shared by its combinations; `--at` selects a Proposal's Trial copy as in `forms explain`. Rows are streamed to the file as PanGloss writes them, so memory does not grow with the batch; the order is PanGloss's, so sort by `line` to read in input order. Every sent line gets a row: one PanGloss wrote nothing for is an `error` row with cause `no-result`, and the run is not complete. The run's wall clock is ten minutes plus `--timeout-ms` (ten seconds when omitted) for each sent line, at most 24 hours. If PanGloss is killed, times out or fails part way, the rows already written are kept, the summary carries `stopReason` (`timed_out`, `memory_limit`, `parser_killed`, `parser_failed` or `incomplete`) and `complete` is false; the run is refused only when no row was written. A batch file, project or artifact that cannot be read or written is refused as `generate-batch.io`. It needs PanGloss v0.35.6 or later.

## Related commands

- [Explain a morpheme's forms](cmd:forms%20explain) explains one root with the morphemes you list.
