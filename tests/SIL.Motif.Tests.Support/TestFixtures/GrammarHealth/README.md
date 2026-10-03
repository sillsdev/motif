# PanGloss grammar-health fixtures

`pangloss-v0.6.0-seeded.json` is the unmodified JSON captured from the pinned Linux x64
PanGloss 0.6.0 executable, SHA-256
`0b2643d7d5bd8b63849772d442829e8e00e6fc7015c12db32e5c6e154ffe6794`.
`PinnedGrammarAdviceTests.PinnedParserSuppliesAdviceForEverySeededFinding` builds a blank
LibLCM project using `PristineProjectFixture`, authors `WarningGrammar`, and calls
`RealParserProject.PrepareForParsing` to define the required boundary. It invokes
`pangloss grammar-health <seeded-project.fwdata> --fw-project "Seeded advice"` through
`PanGlossInvoker` and records the stdout beside the test binaries as `seeded-advice-raw.json`.
The captured report has six findings across three kinds: `grammar.allomorph.unsegmentable`,
`grammar.msa.no-allomorphs`, and `substrate.classification-ambiguous`. Explanation and guidance
are direct properties of every diagnostic, rather than a separate catalog or explain command.

`catalog-advice-v0.6.0.json` contains exact catalog strings for the five additional kinds
used by the screenshot fixture `SeededGrammarFindings`. Its source is PanGloss tag `v0.6.0`,
commit `944b97e7d7fe4c94d8fd5be36b725b6c3ced412e`,
`rust/crates/pg-snapshot/src/warning_metadata.rs`, function `diagnostic_advice`.
The fixture expands `{subject}` with its authored subject name, using `the item` for an
unnamed subject, as the producer's `guidance_for_subject` does. It supplies no Motif-authored
advice and preserves the existing window titles, subject names, messages and counts.

The schema-v4 producer and error fixtures cover structured subject status and navigation.
