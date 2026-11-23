# ADR 0033 — Lexicon import accepts only LIFT and lowers through composers

A lexicon that comes from outside a FieldWorks project enters Motif as a LIFT file, becomes ordinary Drafts through
the entry composers, and carries its source and licence with it. Motif owns no other input format.

**Status:** Accepted, 2026-11-17.

**Amended 2026-11-23:** D1 names LIFT the only format for bringing a lexicon from outside a project; a record file is
a second, separate thing, Motif's own round-trip edit format.

## Context

Grammar builds need thousands of stems from outside sources: a GPL-licensed lexc source for one language, glosses
under CC BY-SA for another, a Wiktionary extract for a third. Each source has its own format, and a hand-written
reader per format would put source knowledge inside Motif ([ADR 0002](0002-grammar-data-never-belongs-in-motif.md)).
LIFT is the interchange format FieldWorks itself reads and writes, so one reader serves every source once a converter
outside Motif has produced LIFT. Large imports also risk losing words that parse today, and the licences of the
sources must travel with the data into the project.

## Decisions

### D1. LIFT is the only import format for a lexicon from outside

`motif lexicon import` reads a `.lift` file and nothing else; there is no format option. Other sources (lexc,
Wiktionary JSONL, TSV, SFM) are converted to LIFT by tools outside Motif, as fetching already lives outside Motif
([ADR 0024](0024-sources-are-read-in-place-and-scripture-stays-in-paratext.md) D6). Motif accepts LIFT 0.13 only;
a 0.14 or 0.15 file is refused with "convert to 0.13". The alternatives were a Motif-defined record schema
(rejected: a second format for converters to track) and FieldWorks' own LIFT merge (rejected: it creates categories,
classes and features that Motif must not invent, and it bypasses Draft, Trial and the gates).

Amendment: this rule is about lexicons that come from outside a project, and LIFT stays the interchange format for
them. A record file is not an interchange format. It is Motif's own round-trip edit format: `motif record export`
writes a record of the saved project as a file, a person or agent edits it, and `motif import` plans the edits back
against that same project, lowering them to Draft operations and never applying them. A record file carries the
digest of the record as exported and is refused whole when the project changed since, so it is not read for a
different project and is not a source for converters to target. Nothing in this ADR's other decisions changes: a
lexicon from outside enters as LIFT through the entry composers.

### D2. Validation first, with the referenced libraries

The whole file is validated against `lift-0.13.rng` using `SIL.Lift` at the libpalaso pin, and parsed with its
`LiftParser` through a read-only merger that collects entries and writes nothing. A file that fails validation is
refused whole, before any entry is read. Per-entry refusals name the XML line and say what to do next
([ADR 0009](0009-assistants-reach-motif-through-one-agent-api.md) D9). Every LIFT element is carried, ignored with a
counted warning, or refused; none is dropped silently.

### D3. The import format is a clean superset of LIFT 0.13

Standard LIFT is used wherever LIFT or FieldWorks' own LIFT export has a representation; an extension never
duplicates one. An extension exists only where neither has a slot, and:

1. it maps one-to-one onto one FieldWorks field or object, such as a `stem-name` trait on `<variant>` mapping to the
   stem allomorph's stem name;
2. an import-only hint that is not FieldWorks data (the attested count and its source, a sibling group) is named as
   such, carries the `motif-` prefix, and is never written to the project;
3. all extensions follow one naming scheme: FieldWorks-style names for traits that map onto a FieldWorks field, the
   `motif-` prefix for import hints;
4. all extensions appear in one published mapping table, LIFT element or trait to FieldWorks class and field to
   composer, on the lexicon import Help page.

A file that validates and uses only listed extensions is accepted. An unknown extension is refused with its line and
never ignored. Traits and fields that FieldWorks itself exports and Motif does not read are not extensions; they are
ignored with a counted warning. Extensions are ordinary LIFT traits, so a file with them still validates, and
FieldWorks keeps them as residue on import.

### D4. Import lowers through composers into ordinary Drafts

Each entry becomes composer intents ([ADR 0013](0013-the-operation-catalog-is-generated-from-the-liblcm-model.md)),
batched into Drafts a chunk at a time, so every write is a shape FieldWorks can create
([ADR 0003](0003-motif-writes-only-what-fieldworks-can-create.md) D1). Import creates no category, inflection class,
feature, stem name, environment or variant type; a name that does not resolve in the project is a refusal. The first
sense of an entry is imported. Drafts then follow the one lifecycle: Dry Run, Trial, Finalize, review, Apply. There is
no import-only Apply route, and an import never finalizes.

### D5. Exempt from the gain gate, never from no-loss

A lexicon import is exempt from the attested-gain gate of [ADR 0021](0021-a-trial-review-report-has-three-levels-and-no-verdict.md)
D14, as a Proposal that adds a rule or affix process already is: its stems are expected to buy few attested words
until the grammar uses them. It is never exempt from the no-loss checks: New word failures, Confirmed correct
readings lost, and the project's gate (D16 there). Adding stems can cost words through parser work limits or new
readings, so the first chunks are the cost probe, and attested stems may be imported first as a separate measured
step from the rest. A hand-written Proposal cannot claim the exemption.

### D6. Re-import creates or skips; updates are named

A record is keyed by the entry's `guid`, else its `id`. A re-import creates a missing entry and skips an existing
one, reporting how the existing one differs. It changes fields only under an explicit `--update <fields>`, only on
entries whose fields still hold what the import last wrote, and refuses to overwrite a person's later edit.

### D7. Empty glosses follow FieldWorks

An entry with no gloss is imported only if FieldWorks' own entry-creation path makes such a sense; until that is
verified against FieldWorks, an entry with no gloss is refused.

### D8. Attribution travels inside the project

LIFT 0.13 has no licence or source slot, so the importer takes source, revision and licence as options and records
them. The project keeps them in two places, using fields FieldWorks already has and adding none:

1. **Per entry:** a short tag `<source>@<revision> <licence>`, such as `lang-sme@9ba7be7 GPL-3.0`, in
   `LexEntry.Bibliography`. It survives `.fwdata` and a LIFT round trip, because FieldWorks exports it as a
   bibliography note. The tag comes from the LIFT entry's own bibliography note when present, otherwise from the
   options.
2. **Per project:** one notice with source, revision and licence appended to `CmProject.Description`, once per source
   and revision, idempotent.

A citation on each operation also records the source in the Motif store
([ADR 0007](0007-a-proposal-has-one-lifecycle-a-conversation-citations-and-one-apply-path.md)); that is review
metadata and does not carry the licence. The rule is written once, in the lexicon import Help page; the plugin skill
and the glossary link to it. The alternatives were a new FieldWorks field (rejected: Motif writes only what
FieldWorks can create) and a sense-level source field (rejected: Motif has no operation for it and it does not
survive LIFT export at the entry).

## Consequences

Converters live outside Motif and are checked with the same schema and library Motif uses, so a file that passes
there is accepted here. Because the importer creates only what the project already defines, a project must have its
categories, classes, features and stem names before import. Large imports run as a chain of Drafts, each Trialled
and Applied before the next is composed, which keeps each Trial's Baseline current. The licence notices are visible
to anyone who opens the project in FieldWorks or exports it.
