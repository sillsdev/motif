# Try a Word data view

Status: implementation authorized. Final interview recommendations adopted as implementation defaults.

## Settled requirements

- Operate on one word at a time through an explicit opt-in trace.
- Exchange structured JSON, not HTML.
- Match the information richness of FieldWorks Try a Word with a better presentation in Motif.
- Extend PanGloss JSON where required to supply the missing information (user confirmed).
- Reuse existing overall and category timing/count architecture, with thin analysis. Do not present category timing as measured time for an individual step.

## Evidence from the initial source audit

FieldWorks displays morph forms, headwords, glosses, grammatical analysis details, navigable identities, and contextual failure explanations. Relevant sources are the sibling FieldWorks repository's Src/Transforms/Presentation/FormatHCTrace.xsl and Src/LexText/ParserUI/HCTrace.cs.

PanGloss trace-details v1 supplies result analyses, trace steps, subrules, failure reasons, search completion information, and category counters/timings. It lacks the full morph metadata and structured failure context needed for FieldWorks information parity.

The current Motif implementation also loses some already-emitted information, including result analyses and subrule numbers, with work and uses not fully represented in the UI. These findings describe the inspected work in progress, not a permanent contract.

## Design tree

1. Information parity requires richer PanGloss JSON: settled, yes.
2. Default presentation: settled. Show result and rich analyses first, with expandable diagnostic paths; keep complete trace and effort accessible.
3. Restricted reruns: settled. Do not implement Limit to selected morphemes in this version.
4. Failure explanations: settled. Show the specific mismatch and affected morph or rule, including required versus actual features or the failed environment when supplied. Expand for technical detail and identify missing evidence explicitly.
5. Morph details: settled. Show form, headword, gloss, and grammatical category immediately; expand for slot, inflection class, features, and identity. Navigation: settled. Clicking a morph or rule opens its details inside Motif, with a separate Open in FieldWorks action where available.
6. Timing presentation: settled. Overall elapsed time beside the result; expandable category counts and timing table. Calculate summaries from existing gathered data where supported, without attributing category totals to individual detours. Incomplete searches: preserve all findings and prominently label Search incomplete on timeout or limit, even if analyses succeeded. Large-trace navigation: settled. Search and filter paths by outcome, rule, or morph; preserve ancestor steps and show how much is hidden. Display filtering must remain distinct from incomplete parser search, and the full trace remains accessible.
7. Concrete layout and acceptance examples: follow earlier decisions.

Two Luna research tasks are examining FieldWorks visual layout and diagnostic interactions. Their source-backed findings will inform subsequent interview rounds.



## Further settled presentation decisions

- Selecting a trace step opens a persistent detail pane alongside the trace, stacking below on narrow windows. It presents input/output, morph details, failure explanation, and relevant category statistics without losing the selected position.
- When a word parses successfully, unsuccessful branches start collapsed, remain clearly counted, and can be expanded. Every recorded attempt remains discoverable.
- Copy and save the complete diagnostic JSON, including information hidden by display filters, with word, search status, analyses, trace, and timing statistics together.
- Load saved diagnostic JSON into the diagnostic view as well. Project association, provenance, and compatibility behavior remain to be settled.

## Saved diagnostics and documentation

- Saved diagnostic JSON opens without the original project, using captured labels and linguistic details. Only navigation to live entries or rules requires project access.
- Preserve recorded details when loading. Show a visible warning when the current project or grammar differs; do not silently substitute live values for recorded evidence.
- Capture available project identity, grammar fingerprint, parser version, and capture time. Missing provenance must remain identifiable as unknown.
- Load supported older formats with explicit not-recorded indicators for missing details. Reject incompatible formats with a clear explanation.
- Provide AI-facing documentation of the diagnostic JSON format, following the conventions used for the other JSON formats. Explain field semantics, analyses versus attempts, contextual failures, search completeness, and interpretation of timing/count data, with examples. The documentation must let an AI interpret a saved diagnostic without the original project.

## Implementation defaults and audited presentation

- Preserve parser traversal order and do not rank failed branches as probable causes.
- Provide Copy AI instructions beside diagnostic copy/save, pointing to a versioned format guide.
- Capture writing-system identity, text direction, and available font information; fall back to installed fonts for standalone viewing.
- Search includes collapsed trace content and reveals matching paths.
- Pair success/failure colors with textual status; use keyboard-accessible disclosure controls.
- Treat parser invocation errors, complete searches with no analyses, and incomplete searches as separate states.
- Only offer live navigation when authoritative source identity is available; never derive an identity from a display name.

The FieldWorks visual audit found successful analyses rendered as morph sequences (FormatHCTrace.xsl:191, 778), collapsed branches (1369), contextual failure reasons (425), and separate phonological input/output tables (996). Its search excludes collapsed branches (JSFunctions.xsl:243). Its traversal preserves source order without diagnostic ranking (FormatHCTrace.xsl:1237). Motif retains the information and improves search, navigation, accessibility, and progressive disclosure.
