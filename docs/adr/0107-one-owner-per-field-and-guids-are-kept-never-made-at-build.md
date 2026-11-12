# 0107. One owner per field; GUIDs are kept, never made by a Build

**In plain terms:** Every piece of project data is written in exactly one place. Converting a FieldWorks project to
FW Lite's history, or building it back, keeps every object's identity, so a link to an allomorph or an analysis still
points at the same thing afterwards.

**Status:** proposed, 2026-11-10. The owner agreed in a grill on 2026-11-10 (Q5, Q6, Q6b). The GUID rule changes
behaviour LexBox and FieldWorks rely on and must be agreed with them. Evidence: [q4-q6-mapping.md](../source-build/q4-q6-mapping.md).

## Context

FW Lite collapses LCM:
- `Entry.LexemeForm` and `Entry.MorphType` live on the lexeme-form allomorph (`LexemeFormOA`);
- `Sense.PartOfSpeech` is read and written through the sense's MSA.

Today's sync creates the allomorph, the MSA and the complex-form `LexEntryRef` in .fwdata with random GUIDs, and finds
them again through their owner. An fwdata → FW Lite import drops those GUIDs, yet grammar, interlinear analyses and the
interlinearizer's allomorph and MSA references all point at them.

## Decision

1. **One owner per field.**
   - FW Lite's types own what FW Lite models, even collapsed: lexeme-form text, morph type, sense POS, glosses and definitions.
   - Motif's generated types (ADR 0102) own everything else and never hold a copy of an FW Lite field. For example,
     the lexeme-form allomorph object has its GUID, environments and stem name, but no Form.
2. **One path each way.**
   - A Build to .fwdata runs FW Lite's own `FwDataMiniLcmBridge` for the lexical part plus a generated bridge for the
     grammar part, into a new .fwdata. The missing log → blank .fwdata path is built from `IProjectLoader.NewProject`.
   - Export to the log runs the same two in reverse.
   - Proposal Lowering emits FW Lite's change types for FW Lite's fields and Motif's for the rest, so an applied
     Proposal is indistinguishable from an edit.
3. **GUIDs are kept.**
   - Export carries every LCM GUID, including allomorphs, MSAs and `LexEntryRef`s, as real objects.
   - An entry created in FW Lite gets its missing companions from a one-time **complete** change, committed like any
     instant edit, with GUIDs derived deterministically (UUID v5 from the entry GUID and the role). Two computers
     completing the same entry make the same objects, and Harmony treats re-creating a live object as a no-op.
   - A Build never mints a GUID, so every rebuild gives the same identities.
4. **A sense whose POS changes** gets an MSA for its new POS through the same deterministic rule, which matches what
   FieldWorks does when a sense's MSA is shared. An MSA that lacks grammar is underdefined grammar, which FieldWorks
   already allows. It is an ordinary Motif Warning only where an existing grammar check says so; the Build adds
   nothing of its own.

## Amendment, 2026-11-12: proven by the format spike

[spike-format.md](../source-build/spike-format.md) proves decisions 2 and 3: two clients completing one entry make one
allomorph and one MSA; a Build to a new .fwdata keeps every GUID from the log; two Builds give identical GUIDs; and
PanGloss parses the built .fwdata (an environment change in the log changes the parse after a rebuild). It also found:

1. **Re-creating is a no-op only for a live object.** A re-sent create revives a deleted object and wipes its fields.
   A deterministic "complete" create must not revive a tombstone; Motif's types return the deleted object unchanged.
2. **FW Lite's bridge mints random GUIDs** for the lexeme-form allomorph and for the stem MSA of a sense with a POS. The
   Build replaces them with the log's GUIDs right after creation, reading the old allomorph's Form and MorphType first.
   LibLCM deletes the orphaned MSA itself. Every LibLCM factory has `Create(Guid)`, so an upstream bridge change that
   takes GUIDs would remove the replacement step.
3. **Export (.fwdata → log) is not proven yet.**

## Amendment, 2026-11-12: export proven on two real projects

[spike-export.md](../source-build/spike-export.md) exported .fwdata → log → new .fwdata.

**Indonesian:**
- Every grammar and lexical GUID is kept.
- 100% of PanGloss parse sets are identical (70 words).
- Two exports give identical rows.

**Cebuano (24,856 entries):**
- Timings: FW Lite import 2m35s; Motif export 1m17s (120,886 rows, 410 MB log); Build 9 s for the lexical part and 7m02s for the grammar part, object by object through LibLCM.
- GUIDs match for every class except two:
  - **LexEtymology** (418 objects): FW Lite does not carry it, so ADR 0107 decision 1 needs an owner for it.
  - Objects the blank project template creates itself (one PartOfSpeech, semantic domains, some lists).
- Parses: **96.8%** of 1,999 words are identical once variant `LexEntryRef`s are exported (91.7% without them). Of the 64 words that differ:
  - 47 have extra parses in the rebuilt file;
  - 30 lost a parse;
  - 1 involves a MoAffixProcess.
  Not yet root-caused.
- Determinism across two exports is not yet confirmed.

**What ADR 0107 missed:**
- The blank template mints its own GUIDs. A Build must start from an empty project or overwrite the template's objects with the log's.
- Classes FW Lite drops, such as etymologies, need a Motif type.
