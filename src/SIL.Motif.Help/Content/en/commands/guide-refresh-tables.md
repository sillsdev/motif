# Save the tables of a Grammar Guide's query blocks

`guide refresh-tables --project <fwdata> [<GDR-n>] [--query <id>] [--draft <name>]` runs each `motif query corpus forms` block of the section (or of every section) read-only and stages its **saved table** in a Draft: a caption line, then a table of the first ten rows, stored straight after the block as ordinary FieldWorks paragraphs in the Table Header and Table Row styles. A linguist who opens the project in FieldWorks without Motif then sees the result.

The caption reads "Saved 2026-11-26 from Baseline 3f9a0c21d4be; 37 rows in all; Motif recomputes this when shown." The table holds at most ten rows in the query's own columns and sort; "N rows in all" counts every row, with "at least N" when the parse behind the rows was incomplete.

- Only this command (and the window's **Save table** button) writes a table. Showing, opening, `guide show` and `guide check` never do, and Motif always shows a recomputed result, never the stored one.
- Nothing reaches the FieldWorks project until a person applies the Draft. Without `--draft` the tables join the pending changes; a named Draft must already exist. A Guide-only Proposal is exempt from the Autonomous Trial gate, as with `guide edit`.
- A block whose run line is another kind, is refused, or has no built parse index keeps the table it has; the answer names it and why. Build the index by running the block's line in a terminal (`guide check` prints it).
- A table whose rows already match is left alone (`unchanged`); a new date alone does not restage it.
- The table is not authored text. `guide edit` keeps each block's stored table whatever the Markdown says and warns when you changed it; a hand edit made in FieldWorks lasts until the next refresh. The example check does not read it.
- `guide check` advises (`guide.saved-table-out-of-date`) when a table's rows differ from the run its block just had, so no query runs twice; `--no-run` skips the comparison and `--timeout` bounds it.

```text
motif guide refresh-tables --project Koro.fwdata GDR-4
motif guide refresh-tables --project Koro.fwdata --query dropped-letters --draft koro-guide
```

Availability: `guide refresh-tables` needs Advanced AI mode (`motif settings advanced-ai on`), like `guide edit`. The generated `motif_guide_refresh_tables` MCP tool takes `project` and the optional `id`, `draft` and `queryId`.
