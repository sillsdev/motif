# Turkic-style sample source provenance

The former derived tables are not stored in this repository. Their source-derived lexical forms, translations, and sentence text were removed with the local copies.

Sources considered include the pinned UniMorph Turkish revision `6c179ace7d2f3d7f3484020e5304c1544d07bb6b`, revisioned Wiktionary entries, and Tatoeba records. Their reuse conditions differ; see [the research note](../../turkic.md) for provenance and links.

The generic extraction entrypoint requires raw source files and a selection manifest supplied outside the repository. Each manifest dataset names an input and output path, a zero-based TSV key column, and the keys to retain. Output stays under the ignored `bin/.cache/research-samples/` tree. No selection list or derived dataset is kept here.
