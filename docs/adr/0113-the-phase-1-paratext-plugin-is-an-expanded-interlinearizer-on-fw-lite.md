# 0113. The phase-1 Paratext plugin is an expanded interlinearizer on FW Lite

**In plain terms:** In Paratext 10, a person sees an interlinearizer. When they and Claude draft grammar design records
(GDRs) together, more words parse, and making an interlinear gloss or analysis gets easier. They never think about the
parser: they work with Claude on how the grammar works, and the plugin shows what changed and lets them Apply it.

**Status:** accepted, 2026-11-13 (owner, decisions 62-84 in the Phase 1 personas record).

## Context

The Phase 1 flow is Motif beside Paratext 10 ([ADR 0100](0100-motif-has-two-stages-grow-and-polish.md), Grow). Earlier
plans gave the plugin a Words view, a Proposals list, Speed Light, chapter states and a grammar owner
([ADR 0110](0110-scripture-stays-in-paratext-and-judgements-ride-on-wfiwordform.md)). On 2026-11-13 the owner cut the
plugin to an expanded interlinearizer. Motif itself, the desktop linguist's tool, is not cut. The Home may be a
.fwdata or a Harmony log ([ADR 0101](0101-a-project-home-is-fwdata-or-a-harmony-log-everything-else-is-built.md)).
Four research reports shaped this: Paratext 10 has no chapter stages (`pt10-stages.md`); the interlinearizer has no
hook for another extension's suggestions (`interlinearizer-extension-points.md`); a local hook was built
(`interlinearizer-hook.md`); and an undo built from a Receipt works for fields but not for every delete
(`spike-undo-apply.md`, local branch `bible/spike-undo`, unlanded).

## Decision

1. **The plugin's parts.** A side panel beside the interlinearizer with three things: grammar notes (GDRs, view only for
   now, a full GDR editor later), chapters to work on (the Context chooser, taken from Paratext's own states if
   possible), and the one proposed grammar update, with Apply and Discard. Motif's suggestions appear inside the
   interlinearizer. The plugin has no other per-word analysis view, no Words view, no Proposals list, no Speed Light,
   no spelling marks and no chapter-state screens. People talk to Claude in Claude Desktop (MCP); the plugin only shows
   results. One background Motif process serves the plugin, MCP and the desktop.
2. **One shown update per person.** One Proposal is shown as the "proposed grammar update". Claude may build several
   Drafts while trying things with the person ("does it work better this way or that way?"), but only one is shown and
   only one is brought out of Draft to Ready. Another person's applied update arrives by sync; Claude rebases.
3. **The card's three lists.** The card shows Claude's summary and groups words into newly parsing ("N more words
   parse"), changed (each approved analysis before and after, with Claude's reason, including "these ones that were not
   approved are now approved"), and no longer parsing, each with links to verses. Claude may change analyses, but says
   so. That is how a person asks "this one fails now, what should we do?". Claude must keep the grammar parsimonious.
4. **Tidy-up updates.** Some Proposals only align the grammar with the GDRs, for example many ad-hoc rules becoming
   phonology. They use the same card, labelled "Tidy-up"; "0 more words parse" is normal. Claude offers one when
   ad-hoc rules pile up.
5. **Every update names a GDR.** Each proposed grammar update names the GDR it carries out, or edits one, with evidence
   verses. Claude's skill refuses to finalize one without. The GDRs should tell the grammar fairly completely. The
   person is in the driver's seat, but normally Claude drafts the GDRs; evidence and the story are central.
6. **No diagnostics for the person.** Warnings, Parsimony and timing are not shown in the plugin. Claude sees them
   through MCP and fixes them before finalizing the Proposal. Motif desktop keeps Warnings, Parsimony, Trial and timing.
7. **Anyone may Apply.** There is no grammar owner and no restriction on who applies a Proposal. This removes the
   grammar owner from earlier decisions 4, 23, 25, 53, 57 and 58. The "Motif Proposals" list names no owner.
8. **No Undo feature.** Applied Proposals stay on record. If a person asks Claude to undo one, Claude does it by hand as
   a new Proposal. There is no short path for now. The undo spike stays unlanded as reference.
9. **Stale changes are refused.** A change whose fields moved since it was drafted is refused, never forced. This
   includes Claude's hand-made undo.
10. **Chapter state is in Context or not.** The plugin's only chapter state is whether a chapter is in Context. There
    are no stages: Paratext 10 has none. Paratext stage names are added only if Paratext 10 adds stages. The current
    chapter still comes from Paratext's scroll-group reference. A word gloss is a person's edit, separate from the
    analysis, and does not change on its own.
11. **The interlinearizer hook on a local branch.** Motif's suggestions reach the interlinearizer through a suggestion
    provider and a selection event, added on a local branch of sillsdev/interlinearizer-extension. Nothing is posted
    until the owner talks to the interlinearizer team; later it is offered as a PR. Upstream fixes stay local.
12. **FW Lite first-class, with LexBox sync and stock FW Lite.** The plugin runs on an FW Lite (LcmCrdt) project from the
    start. GDRs, Sources, judgements and Proposals are Motif types in the log, synced through Harmony
    ([ADR 0102](0102-grammar-classes-are-motif-registered-harmony-types-that-ride-lexbox-opaquely.md)). Phase 1 syncs
    through LexBox with stock FW Lite, tested only against a local server. The .fwdata Home stays for the desktop.
13. **Approvals copied one way.** On each sync, each interlinearizer approval is copied into the FW Lite project as a
    normal FieldWorks approval: a human-approved WfiAnalysis with its morph bundles, held as Motif types in the log
    ([ADR 0107](0107-one-owner-per-field-and-guids-are-kept-never-made-at-build.md) keeps every GUID), plus the
    occurrence anchor (`MAT 5:3#2`) in the per-chapter items. Never the scripture text. The copy goes away once the
    interlinearizer team agrees one shape. This applies ADR 0110 decisions 5-12 to an FW Lite Home.
14. **Spell flagging is dropped** from phase 1. Adding it later is trivial and needs no Motif or PanGloss change.
15. **Desktop is not nerfed.** Only the plugin is cut. Motif keeps its linguist tools for production grammars.
16. **The build order.** (1) The interlinearizer hook (local branch). (2) Motif opens an FW Lite project, builds PanGloss
    from the log, and runs Trials (PR #149's read-side slice). (3) The background service feeds suggestions into the
    interlinearizer. (4) The proposed grammar update card, with Apply writing Motif types into the log. (5) The GDR
    viewer. (6) The in-context chooser. The .fwdata Sources slices W1-W4 are paused; S1-S4 and W0 stay on main for the
    desktop.

## Consequences

- **Supersedes, for phase 1:** ADR 0110 steps 5-7 (the Words view, the Proposals list, and the Harmony-log step 7 that
  now comes first in the build order above), its decision 11 (the Words view), and its .fwdata judgement storage
  (decisions 4-5, which stays for the .fwdata Home). Phase-1 decisions 36-39 apply to an FW Lite Home through decision 13.
- **Supersedes** the grammar-owner parts of earlier phase-1 decisions (4, 23, 25, 53, 57, 58) and persona review items 3-15.
- Everything else in ADR 0110 stands: scripture stays in Paratext, the Source, Passage and Context terms, and the anchor format.
- The interlinearizer team's agreement is needed before the hook is offered as a PR and before the one-way copy can go
  away. Four questions for them are in `docs/source-build/interlinearizer-meeting.md` (items 9-12).
- motif-cli is told to pause W1-W4; motif-app is told the plugin has no Words view or Proposals list.
- The hook is not yet wired in the interlinearizer's WebView, so nothing new shows in its UI until that half is built.

## Alternatives

- **A Words view and a Proposals list in the plugin.** More to build and teach; the owner chose one shown update.
- **A grammar owner who alone applies.** Rejected: anyone may Apply, and the stale check catches a second Apply.
- **An Undo feature built from the Receipt.** Works for fields, not for every delete; Claude's hand-made inverse
  Proposal is enough for now.
- **Chapter stages in the plugin.** Paratext 10 has none to read or set.
