# Grammar Guide and sync

The Grammar Guide is the project's readable account of its grammar. Sections have stable `GDR-n` identities;
Proposals explain changes. Consult [ADR 0005](../../../../docs/adr/0005-the-grammar-guide.md) for the
accepted Guide structure, generated blocks and sync contract.

When a Proposal changes a grammar object, read the relevant Guide section and record the chosen mechanism,
evidence, decisions and uncertainty alongside the change. Consult [black-guidance.md](black-guidance.md),
[mechanism-ladder.md](mechanism-ladder.md) and the findings in `docs/references/grammar-authoring.md`.

A section has **How it works**, **Examples**, **Exceptions**, **Decisions**, **Open questions** and
**In the grammar**, in that order. A part uses `#`, a section `##`, and template parts `###`. Generated
content is a view; the importer ignores its marked blocks. Use FieldWorks Markdown and preserve object links
and stable IDs. Do not turn a working guess into a decision.

The accepted CLI direction is `guide …`, without `gdr` aliases; the current implementation still exposes the
developer command `motif guide check --project <fwdata>`. The check reads the
captured project and existing Trial rows; it does not parse. Missing links, unverified examples and other
findings are advisory, including in the Trial review report. Include relevant words in a subsequent Trial when
there is no conclusive row. A warning does not block Finalize, Apply or Trial success.
