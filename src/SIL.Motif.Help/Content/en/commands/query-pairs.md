# Query pairs

`motif query pairs --corpus <words.tsv|folder> --differ suffix|prefix` (or `motif query --corpus <words.tsv|folder> pairs --differ suffix|prefix`) finds pairs of words of a plain word list that differ only by an affix: a base and base+X (suffix) or X+base (prefix). It needs no project, Trial or parse, and changes nothing.

```powershell
motif query --corpus words.tsv pairs --differ suffix
motif query --corpus texts/ pairs --differ suffix --x 'an|in' --having an,in --json
```

## The corpus

A TSV has one word per line, `form<TAB>count<TAB>parse state`. The header is optional; a first line starting `form` or `word` names the columns (`count`, `tokens` or `frequency`; `state` or `parse.state`). A missing count is 1. A folder is every `.txt` file in it, tokenised on Unicode letters and combining marks. Forms are NFC; `--fold-case` also lower-cases them. Segments are code points and the result says `approximate`. `--segments <file>` lists multi-character graphemes, such as `ng ny ch`, separated by spaces, tabs, commas or lines, with `#` comments; the longest match wins.

## The result

Per X: the bases that take it, the tokens of those derived forms, the tokens of the bases and the example pairs with the most tokens. Per base: the Xs it takes. `--x <regex>` keeps the Xs the whole differing part matches (non-backtracking). `--having a,b` keeps the bases that take all of the listed Xs. `--max-x` (1 to 12, default 4) caps the length of X in segments, `--limit` (default 100) caps the X rows and the base rows, and `--examples` (default 5) caps the examples. A cut is stated: `TRUNCATED: showing 100 of 412 Xs`, and in JSON the `truncation` object holds the totals and shown counts. Rows are in a fixed order, so a run is reproducible. `--json` writes `motif.query.pairs.v1`.
