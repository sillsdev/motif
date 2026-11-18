# ADR 0014 — Text, analysis and writing-system changes are Proposal operations

**Status:** Accepted, 2026-11-14.

**Amended 2026-11-17:** Hand-analysis annotation and its full-fidelity Home boundary.

## Context

Text, staged analysis actions and writing-system choices affect the grammar work a linguist can test. They belong in
the Proposal workflow; hand analysis is the immediate annotation exception below. Durable project objects,
occurrence positions and separately saved LDML have distinct
failure modes. The existing object identity and effect model can describe them without inventing nonobject ids.

## Decisions

### D1. Address project objects and their fields

Text, StText, StTxtPara, Segment, WfiWordform and WfiAnalysis are GUID-bearing objects. An occurrence is a
convenience wrapper over Segment and index; an edit targets the Segment's field, with position inside its value.
No second target space or wider effect tuple is needed. Text creation and later field families are additive.
Kinds remain stable machine identifiers under
[ADR 0013](0013-the-operation-catalog-is-generated-from-the-liblcm-model.md), with text's shape considered in their design.

Global human approval (`WfiAnalysis.Evaluations`, an unordered collection) and assignment to a position
(`Segment.Analyses`, a sequence) are independent facts. The durable analysis/approval slice is available without
claiming robust occurrence assignment across arbitrary text edits. Wordform analyses are an unordered collection;
engine state and unclassified parser derivation output do not become authorable merely by being nearby fields.

### D2. Segment lifecycle is a named drift class

A text edit can split, merge or destroy an addressed Segment. LibLCM's AnalysisAdjuster preserves untouched
segments and their analyses, adjusting offsets; overlapping edits still require a footprint check and may refuse.
This is a bounded review/UI drift class, not a reason to redesign canonical identity. Text import and global
analysis approval need not wait for a durable occurrence anchor. Present unexplained words as a work queue,
never a grade; feature-coverage claims require their own retained identities and evidence.

### D3. Writing-system edits use one Proposal family

`writingSystems/langProject/setCharacterClass` targets the project root. Its closed `after` is
`{ "ws": tag, "class": "wordForming"|"punctuation", "characters": [...] }`. Each item is one visible code point;
whitespace/control characters and writing systems outside the project are refused. The slot is
`(project, writingSystems/langProject/characterClass, "<ws>/U+XXXX")`, so distinct characters are independent and
two writes to the same character conflict.

Word-forming adds the character to LDML `main` and removes it from `punctuation`; punctuation does the reverse.
Remove emptied sets. A Unicode letter, mark or modifier cannot be made punctuation by these lists and is refused
both at compose and Apply. `punctuation` is the standard LDML exemplar set, not a second SIL extension or state
store. Collation, fonts, keyboards and other valid-character sets are outside this operation's scope.

The effect field is `writingSystems/langProject/characterClass`. Before/after values record, by WS, each
`U+XXXX=<effective class>/<listing>`, where listing is main, punctuation, main+punctuation or unlisted. Read back
`get_IsWordForming`; do not infer it from the list alone. The before value binds the footprint and different
writing-system state refuses Apply.

### D4. Retokenize only paragraphs affected by an effective class change

Inside the same Proposal unit of work, force-parse each paragraph with a run in the changed writing system that
contains an effectively changed character. Merely listing an already-punctuation character parses nothing.
Scratch and live Apply use the same rule. Keep orphan wordforms as FieldWorks does; a Trial over all wordforms
still includes them, while callers wanting only selected new words can use `--words`.

### D5. Save writing systems with the project; isolate scratch disposal

A Dry Run uses a file-backed copy and mutates/reads the copy's writing system. A Trial saves through
`FwDataProjectLoader.Save`, saving the writing-system manager before backend commit and the disk-completion wait,
so exported candidates contain changed LDML and reparsed Texts. Scratch disposal forgets unsaved writing-system
changes before disposing the cache: LibLCM disposal must never rewrite an immutable published Baseline.

Writing-system changes are outside the LibLCM data unit of work. A failed unsaved Apply recovers by reload, but a
save failure after LDML was written may leave it ahead of `.fwdata`. Report `NeedsReconciliationException` and
inspect both stores before continuing. Reload alone is not proof of recovery after that partial save.
[ADR 0017](0017-dry-runs-and-trials-use-a-scratch-copy-and-apply-is-read-back.md) owns the common scratch/Apply rules.

### D6. Writing-system authoring and extension stay within the normal workflow

`compose-set-character-class` stages a Draft; Dry Run, Trial and Apply do the rest. The advanced AI Draft tool is
`motif_set_character_class`; it never Applies. Record intent/rationale as an operation and effects in the Applied
Log, with no second writing-system store. `project word-forming` remains the direct HumanOnly setup command for
adding word-forming characters, now using the same save path. Only a Proposal moves a character back to punctuation.

A future writing-system field is a sibling kind with the same project target, a WS slot discriminator and its own
effect field. This does not enable unspecified fields. `WritingSystemCharacterSets` is shared only for the two
exemplar sets. Deleting orphan wordforms or moving the setup command into a Proposal requires a separate decision.

### D7. One Notes operation replaces the Notes of one existing Segment

`texts/segment/setNotes` carries WS, a Note id/text and `expected`: each current Note, in order, with its nonempty
text in every writing system. Dry Run and Apply refuse any mismatch as `text-notes.stale-source`. The first Note
keeps its identity and other-WS text while its selected-WS text is replaced; further Notes and all their text are
removed. If no Note exists, create one with the supplied id. The result has exactly one Note.

`compose-edit-text-notes` resolves a Text name/id and a Text-wide segment number or a word occurring in exactly
one segment to the Segment id at compose time. Bulk work is many operations in one Draft. Optional `at` labels
only identify refusals. CLI TSV input may start with the exact header `Text<TAB>Locator<TAB>Note`; require NFD
cells without edge spaces and report all bad rows/edits together.

Free/Literal Translations, baseline text, words and untouched Segment Note identities remain intact. The composer
sets nonsemantic `extensions.trialWords` to the edited segments' NFD wordforms. A non-grammar-only Trial measures
those words; a mixed grammar Proposal or an operation without that extension uses the Default Selection.
`trialWords` affects scope only, never Apply readiness or Proposal overlap. Appending, editing multiple Notes in
one Segment and Note-level removal are not supplied by this replace operation.

### D8. Hand analysis is immediate annotation

A speaker's intended reading can be recorded at once, even when the grammar cannot explain it. Recording
that reading also supplies a test for later grammar changes.

Hand analysis is a bounded non-Proposal write: ordered morph bundles, word category and glosses, one
Occurrence choice, native approval, and explicit lexical creations from its choosers. Each confirmed
chooser action is one transaction; recording an analysis is one transaction. It requires neither Draft,
Finalize, Dry Run, Trial, Readiness nor Apply. Structural validity, exact identities, current-source guards,
exclusive mutation, read-back, persistence verification and audit remain mandatory. Unlinked pieces and
underdefined grammatical info are valid; parser inability cannot block approval. This permits no rule,
environment, template, slot, existing grammar-feature or Guide edit.

Native Morph, Sense, Msa and InflType links are independently nullable, subject to class, ownership and
variant checks. Each Piece has a request-local `pieceKey` for correlation and optional `variantRef`
provenance; the key is not a native bundle GUID. Typed marker defaults use project list order and
FieldWorks' stem, bound-stem, plain-affix and phrase disambiguation. Unknown markers retain their spelling,
warn and use the project's stem GUID even for a requested lexical creation. Diagnostic candidates do
not require another decision before approval.

Add New Sense always creates a sense; only equal grammatical info may be reused. A new variant is its
own creation case with entry, form and reference, requiring no invented sense or MSA. `LexiconPolicy`
defaults to `AnalysisOnly`; `SyncSinglePieceGlossAndCategory` enables only the single-piece approval's
ordered lexicon synchronization: reuse an equal sense, fill its missing category, edit the Occurrence's
exclusive single-piece sense, add a sense on a matching entry, or create entry/form/sense. Those guarded
choices belong inside the same annotation transaction, never a general grammar writer.

## Consequences

Text and writing-system changes gain the same review and evidence path as grammar changes. Stable Segment targets
avoid a new identity system, while lifecycle and all-WS guards still expose drift. A character-class Apply costs a
pass through affected paragraphs and has a distinct LDML save/recovery boundary that must remain visible.
