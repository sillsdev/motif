# ADR 0045 — The Handoff is five files and a pasted header

**Status:** accepted, 2026-09-18. Rewrites the **Handoff** entry in `CONTEXT.md`. Builds on
[ADR 0044](0044-every-parser-process-is-one-pangloss-invocation.md) (one way to run the parser) and
[ADR 0011](0011-experiment-loop-boundary-motif-is-the-record.md) (Motif is the record). The work package is
R5 of [the 0.1.0 release plan](../superpowers/plans/2026-09-15-release-1-0.md).

**In plain terms:** Motif writes a folder that a linguist drags into ChatGPT, Claude or Gemini and asks why
a word did not parse, or why parsing is slow. That folder held seventeen files in three nested directories,
and a real test showed a model could answer factual questions from it but could not say which language it
was looking at, disagreed with itself about which parser had run, and gave no reason for a word that failed.
**From now on the Handoff is exactly five flat files, whatever was selected**, dragged straight from the
window, with a short block of text the person pastes ahead of their question. The long explanations stop
travelling in the folder and live in the repository that owns each format. And because "why didn't this word
parse" cannot be answered from statistics alone, the parser's own derivation trace becomes part of what a
run collects.

## Context

The Handoff is Motif's outbound artefact: the person, not Motif, sends it. It is also about to become the
only one of its kind. The FieldWorks AI export is being retired and FieldWorks stripped of all handoff
material, so anything that export did and Motif does not is simply lost.

Four facts constrained the design, all established rather than assumed.

**A model reads a small flat set better than a large nested one.** The 2026-09-15 experiment (section 7 of
the release plan) established that a reader with no other context answered every factual question correctly
from the files on disk — the failures were of identity and provenance, not of comprehension. Seventeen files
did not help; naming the language would have.

**Merging is the cost of tracing, and it is not ours to switch off.** PanGloss's trace is a port of
HermitCrab's `TraceManager`: 19 trace types, 24 failure reasons, per step the named rule, stratum or
template, which subrule or allomorph fired, and the word forms in and out. It is the only thing in the
system that can answer "why didn't *xyz* parse". But a traced parse deliberately runs unmerged —
`AnalysisStratumRule.cs:152`'s "don't merge if tracing, it messes up the tracing" guard, ported at
`pg-parse/src/morpher.rs:402` — because a merged trace understates the search. PanGloss's own audit
(`docs/research/hc-tracing-fieldworks-audit.md`) states the consequence: "diagnostics can amplify the
pathology they are intended to explain." Analysis memoization, the other collapse, has been removed from
PanGloss outright. So a trace is not a recording of a parse; it is a second, larger parse.

**`batch` cannot trace.** `--trace` exists only on `pangloss parse <grammar> <word>`, one word per process,
and that command carries no `--step-cap` of its own. There is no trace-specific containment in PanGloss
today, and its audit says there should be.

**PanGloss is public.** `sillsdev/PanGloss` serves raw Markdown, so documents can live in the repository
that owns the format they describe rather than being copied into every folder Motif writes.

## Decision

**1. Five files, always five.** `grammar.json`, `texts.json`, `assessment.json`,
`parse_grammar_texts_assessment.py`, `handoff.md`. Flat, no directories. Every selected Text goes into the
one `texts.json`, keyed by GUID and title, so selecting twelve Texts does not reconstruct the nested folder
this ADR deletes. The `.fwdata` never travels: this Handoff serves grammar-only fixes, and lexical and
FieldWorks updates are a later phase.

**2. A Handoff with no Assessment is valid.** `assessment.json` is absent and `handoff.md` says so. Motif
replaces an export that never ran a parser; refusing to hand off until an Assessment exists would make the
successor worse than the thing it succeeds.

**3. Every JSON file is valid JSON with one record per line.** Pretty-printed to the record, compact within
it, so a `grep` for a word returns a whole record and `json.load` still works. `handoff.md` teaches both.

**4. `handoff.md` is capped at 100 lines.** It holds orientation, a basic manifest, and per file: what it
is, one `grep` example, one Python call. The cap is the mechanism; without it this file becomes the
seventeen-file explainer again. Anything fuller lives in the helper's `--help` and in the linked documents.

**5. The pasted header is generated per run and copied from the window.** About five bullets orienting a
reader who has never heard of FieldWorks, the language name and the project name, the two questions the
Handoff exists to answer, and the pinned links. No provenance beyond that: the reader is a chat model, and
it does not need a chain of custody.

**6. Documents live with whoever owns the format, pinned.** PanGloss gains `docs/formats/` —
`grammar-format.md`, `trace-format.md`, `hc-mechanics.md` — with its own README stating it is written for
outside readers, so a linguist's model is not pointed into port-audit material. Motif keeps
`docs/handoff/flextext-json-format.md` (Motif's `FlexTextJsonWriter` produces that format; PanGloss never
touches it) and gains `assessment-format.md`. Motif gains `docs/parser-help/`, the FieldWorks parser-help
material moved before FieldWorks is stripped. Links are raw Markdown URLs pinned to a release tag, or to the
branch when no release exists; none of these documents travel inside the folder.

**7. Official releases mandate a git tag, cut before the release.** A Handoff's links pin to a version, so a
release without a tag produces a Handoff that cannot name what wrote it.

**8. An Assessment run collects traces, in one invocation with two phases.** The batch runs merged and
timed, as now. Then, inside the same run and after the batch's own results exist, a chosen set of words is
re-parsed traced and unmerged. From the person's side this is one run. The amplification is confined to the
traced words, and every word's timing still comes from the merged pass, so timings stay comparable.

**9. The Selection admits typed words.** A third kind of member beside Texts and entries: words the person
typed, which need not appear anywhere in the project. "Why didn't *xyz* parse" is usually asked about a word
someone has in their head. They are parsed and measured with everything else.

**10. The traced set is chosen, shown, and not bounded by fiat.** The choices are typed words, the *N*
slowest, and all of one or more Texts. There is no fixed ceiling: the window shows how many words the
current choice will trace and what they cost — PanGloss's step counts beside the measured times, since
counts compare across machines and times do not — and the person decides. When a set must be narrowed:
typed words first, then failures and limit-hits, then the slowest.

**11. Tracing can never cost the Assessment.** The retained invocation is published when the batch
completes, before any trace runs; traces join it as they land. Cancelling during the traced phase leaves the
invocation standing, marked partial with counts. A trace that exhausts its cap or its own timeout — shorter
than the batch's, imposed by Motif because PanGloss has no trace-specific bound — is kept with
`traceComplete: false` and the reason. A half-finished derivation is evidence.

**12. Traces live inside `assessment.json`, keyed by word**: the tree verbatim, plus a derived one-line
summary per traced word — outcome, step count, whether it completed, the distinct failure reasons, the
deepest rule reached. The tree is verbatim because deciding which branch mattered is the judgement being
handed to the model. The summary is what makes fifty traces triageable and what a `grep` lands on.

**13. What leaves the machine is stated in the window, not in the file.** One line beside the drag tiles.
The decision is made when the person drags, not when a model reads line 4 of a Markdown file.

## Consequences

- `CONTEXT.md`'s **Handoff** entry is rewritten; **Selection** gains typed words; **Trace** is added.
- `selection.txt`, `statistics.md`, `recipes.md`, `instructions.md` and `read_handoff.py` are deleted.
  `starter-prompt.md` is rewritten wholesale — its current file list describes none of the five.
- `read_handoff.py` is replaced by `parse_grammar_texts_assessment.py`: standard library, many convenience
  routines for pulling and filtering parts of each file including parts of a trace, and a `--help` that
  teaches the file types. `handoff.md` references it rather than duplicating it; its source is also linked.
- `grammar-format.md` and `hc-mechanics.md` move to PanGloss. This is a cross-repo change, and Motif's
  release now depends on those documents existing at a pinned URL.
- `HandoffViewModel.cs`'s `blob/main/…` links become `raw.githubusercontent.com` at a pinned ref. A `blob`
  URL serves rendered HTML, not the Markdown the header promises.
- The design is load-bearing on a model's willingness to fetch a URL. If it will not, the long explanations
  are not merely inconvenient — they are absent. R6's acceptance test must check this directly.
- Two asks go to PanGloss as issues, not as release blockers: a trace surface on `batch`, and the
  trace-specific containment its own audit identifies as missing.
- Memoization removal is on `feat/remove-memoization`, not PanGloss `main`. The pinned release still
  memoizes; the traced phase's cost model changes when that lands.

## Rejected alternatives

- **Keep the seventeen-file folder and fix its identity problems.** Rejected: the identity failures were real
  but the file count was the thing a reader had to navigate, and the fix for "which language is this" is one
  line of the pasted header, not a better directory layout.
- **Ship the `.fwdata`.** Rejected by scope: this Handoff serves grammar-only fixes. A raw project file
  invites lexical answers the workflow cannot apply and carries far more than the question needs.
- **One file per selected Text.** Rejected: the file count then varies per run, the pasted header's manifest
  cannot be learned, and twelve Texts rebuild the nested folder. A large file is acceptable precisely because
  it can be torn apart with Python or grepped.
- **Run the whole Assessment unmerged so traces are free.** Rejected: with memoization gone, merging is the
  only remaining collapse. Every word would pay the amplification, limit-hits would multiply on exactly the
  corpus words already near the cap, and it needs a PanGloss change, since `batch` cannot trace.
- **A separate, user-visible Trace pass after the Assessment.** Rejected: it is two runs where the person
  asked for one, and phase two of one invocation gets the same access to the batch's results — which is what
  "the ten slowest" requires.
- **Prune traces to the failing paths before writing them.** Rejected: that is Motif deciding in advance
  which branch mattered, which is the judgement being delegated. The derived summary gives triage without
  discarding evidence.
- **Copy the reference documents into every folder, as today.** Rejected: they are the largest part of the
  package, they go stale the moment either repository moves, and duplicating a format's documentation away
  from the code that produces it is how the two come to disagree.
- **Put the data-sensitivity warning in `handoff.md`.** Rejected: a warning addressed to the person, buried
  in a file addressed to the model, is read by neither — and it arrives after the drag it was meant to
  inform.
