# Generator and manifest findings

The model supplies structural facts; generated operations still need reviewed names, descriptions and exceptions. FieldWorks labels are helpful evidence, but their meaning depends on a view or owning list.

- The 2026-08-03 audit of 473 in-scope rows found 27 with pinpoint citations, 40 with a named source and 406 with generic rationale. The audit's concern was hand-authored classifications becoming code. Current derivation and its closed exceptions are described in ADR 0013.
- The description-source investigation found 73 of 93 descriptions sourced from the model or FieldWorks Help, five preserved corrections and 15 without a found source. A sourced description carries provenance, not an invented paraphrase.
- UI labels live in multiple FieldWorks configuration mechanisms rather than in the schema. A bare `CmPossibility` may get its meaning from its owning list; class metadata alone cannot recover that name. Harvested labels are a cross-check, not a complete authority.
- Public API surveys separated stable machine identity from changeable display labels and descriptions; renaming a wire identity needs explicit compatibility handling.
- The 2026-08-11 ordering census examined 64 ordered fields: 32 had explicit evidence and 32 rested on sequence cardinality alone. Of the latter, 14 were parser-reachable and 18 were not. A sequence's existence does not by itself prove what its order means. These are source audits, not machine benchmarks.

Sources: `docs/research/2026-08-03-manifest-trust-audit.md@0697348c8`; `docs/research/2026-08-09-kind-description-sources.md@0697348c8`; `docs/research/2026-08-05-fieldworks-user-facing-names.md@0697348c8`; `docs/research/2026-08-05-naming-a-public-api-over-a-legacy-model.md@0697348c8`; `docs/research/2026-08-11-ordering-claims-census.md@0697348c8`.
