# Query values

`motif query --values classes --project <fwdata>` (or `--grammar <path>`) lists the natural classes and phonological features of the project's Baseline or of a grammar file: the names a `phon` or `env` pattern can use. The MCP tool `motif_query_schema` returns the same list with `values: "classes"`.

Each class shows its name, its abbreviation when it differs, and for a segment class how many phonemes it holds. PanGloss lists neither classes nor features, so the names are read from the grammar; a name PanGloss does not know is refused as `unknown_natural_class` with the nearest names. `--json` writes `motif.query.values.v1`. See `query forms` for the pattern language.
