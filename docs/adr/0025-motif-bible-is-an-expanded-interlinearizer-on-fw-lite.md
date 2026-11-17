# ADR 0025 — motif-Bible is an expanded interlinearizer on FW Lite

In Paratext 10 a person works in an interlinearizer and discusses the grammar with Claude. The plugin
shows one proposed grammar update and its effects while the desktop retains the wider linguist tools.

**Status:** Accepted, 2026-11-14. The general Home architecture in ADR 0026 remains Proposed.

## Context

The plugin needs to help people understand and analyze their scripture without duplicating a desktop
workspace. FW Lite and LexBox provide its project/sync direction; scripture remains in Paratext.

## Decisions

### D1. The plugin has one small side panel and inline suggestions

The panel contains a view-only Grammar Guide (a full editor can follow), chapters to work on through a
Context chooser using Paratext states where possible, and one proposed grammar update with Apply/Discard.
Suggestions appear inside the interlinearizer. There is no separate Words/analysis view, Proposals list,
Speed Light, spelling marks or chapter-state screen. People talk to Claude Desktop through MCP; the plugin
shows results. One background Motif process serves plugin, MCP and desktop through the shared catalog
and existing database/job boundaries, without a new public protocol.

### D2. One update is shown, while Claude can explore several Drafts

This limit applies only to motif-Bible. The desktop supports several Proposals and comparing alternatives
under ADR 0001. In the plugin, one Proposal is the person's shown proposed grammar update; Claude may try
several Drafts but only one is brought to Ready. Another person's applied update arrives through sync and
Claude updates the base.
Anyone may Apply: there is no grammar owner or owner-named list permission. Applied Proposals stay on
record. Undoing one is a new Proposal authored by Claude, without a shortcut Undo feature. ADR 0007 owns
the common lifecycle and ADR 0017 the stale-change refusal. Changes whose fields moved are never forced,
including a manually authored inverse Proposal.

### D3. The update card states what changed with verse evidence

Show Claude's summary and three lists: newly parsing words (“N more words parse”), changed approved
analyses before/after with Claude's reason, and words no longer parsing. Link each to verses. Explicitly
state changed approvals rather than letting them masquerade as grammar improvement. The measured evidence
retains [ADR 0021](0021-a-trial-review-report-has-three-levels-and-no-verdict.md)'s limits and no verdict.

A Tidy-up Proposal aligns grammar with the Guide, such as replacing many ad-hoc statements with phonology;
the same card is labelled Tidy-up and zero newly parsing words is normal. Claude should keep the grammar
parsimonious and offer such updates when ad-hoc statements accumulate.

### D4. Every grammar update names a Guide section

Every update carries out or edits the Grammar Guide and cites evidence verses. Claude's workflow refuses
Finalize without that link. The Guide should explain the grammar fairly completely; normally Claude
drafts it with the person retaining the decision. ADR 0005 owns the Guide contract.

### D5. The plugin exposes no diagnostics

Warnings, Parsimony and timing are not shown in the plugin; Claude reads them through MCP and addresses
them before Finalize. Desktop retains those tools, Trial and timing, behind its Diagnostics switch where
applicable. motif-Bible follows the five-touchpoint direction with fewer capabilities; it gains no desktop
capability merely because both use Motif.

### D6. Context is the only chapter state

A chapter is either in Context or not. There are no invented Paratext stages or stage screens; stage
names are added only if Paratext 10 supplies stages. Current chapter follows the scroll-group reference.
A person's word gloss is separate from the analysis and never changes automatically.

### D7. FW Lite is the plugin's project from the start

Use FW Lite/LcmCrdt with Motif Guide, Source, judgement and Proposal types carried in its Harmony log,
syncing through LexBox with stock FW Lite. Testing is local-server only; this decision claims no hosted
deployment proof. The .fwdata Home remains a desktop path. The broader type registration, mapping and
Build design is wholly Proposed in
[ADR 0026](0026-a-project-home-is-fwdata-or-a-harmony-log-everything-else-is-built.md); this accepted plugin
scope does not promote that whole design.

On sync, copy interlinearizer approvals one way as normal human-approved WfiAnalysis/morph bundles into
Motif log records, preserving GUIDs, plus occurrence anchors in per-Source chapter items. Never copy
scripture. The provisional copy disappears only when the interlinearizer team agrees a common shape.
[ADR 0024](0024-sources-are-read-in-place-and-scripture-stays-in-paratext.md) owns Source/Passage/Context,
judgement meaning and anchors. Phase-one spell flagging is omitted.

### D8. Integration proceeds through the interlinearizer hook first

Sequence: suggestion provider/selection-event hook; Motif opening FW Lite, building the parser from its
log and running Trials; background suggestions; update card applying log records; Guide viewer; Context
chooser. The hook and upstream fixes remain local until the owner talks to the interlinearizer team;
only then is a contribution offered. A provider hook without its WebView integration is not a working UI.
This sequence gives Harmony priority for the plugin. It does not revive a separate .fwdata-source plugin
build order; existing desktop support remains useful on its own.

## Open

The interlinearizer team must agree the hook and shared approval/analysis shape before upstream publication
and removal of the one-way copy. This is an integration dependency, not permission to widen the plugin.

## Consequences

The plugin's grammar story, evidence and one update card support interlinear work without a second desktop
interface. The desktop remains capable of production grammar diagnosis. Local integration and sync proofs
do not imply release readiness or agreement from another team.
