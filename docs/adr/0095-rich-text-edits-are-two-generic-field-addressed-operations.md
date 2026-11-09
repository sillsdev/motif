# 0095. A person's rich-text edits are two generic, field-addressed operations

A person who edits prose in Motif, a Grammar Design Record's Discussion today and a sense's definition, gloss or note
next, should have that edit reach the Draft without losing its formatting, with paragraphs added, joined or removed,
and with the text it began from checked at Apply. Motif does this with two operation kinds addressed by object and
field, not one kind per field or per record type.

**Status:** accepted, 2026-11-09 (owner). Applies [ADR 0083](0083-grammar-design-records-live-in-the-fieldworks-project.md)
§3–§4 (a person's GDR edit joins the one Draft and remembers the text it began from) to every rich-text field, and
gives #88's conflict viewer its hook. Uses SIL.TextEditing's paragraph keys (textediting ADR 0006).

## Context

The only existing kind that writes paragraph text, `analysis/stTxtPara/setContents`, writes one plain run in one
writing system: typing a letter would drop a paragraph's character styles, links and pictures. No kind removes or
joins a paragraph, and no generated kind carries a starting text that Apply checks. GDRs are a template and
conventions on a FieldWorks custom possibility list; the same rich-text editing must work wherever a field holds
prose.

## Decision

1. **`text/setParagraphs`** edits any StText-valued field, addressed by owner object and field. Its `after` holds the
   paragraph order (each paragraph's key, null for a new one), each changed or new paragraph's style name and runs
   (text, writing system, character style and the other run properties the field allows), the removed paragraphs'
   keys, and `expected`: the starting text of every changed or removed paragraph, by key. Apply creates the StText
   when the field has none, keeps existing paragraph identities and creates new ones.
2. **`text/setRichString`** edits any String or MultiString field, addressed by owner object, field and writing
   system. Its `after` holds the runs and the expected starting text.
3. **Drift holds the Apply.** If a field no longer matches `expected` at Apply, Apply refuses as a whole with a
   stale-field refusal naming that field, and the Draft stays whole (owner, 2026-11-09: while one field is undecided,
   Apply waits). The person resolves the field (the conflict viewer, #88), which restages it from the live text, and
   applies again.
4. **One editing session updates one operation.** A later session on the same field replaces the operation (same
   target and field), and `expected` stays the text of the first session's start.
5. **Which fields may be edited this way comes from the field inventory**, not from per-field kinds. The kinds join
   the operation catalogue additively; existing plain-text kinds such as `setContents` stay for agents' Proposals
   until rich text replaces them, and the generator is not changed.
6. **Templates add no kinds.** A GDR is the `text/setParagraphs` edit of a possibility's Discussion; the GDR template's
   rules (Motif's GDR checker) run over the result like any other check.

## Consequences

Every prose surface (GDRs, sense definitions and notes, later others) shares one Draft path and one stale check.
Snapshot and diff still flatten rich text, so a formatting-only change is invisible in effects until they learn runs.

## Alternatives

- **A GDR-specific kind** (`lists/cmPossibility/setDiscussionParagraphs`): rejected; one kind per field and type is
  the opposite of a generic editor.
- **Widen every generated string kind with runs and `expected`, plus a generic paragraph delete:** the same result,
  but a wide change to the generator and to every string kind at once.
- **Reuse `setContents` and paragraph creation:** cannot join or remove paragraphs, loses rich runs, and has no
  starting-text check at Apply.
