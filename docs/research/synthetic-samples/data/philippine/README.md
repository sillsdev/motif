# Philippine-style sample source provenance

The former derived tables are not stored in this repository. Source-derived Tagalog forms, translations, and sentence text were removed with the local copies.

Sources considered include the pinned UniMorph Tagalog revision `799d79de338d1370ebfbeabb9823a0e8f86a2b6a`, Wiktionary, and Tatoeba. Each source has its own reuse terms; see [the research note](../../philippine.md) for provenance and links.

The generic extraction entrypoint requires raw source files and a selection manifest supplied outside the repository. Each manifest dataset names an input and output path, a zero-based TSV key column, and the keys to retain. Output stays under the ignored `bin/.cache/research-samples/` tree. No selection list or derived dataset is kept here.
