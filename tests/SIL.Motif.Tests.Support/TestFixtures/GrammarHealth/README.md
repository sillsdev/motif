# PanGloss grammar-health fixtures

`pangloss-v0.6.2-seeded.json` is the unmodified JSON captured from the pinned Linux x64
PanGloss 0.6.2 executable, SHA-256
`ee955edee049e8710dce4748c0609a011002d5abeec20eb23d37edc033f75c2b`.
`PinnedGrammarAdviceTests.PinnedParserSuppliesAdviceForEverySeededFinding` builds a blank
LibLCM project using `PristineProjectFixture`, authors `WarningGrammar`, and calls
`RealParserProject.PrepareForParsing` to define the required boundary. It invokes
`pangloss grammar-health <seeded-project.fwdata> --fw-project "Seeded advice"` through
`PanGlossInvoker` with the pinned executable and records stdout beside the test binaries as
`seeded-advice-raw.json`. See `pangloss-fixtures.md` for the capture command.
The captured report has six findings across three kinds: `grammar.allomorph.unsegmentable`,
`grammar.msa.no-allomorphs`, and `substrate.classification-ambiguous`. Explanation and guidance
are direct properties of every diagnostic, rather than a separate catalog or explain command.

`catalog-advice-v0.6.2.json` contains exact catalog strings for the five additional kinds
used by the screenshot fixture `SeededGrammarFindings`. Its source is PanGloss tag `v0.6.1`,
commit `8d7055b2ba95c90a9b0e05f527caa53abad92da8`,
`rust/crates/pg-snapshot/src/warning_metadata.rs`, function `import_diagnostic_advice`.
It includes the producer's revised FieldWorks advice for invalid environments and unsegmentable
forms. The fixture expands `{subject}` with its authored subject name, using `the item` for an
unnamed subject, as the producer does. It supplies no Motif-authored advice and preserves the
existing window titles, subject names, messages and counts.

The schema-v4 producer and error fixtures cover structured subject status and navigation.
