# Why Harmony/CRDT was rejected or demoted in Plan A

Source: `git show origin/main:<path>` in /mnt/h/repos/motif. Read-only. Line numbers are from those files.

## Answer

Plan A (2026-08-01) demoted Harmony/CRDT for technical reasons: a runtime the data owner cannot load, no effect model or dry run, no proposal layer, dual authority, and grammar features (feeding order, cross-owner moves) that depend on unbuilt Harmony work. The owner supplied the framing and the trigger. Plan A itself reversed ADR 0013 by targeting LibLCM objects instead (ADR 0014 :5-12).

## Reasons for the demotion (harmony-adoption-report.md, 2026-08-01)

1. Runtime: Harmony/LcmCrdt is net10 with EF Core; LibLCM is net48. The data's owner cannot load it. `docs/harmony-adoption-report.md:13-20, 89-91`.
2. No effect model, no dry run, no proposals. `:89-110`.
3. Dual authority: a CRDT store and `LcmCache`/`.fwdata` both written. `:19`.
4. Grammar residue needs HAR-3, HAR-5, HAR-6, none with a branch or issue. `:111-112`.
5. Applied log sits inside `.fwdata` (`LexDb.Resources`); Chorus merges it blindly. `:58, :265-275`.
6. Estimated 20-50 engineer-years, with lowering still needed and the crosswalk resurrected. `:89-110` (exact sub-line not isolated).

The recommendation (proposal 2) is at `:165-188`. The HAR items are described as "necessitated by the original plan" at `:178-184`.

## Named technical blockers

- **Order as LWW scalar.** `SetOrderChange` double-Order LWW with fractional-value collision: `docs/harmony-additions-needed.md:81-84`. Scope: 2 `feeding` fields of 473, `:86-87`.
- **Feeding/bleeding.** Phonological rule order encodes meaning: `docs/declarative-commands-vs-crdt.md:97-101`; `docs/harmony-additions-needed.md:86-92`.
- **Alpha variables / index-as-identity.** `docs/declarative-commands-vs-crdt.md:103-111`; `docs/harmony-additions-needed.md:90-95`. `JsonPatchValidator` rejects index paths: `declarative-commands-vs-crdt.md:107-111`, `docs/plan-lcmcrdt.md:23`.
- **MoAffixProcess Output/Input position.** Named only: `docs/adr/0013-harmony-is-the-change-mechanism.md:84-86`. No mechanism detail found in the files read. Unverified beyond the name.
- **Missing grammar classes / crosswalk.** Crosswalk "is required and does not yet exist": `docs/adr/0014-generate-the-crdt-layer-from-masterlcmodel.md:91-97`. Gate covers zero `feeding` rows: `:125-129`.
- **Snapshot apply.** One bad change poisons every replay (`AddRangeFromSync`): `docs/harmony-additions-needed.md:118-148`.
- **Sync / materialization.** CRDT to full `.fwdata` is "large" and sits in the application layer, not Harmony: `docs/harmony-additions-needed.md:176-186`. Content-integrity gap: `docs/declarative-commands-vs-crdt.md:115-117`.
- **Ownership.** 40 commits, one author, no CI: `docs/adr/0013-harmony-is-the-change-mechanism.md:101-104`.
- **Diagnostics.** Sync layer returns counts and job-level enums, not per-change problems: `docs/inventory-harmony-conflict-reporting.md:252-254, 282`. This is an observability gap, not a stated reason for demotion.

## Owner decisions vs technical findings

Owner:
- Framing that ADR 0013 accepts: `docs/adr/0013-harmony-is-the-change-mechanism.md:25-30`.
- Trigger: "look at Harmony — we should use that," `docs/harmony-adoption-report.md:3`. This started the analysis; it is not a rejection.
- Lexeme-first start, `docs/grill-plan-a.md:139`.
- Chorus deferred: "We don't care about Chorus right now," `docs/grill-plan-a.md:394-398`. Dated 2026-08-05, after the demotion.
- Desktop-only ICU (D17), `docs/grill-plan-a.md:354-357`. Recorded as a product decision.

Technical: everything in the two sections above.

Unverified: no file read records owner sign-off on proposal 2.

## What the files say about .fwdata and CRDT authority

- ADR 0013 makes the CRDT store "a first-class source of truth, not a cache in front of LibLCM": `docs/adr/0013-harmony-is-the-change-mechanism.md:52-54`.
- Plan A reconciles CRDT state back to `.fwdata`: `docs/plan-lcmcrdt.md:9-12`.
- `harmony-additions-needed.md:176-186` treats full `.fwdata` as an export produced from CRDT state.
- No file found calls `.fwdata` "derived" or "compiled." Under Plan A, `.fwdata`/LibLCM is the authority and the CRDT layer is a projection. This is my inference from ADR 0014 and `plan-lcmcrdt.md`, not a stated position.

## Open questions and unverified items

- Whether ADR 0013 is formally superseded. Grep was truncated (`head -50`). No supersession marker found in the lines read. ADR 0015 says ADR 0013 "withdrew" something (`docs/adr/0015*.md:15, 24, 42`, from grep).
- Author of the adoption report is not stated in the file.
- Not read: `docs/grill-plan-a.md:430-961`, `docs/plan-motif.md`, `docs/adr/0028-*.md`.
- MoAffixProcess mechanism details unverified.
