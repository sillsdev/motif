# List word type dispositions

`disposition list` lists the word types that have a standing disposition, in word type key order, with the category and evidence of each and how many types stand in every category. It reads Motif's store and never writes. The MCP tool `motif_disposition_list` takes `category`, `limit` and `page`.

## Example

```text
motif disposition list --project <fwdata> --as name --limit 100 --page 2
motif disposition list --project <fwdata> --format json
```

- `--as <category>` keeps one category: `name`, `loan`, `other-language`, `abbreviation`, `numeral`, `non-word` or `misspelling`. The counts by category always cover the whole store.
- `--limit N` is the number per page, 50 by default and at most 200. `--page N` reads the next page, starting at 1; the output says when more pages exist.
- `--format table|json` (or `--json`) chooses the output.

## Related commands

- [Show a word type's disposition](cmd:disposition%20show) shows one type with its history.
- [Give word types a disposition](cmd:wordform%20dispose) records one.
