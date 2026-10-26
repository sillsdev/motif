# Bantu-style sample source provenance

The former derived tables are not stored in this repository. Their source-derived lexical and sentence content had separate provenance and licensing and was removed with the local copies.

Sources considered include the pinned UniMorph `swc` revision `02a2bdec0e5cb0dc93b6ef11db4d54a82c34b224`, Tatoeba downloads, Wiktionary, and published Swahili references. See [the research note](../../bantu.md) for source links and limits.

The generic extraction entrypoint requires raw source files and a selection manifest supplied outside the repository. Each manifest dataset names an input and output path, a zero-based TSV key column, and the keys to retain. Output stays under the ignored `bin/.cache/research-samples/` tree. No selection list or derived dataset is kept here.
