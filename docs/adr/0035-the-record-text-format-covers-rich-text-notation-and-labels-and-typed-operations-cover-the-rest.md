# ADR 0035 — The record text format covers rich text, notation and labels; typed operations cover the rest

The Markdown-like record file exists to support Harmony, AI-based editing and human diff review, and only for the
fields where text reads better than a typed change. Every other field is edited by typed operations alone.

**Status:** Accepted, 2026-11-23.

## Context

A record file (an entry, a notebook record, a list item) can show a FieldWorks record as text
([ADR 0006](0006-rich-text-is-fieldworks-markdown-in-the-shared-textediting-field.md)), and every edit lowers to a
Harmony change ([ADR 0026](0026-a-project-home-is-fwdata-or-a-harmony-log-everything-else-is-built.md)). Coverage had
been counted as how many FieldWorks fields the text can edit, which treated every uncovered field as a gap. By distinct
field at the decision:

| Kind | Covered | Uncovered |
|---|---|---|
| Rich text | 81 | 18 |
| Notation | 35 | 36 |
| Labels | 42 | 60 |
| Plain values | 8 | 141 |
| References | 43 | 124 |
| Owned objects | 11 | 40 |

## Decisions

### D1. The text format carries rich text, notation and labels

In the text format: rich text in several writing systems and paragraph text; grammar notation (environments,
phonological rules, affix templates, allomorph conditions); and name and abbreviation labels in each writing system,
through the shared multi-string path. As Harmony JSON patches these are unreadable to diff and to draft; as text they
are not.

### D2. Everything else is a typed operation

Plain values (true/false, numbers, dates, choices), references and list membership, and creating or deleting owned
objects are out of the text format, apart from the nesting entry files already have (senses, examples). Each is a typed
operation that lowers to a Harmony change. A typed change ("Sense 2: part of speech noun → verb") is already the best
diff for these fields and what AI drafts best, as a checked tool call. Text would add a parse step, and a reference
written as a name would show an ambiguous name in place of an id.

### D3. Coverage is counted per field by owner, operation and change type

Every FieldWorks field has an owner (D5 of ADR 0026), a typed operation and a Harmony change type. Text-format
coverage counts only the fields D1 names; the 305 uncovered plain-value, reference and owned-object fields are not gaps.

The alternatives were text for every field (rejected: those 305 fields gain nothing from it) and no text format, typed
operations only (rejected: rich text and notation are unreadable that way).

## Consequences

A new field is classed by what it holds: rich text, notation or a label gets a text form; anything else gets a typed
operation and a Harmony change type, and no text form. The remaining text gaps are the 18 rich-text, 36 notation and
60 label fields.
