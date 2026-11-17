# Primary sources

## Separate licence: Black's conceptual introduction is not MIT

`black-flex-conceptual-intro-fulltext.txt` is separately licensed, not covered by Motif's MIT licence.
It is freely available and made to ship with FieldWorks. The named third-party document exception in
[ADR 0002](../../../adr/0002-grammar-data-never-belongs-in-motif.md) permits retaining it here, including
its explanatory examples; it grants no general permission to commit language data.

Verbatim primary-source material this guide's `workflow/` section distills from and cites. Kept
here as full text (not just summarized) so an LLM reading this guide has the original to check
claims against, not just a paraphrase.

- **`black-flex-conceptual-intro-fulltext.txt`** — "A Conceptual Introduction to Morphological
  Parsing for FieldWorks Language Explorer," H. Andrew Black (SIL), 3 July 2025. Ships as a Help
  file with FieldWorks 9 (`Helps/WW-ConceptualIntro/ConceptualIntroFLEx.pdf` under the FieldWorks
  install directory). Extracted verbatim via `pdftotext -layout`; page markers are PDF page
  numbers.

**Provenance note:** this material properly belongs alongside FieldWorks itself (it already ships
there as a Help file) — it's included in this repo for now as a practical staging point, not
because `machine` is its long-term home. If/when it's relocated to the FieldWorks repo, update the
citations in `workflow/` to point there instead.

Nothing in `workflow/`'s own pages is a copy of this text — those pages are original guidance,
independently checked against the actual HermitCrab engine source, that cites specific sections
here for a reader who wants Black's own words.
