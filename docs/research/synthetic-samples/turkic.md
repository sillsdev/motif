# Research provenance for the Turkic-style sample

This research note records sources considered for a synthetic teaching fixture. It no longer contains selected Turkish words, paradigms, sentences, or a complete grammar specification.

## Sources and scope

- UniMorph Turkish data was reviewed at commit `6c179ace7d2f3d7f3484020e5304c1544d07bb6b`; see the [pinned repository](https://github.com/unimorph/tur/tree/6c179ace7d2f3d7f3484020e5304c1544d07bb6b). The data is CC BY-SA 3.0.
- Gloss research consulted revisioned [Wiktionary entries](https://en.wiktionary.org/wiki/Wiktionary:Copyrights), retrieved 2026-09-26. Wiktionary text is CC BY-SA 4.0 or GFDL.
- Sentence-source research used the [Tatoeba API](https://api.tatoeba.org/) and its [reuse guidance](https://en.wiki.tatoeba.org/articles/show/using-the-tatoeba-corpus-for-your-own-projects). Tatoeba records use CC BY 2.0 FR and require contributor attribution.
- A linguistic reference considered for broad context was the [Turkish morphological analyzer paper](https://aclanthology.org/R19-1156/), published under CC BY 4.0.
- The [UD Turkish-IMST treebank](https://github.com/UniversalDependencies/UD_Turkish-IMST) was screened out because its non-commercial licence did not fit the research distribution.

The sources have different reliability and reuse conditions. This note does not make a language claim, reproduce the former selected data, or provide a complete teaching grammar. The local source-derived tables and data-specific selection lists were removed; no reconstruction manifest is stored in Motif.

## Product fixture

The Turkic-style product sample is supplied by the owner-approved `fixture-inputs-v1` release of `sillsdev/motif-test-grammars`. Motif keeps only its metadata lock at `evals/synthetic-samples.lock.json`; use `./evals/Get-SyntheticSamples.ps1` to provision and verify the external input.
