# Comparing word analyses

FieldWorks uses different definitions of the same analysis for parser comparison and duplicate cleanup. Choosing one without naming the use can turn a human judgement into an impossible parser requirement.

The parser comparator checks the ordered morph bundles' form, morphosyntactic analysis and inflection type, including its guessed-string case. It does not require sense or word-level category, which parser-created analyses do not populate in the same way as a human. Duplicate cleanup compares sense, category and full form but does not compare inflection type. The comparison to parser output follows the parser comparator; sense and category remain separate human evidence. Unresolved identities must remain visible instead of being discarded. The source is a code investigation dated 2026-08-05, not a timing measurement.

Source: `docs/research/2026-08-05-what-is-a-proper-word-analysis.md@0697348c8`.
