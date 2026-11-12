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
