# ADR 0028 — The window is one workspace of pages on a fixed grid

**Status:** Accepted, 2026-11-14.

## Context

A project workspace lets a person inspect evidence and work on changes without repeating a wizard. Shared page,
pane and word-presentation rules keep navigation predictable, typography faithful to FieldWorks, and layout work
bounded as words, analyses and window sizes change. The touchpoints and Diagnostics policy belong to
[ADR 0001](0001-people-work-through-five-things-diagnostics-are-behind-a-switch.md).

## Decisions

### D1. Pages share a workspace; review is a view over evidence

Pages are available tasks, not lifecycle stages. Opening a project displays stored evidence. Neither opening nor a
FieldWorks save reruns measurements; stale evidence is identified and the person chooses Refresh or Try Again.
Diagnostic pages and trace details obey the Diagnostics switch in ADR 0001; Progress replaces Matrix.

Review Changes gathers the Dry Run, Trial evidence for affected words and each change's Preflight in one view.
Reading it creates no Decision record, permission or Apply gate. Looked at marks record attention; Readiness is
computed from evidence. The Proposal lifecycle and marks are defined in
[ADR 0007](0007-a-proposal-has-one-lifecycle-a-conversation-citations-and-one-apply-path.md). A change that no
longer fits blocks Apply under [ADR 0017](0017-dry-runs-and-trials-use-a-scratch-copy-and-apply-is-read-back.md);
incomplete-evidence force options never bypass that fit check. The Default Selection and stored summaries follow
[ADR 0023](0023-one-store-beside-each-project-inside-a-byte-budget.md).

### D2. Proposal mode is a lens over the same pages

The desktop holds several open Proposals as window state and supports comparing alternatives: doing one
thing versus another. It publishes an `EvidenceLens` through the existing page evidence hook over today's
evidence set and the selected Proposals' Trial sets. The one-shown-Proposal limit applies only to motif-Bible
under ADR 0025. Each page runs the same query per evidence set; comparing alternatives never blends their
identities or silently treats an unmeasured alternative as measured. A plain-value page may keep showing
today's value. Today, Today → Proposal and Proposal markup are lens properties; there is no second set of
views, Proposal-only page or per-feature Proposal page. Leaving comparison returns to today. Evidence-set
resolution belongs to
[ADR 0018](0018-one-command-catalog-behind-the-cli-window-and-mcp.md) and
[ADR 0020](0020-a-job-produces-assessments-and-scope-decides-comparability.md).

### D3. Window and CLI words address their respective readers

The window speaks to a linguist, the CLI to agents and scripts. Shared commands return the same numbers; labels
differ deliberately. Proposal is a window word; Draft and Preflight need not be exposed as implementation terms.

| Window | CLI or code |
| --- | --- |
| Review Changes | `preflight`, `show`, `trial`, `proposal review`; no separate `review` command |
| Apply to FieldWorks project | `apply --proposal`, `apply --all-pending` at the save boundary |
| N changes not applied yet; Your changes not applied yet | the stored pending-changes Draft Proposal |
| No longer fits | a drifted change; failed per-change Preflight |
| AI Handoff | Handoff |
| Proposals | `proposals` |
| Open a Proposal; Proposal mode | `--proposal` on evidence reads |
| Try Again; Needs Another Try | `trial`; a stale Trial |
| Looked at; Keep | a stored review mark |
| Undo a change | `remove-operations` |

Pending changes survive closing the window in `Project.motif.db`; the displayed change count is not an in-memory
store. Keep and Undo carry the meanings in ADR 0007, never Accept or Reject.

### D4. Text Coverage keeps its measurement meaning

Text Coverage is the share of the Default Selection's distinct words and their occurrences in the chosen Texts
that parse. The corresponding share over a Corpus is grammar coverage. Corpus retains its external-input meaning
and is not renamed. Sources, Passages, Context and Progress follow
[ADR 0024](0024-sources-are-read-in-place-and-scripture-stays-in-paratext.md).

### D5. The shell owns a fixed H frame and pages own pane contents

An Avalonia Grid places the icon rail, optional page side panel, working page and Inspector. A slim title bar and
status bar sit outside the page. Compact component tokens fix column widths. There are no splitters, movable
panes, floating panes, Dock dependency or remembered split layouts.

The rail opens one page at a time. `IWorkspacePaneProvider` supplies a page's side panel, Inspector content and
pinned Inspector footer. The shell owns placement; the page owns controls and meaning. Controls detach on
navigation so each has one visual parent. Pane configuration is neither configurable nor persisted: there is no
layout store or Reset Layout command. Window bounds and display preferences keep their separate contracts.

### D6. Width and temporary choices govern pane visibility

Use zoom-adjusted width. A person may hide or reopen a panel for this session; crossing that panel's threshold
clears the override and reapplies its width rule. Resizing within the range preserves the choice. Both panels fit
only if their fixed widths leave at least 320 DIP for the working page (`Component.Shell.WorkingPageMinWidth`).
Otherwise reopening one temporarily closes the other. If resizing makes an open pair too wide, hide the side
panel without discarding its override; widening within that range restores it.

If rail, Inspector and minimum page cannot fit, explicitly opening the Inspector occupies the working area and
temporarily hides the page, keeping its controls and model attached. **Back to Page** returns to it. Widening
restores the side-by-side arrangement. Navigation reveals the destination while retaining the inspected subject;
the Inspector strip can reopen it. Hiding the Inspector retains its subject and its labelled reopening strip.

A page with only pinned actions retains the shared object Inspector. Pinned actions move below the page when the
Inspector collapses. Closing an object opened from a narrow page restores the preceding pane choices before
focus returns to the origin; an object opened from page details returns to those details and the same footer.
At 640 DIP and 100% zoom the collapsed frame leaves at least 560 DIP for the page. At 150% zoom every control
remains reachable by reflow or scrolling at actual available width.

### D7. One compact scale governs interface chrome

Ordinary UI text is bundled Andika at 13 DIP, page titles 16 DIP, and section headings explicitly semibold.
Spacing uses 2, 4, 6, 8 and 12 DIP steps, with an 8 DIP page inset. Small controls are 26 DIP high and default
controls 28 DIP. Focus, labels and selected states remain visible under pointer and keyboard focus. There is no
second comfortable-density mode. Language text keeps its project typography.

Structural panels have straight edges and no shadows; sections use headings, content and needed dividers.
Editable controls, meaningful selection boundaries and Opinion/outcome cues retain their edges. Popups and
tooltips retain boundaries and raised shadows where needed, constrained to the window at the selected zoom.
Page content is a virtualized list or vertically scrolling body. Navigation, status and pinned actions stay
outside it. Nested scrolling is reserved for an independent viewport, such as a virtualized list.

### D8. Layered tokens map shared purposes to FieldWorks without changing the host

Keep Primitive → Intent → Component names and the zero-tolerance token gate. Primitives name values, Intent shared
meanings and Component local reusable-control roles. Views consume Intent or Component keys. Page chrome uses
shared Intent meanings and page selectors; repeated-item selectors live in the control's theme for locality.
Theme-sensitive brushes, shadows and theme objects are dynamic. Invariant spacing, sizes, strokes, radii and UI
type sizes use static resources. Writing-system sizes and line metrics are resolved data.

The [FieldWorks token map](../ux/fw-token-map.md) lists each mapped Intent/Component key, FieldWorks key and value,
and reasons for differences. Shared light purposes, including selected rows, label ink, field borders and rules,
adopt FieldWorks values where contrast checks pass; failures go in the deviation register. A test pins shared
light values. Never override a Semi key app-wide: Motif-owned themes or resources scoped to the Motif root supply
its control heights, preserving the visuals and the host's defaults. The framework is theme-variant-ready;
fuller dark/high-contrast design follows FieldWorks' net10 move, and hosted Motif follows the host theme.

### D9. One typed word-presentation module owns layout and interaction

Pages choose facts and action meaning. The module owns fitting, reading, selection, copying, keyboard navigation
and accessibility through lookless `WordRow`, `WordStripToken`, `WordCard` and `WordRowHeader`, each with a local
`ControlTheme`. Keep a constrained `ListBox` and `VirtualizingStackPanel`, native `CopyableTextBlock`, separately
tagged form/gloss/category constituents, checkboxes, buttons and named Opinion/outcome/warning/Read controls
where they own names, tips or actions. One parent may draw separators, gutters, underlines and noninteractive
decoration. Do not replace rows with a canvas or TreeDataGrid. Preserve virtualization cache settings; change
`CacheLength` only from native traces after simplifying the row.

Callers supply typed presentation data, list-owned layout, stable identity and typed action requests. Data carries
tagged linguistic strings, derived Opinions and parser outcomes, Read state, changes, warnings, analysis evidence
and optional list notes/time values. Existing projections own meaning and comparison. Presentation never
recomputes agreement, persists a mark, stages an Opinion or starts an Assessment itself. Shared morphology and
mark rendering stay private unless independently needed elsewhere.

Open/checked state binds to host keyed state; hover, focus and text selection stay local. `ListBox` selection is
distinct from the action checkbox. Recycling detaches local state, subscriptions and card work from the previous
identity. Host actions receive word/occurrence and evidence identities, never descendants or concrete host view
models. Late async completions cannot update a replacement row.

The interface accepts no caller templates, raw `object` action slots, part names, selector classes, per-row widths,
line-height adjustments or ancestor walks. Variable cards use typed analyses, warnings, timing, occurrences,
comparison alignment and pending-change sections. Host adapters serve Text occurrences, aggregate words and
pending changes; no generic renderer registry is needed.

### D10. The list resolves columns once and keeps full evidence reachable

One list policy publishes an immutable resolved layout to header and realized rows, once per distinct available
width, schema, style and locale. Reuse it across rows and scrolling. Preserve column order, page column sets and
list-wide hiding of a common Meaning. A row has one shell Grid and at most one frame/focus Border. Track topology
belongs to the schema; row code never rebuilds `ColumnDefinitions`, sets content-derived track minima or writes
`SharedSizeGroup`. Showing a new word cannot redefine the schema.

Word, mark and number tracks are bounded by tokens and measured headings; enabled morphology shares the rest.
Narrow layouts may move next steps below data, never abbreviate FieldWorks/PanGloss or silently hide a requested
semantic column. Below minimum width the list may own one horizontal viewport. Zoom transforms the root once;
column resolution uses the resulting logical width.

Compact morphology clips a preview with an accessible action opening the full card; no per-row scroll viewers.
Full card morphology wraps, retaining constituent links, selection and copy. Large collections use progressive
pages or a bounded detail viewport. Clipping never implies absent evidence. Keep detailed-card labels, grouping
and controls; reduce grouping only when a native trace identifies cost. Every constituent must stay reachable
and copyable, replacing scrollbar-specific assertions in `WordRowOpenLayoutSystemTests`.

Closed rows create no cards and request no card evidence. Opening requests evidence once per identity/revision
and shows loading/failure. Closing detaches visuals and cancels cancellable work; the host may retain loaded
evidence separately. Hidden semantic cells are absent, not instantiated and hidden. Next-step buttons remain
keyboard-reachable with existing reveal behavior; they may change height, never column width.

### D11. Layout dependencies run from outer width to row height

The list gets finite width from its constrained parent and permanently reserves its vertical-scrollbar gutter;
Settings keeps its reserved sidebar gutter. Subtract chrome and gaps once, identically for headers and rows. At
supported widths disable automatic horizontal scrolling. Compact cells report no desired width beyond assigned
tracks. A below-minimum horizontal mode depends on outer width and the fixed schema minimum, never open-row
extent. Cards, actions and arriving data may change height, never gutter or schema.

The dependency is outer constraint → list tracks → row/cell arrangement → row height. Create tracks before row
measurement, and update assigned numeric widths once per width revision. The owner may resolve its parent
constraint at the beginning of measure. Row measure/arrange never calls back to rewrite columns. Identical inputs
publish no revision and cause no invalidation. Wrapping metrics depend on style and assigned width and never feed
track widths. Neither a list-level `LayoutUpdated` loop nor a larger iteration allowance is an acceptable fix.

Checks observe actual root measure/arrange calls and warnings at 1040/1240 widths, 100/125/150% root zoom, both
themes, long/combining/RTL text, card opening/closing, action reveal, filters, scrolling, recycling and scrollbar
thresholds. Each distinct resize input publishes at most one layout; unchanged settled inputs publish none and
mutate no columns. Require zero `Layout cycle detected` warnings. Windows behavior requires a successful Windows
result under [ADR 0030](0030-all-automated-testing-is-headless.md); WSL settlement does not prove native scrollbar
behavior, which requires manual verification when only the real OS can show it.

### D12. One text-style seam resolves project typography and actual line metrics

Resolve role, writing-system id, exact FieldWorks style name, UI locale and weight/emphasis against captured
display inventory and UI font policy. Return family/fallback, features, direction, DIP size and line metrics to
native selectable-text/editor adapters and word controls. Consume
[ADR 0016](0016-sil-icu-and-writing-system-display-follow-fieldworks.md) and the
[writing-system display contract](../writing-system-display-contract.md); do not reread LDML or rebuild stylesheet
inheritance. Convert linguistic points by `96 / 72` without clamping or rescaling. Render only valid OpenType
features while preserving stored strings verbatim. Untagged/composed strings gain no invented vernacular id.
Each constituent keeps its direction; UI follows locale, and running Text follows its own writing system.

Reuse bundled Andika, UI language fallbacks and font collection; one owner initializes UI fonts and the four
Semi/application resource keys. Never prepend Andika to a project's requested language family. Locale/Baseline
changes publish a style revision; subscribers update and detach on disposal.

Use actual shaped runs, including fallback glyphs and combining marks, for ascent, descent and leading without
shrinking requested sizes. Reuse the text adapter's rendering layout rather than shape twice. Shared local
form/gloss/category tracks take the maximum visible-run metrics within that row, card or Text line; no list-wide
tallest-word height. `RunningTextPanel` consumes explicit metrics, without descendant searches and repair measures.

Missing-font notices register when an explicit text adapter first draws nonempty text in a visible top-level
surface, then publish outside rendering. Only those adapters participate: no global `Control.IsVisible` class
handler, window subtree scan or process-static control registry. Notice identity is project-display revision plus
writing system; hide/show and recycling cannot duplicate it.

### D13. Diagnostics measure actual work through the public presentation seam

Provide read-only snapshots of realized visual/control/panel counts, maximum depth, card/cell realization, root
measure/arrange calls, column mutations, schema fingerprint, reserved chrome and layout revision/recomputation
counts. Census runs on request after layout, never every render or scroll. Counters include actual work of
retired/recycled rows within the observation interval; incrementing once per requested action is not measurement.

Count roots and all Avalonia/Semi template descendants, including hidden instantiated controls. Report Motif-owned
counts separately, excluding only enclosing list/window chrome. Count cards separately and also within open-row
totals. Reproduce the research fixtures before setting ceilings: row 111 controls/depth 22; minimal card 63 total
visuals/depth 18. The reported 55 card descendants are a different category; establish its Control count rather
than equate them. Also report shaped-text layouts and automation peers so fewer Controls cannot hide work.

Target ceilings, not measured results, are 64 controls/depth 10 for a closed comparison row with three morphemes
per producer, 40/depth 9 for a three-morpheme strip token, and 64 controls/depth 18 for the minimal card while
retaining its detailed grouping. Expanded cards have additive budgets for realized sections, readings, morphemes
and actions, not a fixed cap on arbitrary evidence. Never remove behavior or automation to meet a count. Pin
fixtures, framework and fonts; ratchet only after the implementation passes. Reduce card ceilings only with
measured justification.

Tests use the caller seam: typed data/layout/actions, snapshots, events and automation peers. Replace private
part-name/class/subtree assertions with semantic geometry and automation patterns; preserve comparison/projection
tests. Remove `TestFontScale` only when production-metric coverage replaces it. Keep deliberate large-font and
zoom checks through supported inputs.

## Proposed

On a settled constrained list, one viewport jump should leave the column revision unchanged, recompute no columns,
load no cards, and cause at most two root measures and two root arrangements per affected realized row. Unchanged
retained rows outside the step should have zero root measures. These deterministic limits require a first
instrumented run before becoming gates. Timings and native CPU/GC/layout traces are reported evidence, not tight
shared-runner gates; structural counts do not establish desktop latency.

## Consequences

One layout and text seam lets fixes reach all callers without duplicating pages or overriding language typography.
Each caller migration replaces its previous path without permanent compatibility layers. Review virtualization
and lazy Text reads remain coordinated work; this design requires no new store generation, CLI verb or Released
command. Preserve language, marks and actions unless a deliberate visual change records its reason. Local
`ControlTheme` use alone is not evidence of speed. A future narrow drawing experiment requires a
native trace showing remaining rendering cost and full behavior parity before revisiting native controls.

`HFrameTests`, `PaneWidthBudgetTests` and `FocusedInspectorFrameTests` cover geometry, thresholds, reopening and
return behavior. `DesignTokenTests`, `ComponentStyleTests` and `InteractionCueTests` cover density and cues.
Final-source UI/control changes run Release `-All` with the pinned PanGloss and report source SHA, parser
path/version, skipped categories and Windows follow-up. Screenshots and capture states are required only when a
person requests captures, per ADR 0030. Native Windows and macOS interaction traces are needed before claiming
desktop performance; such native checks are manual. This ADR claims neither a performance gain nor completed
visual validation.
