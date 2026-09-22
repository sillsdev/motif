# Try a Word Rich Diagnostic View

## Delivered implementation

The rich diagnostic view is implemented against the shared PanGloss `pangloss.trace-details.v2` reader. Motif keeps the producer JSON as the retained diagnostic document and projects recorded analyses, morphs, outcomes, effort counters, trace order, contextual failure fields, writing-system metadata, and provenance for display. Filters operate on view models only; copy and save use the retained document.

The App surface now has one reusable `DiagnosticPanel`, hosted below the persistent Try-a-Word input in `AssessPanel` and reused by the project-independent `DiagnosticWindow`. The main project menu exposes Open diagnostic before a project is selected. A loaded document is readable without a project, Baseline, or parser executable, and file/schema errors are shown in the diagnostic surface.

The panel presents recorded analyses first, then collapsed failed attempts and the full source-ordered derivation tree. Morph cards show form, headword, gloss, category, writing-system direction/font, identity, slot, inflection class, features, guessed text, named MSA fields, and retained raw details. Selected steps show input/output, rule/subrule, outcome, attempted morphs, source identity kind/ID/quality, and required/actual/environment failure context. Aggregate Work, Uses, Tried, Outputs, misses, and nullable Time are shown in a separate effort table and are labelled as aggregate statistics.

Search inspects step text and descendant morph fields even when descendants are collapsed. Outcome, rule, and morph filters preserve matching ancestors, expand matching paths, and report hidden counts. Incomplete search remains prominent beside a successful result. Status text distinguishes succeeded, failed, and recorded attempts. Disclosure controls are keyboard accessible, and narrow panels stack the selected detail pane below the tree. Captured writing-system direction and available fonts are applied per text value with a UI fallback.

Live FieldWorks links are separate from recorded internal details. They are enabled only for verified `CanNavigate` provenance and host-resolved `silfw:` links. Standalone or mismatched documents remain fully readable with navigation unavailable.

## Explicit implementation decisions and deviations

- Native Avalonia storage pickers and clipboard calls are handled directly by the view code in `DiagnosticPanel` and `MainWindow`; no new `AvaloniaStoragePickers` adapter was introduced.
- The shared panel is hosted in the existing assessment layout and a separate diagnostic window rather than being a second project-specific result implementation.
- The AI instruction action copies a short evidence-handling prompt that links to `docs/handoff/trace-diagnostic-format.md`. No embedded prompt asset was added and the format guide is not duplicated in the clipboard text.
- Host enrichment is supplied by the Commands-side `TraceDiagnosticCapture` helper. It records project/baseline identity, capture timing, writing-system metadata, and only host-verified FieldWorks links; the App consumes that seam and does not re-derive provenance.
- The App does not serialize filtered view models or reconstruct a response DTO for export. It copies/saves the exact retained diagnostic JSON and loads it through `WordTraceQuery.LoadDiagnostic`.
- The existing unrelated walkthrough failures remain outside this feature.

## Verification and handoff

- `build.ps1`: passed comment hygiene and solution compilation with 0 errors.
- Focused App diagnostic tests: 22 passed, including producer-envelope load, retained host capture metadata, legacy morphology evidence, invalid-schema refusal, ancestor expansion, shared writing-system handling, unavailable MSA details, narrow layout, and keyboard/tree presentation behavior.
- Earlier full suite: 1906 passed, 38 skipped, 2 baseline walkthrough failures. Final fixture verification: 1905 passed, 38 skipped, those 2 failures plus an intermittent runner failure. The focused trace/runner rerun passed 62 tests with 2 skipped; see the acceptance evidence for the limits.
- No source changes are pending from this documentation update.
