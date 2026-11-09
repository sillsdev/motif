# 0097. A Text Proposal can replace the Notes of existing segments, one operation per segment

A linguist who has to change many segment Notes in a Text that already exists, such as turning placeholder
dispositions into real ones across thousands of words, should do it in one Proposal that goes through the ordinary
Draft, Dry Run, Trial and Apply flow, without replacing the Text.

**Status:** accepted, 2026-11-09. Extends [ADR 0017](0017-text-and-analysis-destination-scope.md): Text operations so far
only created whole Texts. Issue #104.

## Decision

1. **`texts/segment/setNotes`** targets one existing Segment. Its `after` holds the writing system, the Note
   (`id`, `text`) and `expected`, the Segment's current Note texts in that writing system. Apply stops if `expected` no
   longer matches. One operation edits one Segment, so a bulk edit is many operations in one Draft.
2. **Replace, not append.** The Segment ends with exactly one Note. The first existing Note keeps its identity and has
   its text in the writing system replaced; any further Notes are removed; with no Note, one is created with the given id.
   This is the smallest rule that covers the use (a placeholder becomes a disposition) and keeps a Note's id stable.
3. **Addressing** is by Text (name or id) and then a segment number counted across the Text or a word that occurs in
   exactly one segment; the composer resolves both to the Segment id at compose time. The composer is
   `compose-edit-text-notes`, CLI-first; a TSV (`Text`, segment or word, `Note`) is read by the CLI into the same intent.

## Consequences

Free and Literal Translations, baseline, words and Note identities of untouched Segments do not change, so a Trial
on the Text is unaffected. Appending a Note, editing several Notes of one Segment and Note-level removal are not
covered; a later operation can add them.

## Alternatives

- **A Note-level operation** (set one Note by id): more operations for the same one-Note case.
- **A replacement Text:** loses identities and needs the old Texts deleted.
- **Widen `text/setParagraphs` of ADR 0095:** that edits paragraph runs; Notes belong to Segments, not paragraphs.
