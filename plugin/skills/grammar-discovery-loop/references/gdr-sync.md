# Grammar Guide and sync

The Grammar Guide is the project's readable account of its grammar. Sections have stable `GDR-n` identities;
Proposals explain changes. Run `motif guide check` (Autonomous today) to see where a section is out of sync with the grammar.

When a Proposal changes a grammar object, read the relevant Guide section and record the chosen mechanism,
evidence, decisions and uncertainty alongside the change. Consult [black-guidance.md](black-guidance.md),
[mechanism-ladder.md](mechanism-ladder.md).

**Outline (required).** File each decision under the object it changes, never under a hypothesis ID. Parts
(`#`), in order:

1. Orthography and segments
2. Phonological rules
3. Word classes and inflection classes
4. Templates and slots
5. Derivation and extensions
6. Clitics
7. Irregular forms (stem names, exception features, variants)
8. Ad hoc rules
9. Records (measurement policy, sources, process)

Each section carries a paradigm or interlinear example. A `decide` that changes the mechanism rewrites **How it
works** in the same Proposal; only **Decisions** keeps history. Process notes go under Records. An existing Guide restructures once, in one Guide-only Proposal, keeping GDR
numbers and moving process records under Records.

A section has **How it works**, **Examples**, **Grouping commands**, **Exceptions**, **Decisions**, **Open questions**
and **In the grammar**, in that order. A part uses `#`, a section `##`, and template parts `###`. Generated
content is a view; the importer ignores its marked blocks. Use FieldWorks Markdown and preserve object links
and stable IDs. Do not turn a working guess into a decision.

**Grouping commands** holds ` ```motif-query ` definitions (`id`, `title`, one read-only `run: motif ...` command
without `--project`, optional `columns` and `sort`) and ` ```motif-show ` directives (`query: <id>`, optional `show:
table|counts|list`, `limit` and `columns`). Write a query with an existing read-only verb such as `motif query
forms`; `motif group suffix --ending <text>` and `motif group prefix --start <text>` also work as a `run:` line. Never paste a result into the section: results are computed at render
and are never stored. A directive must name a defined `id`. The Proposal's before and after rerun each query with
`--as-of <base revision>` and `--as-of <result revision>` (a project revision, distinct from `query stems --at`). The exact syntax is in ADR 0005 D2.

The CLI verbs are `guide list`, `guide show` and `guide check`; there are no `gdr` aliases. The developer command
`motif guide check --project <fwdata>` runs the check. The check reads the
captured project and existing Trial rows; it does not parse. Missing links, unverified examples and other
findings are advisory, including in the Trial review report. Include relevant words in a subsequent Trial when
there is no conclusive row. A warning does not block Finalize, Apply or Trial success.
