# ADR 0020 — A Job produces Assessments; scope decides comparability

Measurements say what was tried, which tool ran and whether the work finished. Motif presents comparable
evidence without deciding whether a linguistic analysis is good.

**Status:** Accepted, 2026-11-14. The design in Proposed remains unaccepted.

## Context

Checking proposed project edits and measuring a parser answer different questions. Stored measurements must
remain understandable after the project, parser, expectations or measurement scope changes.

## Decisions

### D1. Jobs, Assessors, Assessments and Trials have distinct meanings

A Proposal is the stored, reviewable unit of Motif operations. A Dry Run applies it to a disposable copy and
reads back its effects. An Assessment is one immutable kind of raw measurement, made by one Assessor under
an Assessment scope. A Job is queued work and can produce several Assessments. An Assessor invocation can
produce several kinds: engine size, parse timing, per-object cost, Correctness, a Trial summary, or completion.
An Assessor need not be PanGloss or even a parser. Assessment kinds form a closed set so missing input can
be named. Failed-Apply recovery discards the cache unconditionally under ADR 0017; no cache-poisoning
classification is retained.

A Trial combines a Dry Run and candidate Assessments. Starting one changes no Proposal status, freezes no
authored content and does not require Finalize. Measurements of a Draft cite its intent digest rather than
a nonexistent revision. An edit never rewrites an earlier Trial's measurements.

### D2. An Assessment has no lifecycle

Measurements are immutable once stored. The producing Job carries execution states; current is a pointer
held by the project. Availability after retention is separate from measurement identity. Assessment detail
and parser artifacts follow [ADR 0023](0023-one-store-beside-each-project-inside-a-byte-budget.md).

### D3. Comparisons join on the word and measurement kind

An Assessment scope records the words or explicit examples, Assessor, engine, collected capabilities and
effective limits a run was told to use. It is declared in readable `<project>.motif.toml` configuration and
embedded by content in the Assessment. Editing configuration cannot reinterpret a stored measurement.
Per-word work/time limits are part of that recorded context; parser work is defined in ADR 0019.

Word-based comparisons join on the word and kind. Assessor, engine, limits, corpus and tokeniser annotate
the comparison rather than prohibit it; neither scope equality nor containment is required. Missing data
can still make a particular Report unanswerable. When both sides record parser content hashes and they
differ, the comparison, stored Trial-summary scope and Report warn with both identities and versions when
known. An unrecorded parser is unknown, not a mismatch. The work red flag in
[ADR 0021](0021-a-trial-review-report-has-three-levels-and-no-verdict.md) requires comparable work over the
same scope even though general comparisons permit differing contexts.

### D4. Reports present stored raw evidence and declare missing capabilities

A Report is a presentation/query over stored Assessment material or a validated frozen Parsimony evidence
bundle with optional actual Assessment references. Coverage, Correctness and Trial-summary producers keep
their real Assessment requirements. Static or text-grounded Parsimony needs no placeholder Assessment.
One registered producer family accepts these closed inputs; `parsimony` orchestrates that registry.

The Assessor interprets its own format. Motif may delegate rendering or comparison back to it, then joins
and presents the result. Per-rule cost is PanGloss's nesting-aware `self_time_ns`, not Motif's estimate from
word timings. The store records external artifacts' paths and digests. Asking for uncollected counters
names the missing capability rather than returning zero.

A Report waits for its Assessment to finish; an in-flight alphabetic prefix is neither a sample nor a
valid lower-bound Report. Stored reads never start parsing or refresh evidence. Each Report pins the exact
inputs it used, exposes freshness, and remains readable within the retention rules of ADR 0023. Known
changes can be summarized as words, grammar, and texts/analyses; an unaccounted external change is reported
as invalid evidence requiring an explicit rerun. No causal history is invented.

Word-only changes use the supplied-lexicon path without rebuilding an unchanged FST; grammar changes pay
for a build. ADR 0019 owns that parser boundary.

### D5. A project may opt into a regression gate, without giving Motif a verdict

A project can declare in `<project>.motif.toml` that falling coverage or loss of an Approved analysis blocks
Apply. A human can override that configured gate and the override is durable. It is not a recorded review
or an approval token; Proposal review has the lifecycle and attribution rules of ADR 0007. Motif does not
rank candidates or own an improvement objective, accept policy or stopping rule. A person or external
driver judges the evidence. Parsimony and phonology examples have the narrower advisory and opt-in rules
in [ADR 0022](0022-parsimony-and-grammar-warnings-advise-and-blocking-is-opt-in.md).

A word InvalidShape on both sides for the same recorded reason is unaffected: it remains visible but does
not make Correctness incomplete or block Apply, including autonomous Apply. A one-sided InvalidShape, or
different reasons on the two sides, still blocks under the existing rules.

### D6. Apply promotes current evidence before releasing Proposal ownership

Apply promotes the candidate Assessment to the project's current pointer and refreshes the Baseline lazily.
Promotion is bookkeeping: its recorded Baseline token still exposes later Drift. Promotion precedes
Proposal cleanup and protects the promoted identity as current project evidence. Close-time and inactivity
release remove Proposal ownership without deleting bytes still used by current evidence or another
entitled consumer (ADR 0023).

### D7. Same analysis means the same ordered morphology

Use FieldWorks' `MatchesIWfiAnalysis` shape: equal morph-bundle count and, in order, exact source Form
(`MorphRA`), MSA (`MsaRA`) and inflection type (`InflTypeRA`), with its conditional guessed-text and
writing-system handling. `fieldworks-parse-analysis/v1` carries these authoritative identities. Missing
identity is unavailable evidence; captions, owner ids, spelling similarity and MSA-plus-spelling cannot
supply it. Internal duplicate-analysis merging is a different comparison with different fields.

Sense and word-level category are reported as human information and do not gate morphology agreement.
A positive result claims only “the parser agrees about the morphology.” It says nothing about sense or
whether the parser finished searching. Reviewed reading negatives use the same exact ordered identity;
a forbidden reading never becomes a forbidden whole word. Surface negatives require explicit context.
Only the Proposal-bound explicit-retirement adapter in ADR 0015 may translate frozen, explicitly mapped
Form roles; it preserves original evidence, refuses collapsed contrasts and changes neither ordinary
Baseline matching nor guessed-string rules.

### D8. A Selection is an explicit word list outside authored intent

Store an explicit, resolved NFD word set with its origin, not a rerunnable query. An Assessment may export
failed, timed-out or unanalysed words into a new list; it does not retain a query lifecycle. Selection
provenance is outside the Proposal's intent digest, so changing measurement breadth invalidates neither
the authored revision nor its bound review. Named Selections express importance without per-word priority
weights. Source/occurrence membership and tokenisation remain separately bound where relevant (ADR 0024).

### D9. Approved expectations are the composite of FieldWorks analyses

All distinct human-approved morphologies on a word form constitute its expected set. One approved reading
does not cover another. An occurrence's analysis is disambiguation; the type-level composite is the grammar
test. Adding, updating and removing expectations are separate facts, never netted against passing counts.
The FieldWorks ownership contract is ADR 0003. Proposed occurrence-first evidence is set apart below.

### D10. Key project records on the word form and compare analysis content

`WfiAnalysis` GUIDs are not durable analysis identity: edits can mutate, fork or delete them depending on
sharing. Key aggregate records on the stable `WfiWordform` GUID and compare morphology by content. Spelling
correction can repoint an occurrence to a different word form; observing `Form` alone cannot catch it.
A vanished or merged-away word form is reported as vanished and requires reselection. No similarity
matching, inferred forwarding or tombstone is fabricated.

### D11. Capture the before-state in the comparison footprint

FieldWorks can destroy a previous approved analysis, so record it before mutation. An analysis Proposal's
footprint includes word-form GUID, Form, approved-set content digest and existence. Occurrence references
are excluded: routine text churn does not change the intended analysis edit. Existence matters because
unreferenced word forms may be collected.

### D12. One aggregate read supplies manual and automatic evidence without parsing

For each word form return its manual and automatic analyses, links, occurrence counts and instances per
manual analysis. Read whatever stored Assessment exists and say missing or stale when appropriate. Even
without a parser result, the manual test set remains useful. Producing an Assessment is a separate explicit
verb. Diffing two responses exposes automatic-analysis changes, text-count churn and expectation changes.
The aggregate itself grants no Apply permission.

### D13. Observations do not establish cause

Report changed grammar, changed expectations and new agreement side by side. Do not claim which caused a
match when both changed. A Hole is undecided by construction. Linguistic interpretation belongs to its maker.

### D14. Unanalysed words provide reach evidence, not invented expectations

A correctly spelled word with no approved analysis has no positive morphology expectation. Count how many
such words parse, explicitly as weak reach evidence used only to assess a complete-language-coverage
claim; the Report states that limit. An increase could mean better grammar or looser grammar and is
never correctness. SpellingStatus records incorrect spelling; supported reviewed negatives remain distinct
project judgements (ADR 0003). No
expectation is inferred merely from an absent analysis. Spelling-correction pair records are undesigned.

### D15. Every figure names the state it describes

State corpus and grammar identities with every coverage figure; stale measurements are phrased in the past
tense rather than suppressed or shown as current with a footnote. `GrammarCoverageFigure.Describe` requires
current corpus and grammar arguments and has no parameterless rendering path. A lower bound remains labelled
as such, and nothing-adjudicated never becomes a correctness percentage. Unqualified corpus accuracy is
unavailable under ADR 0024. Grammar coverage measures reach; feature coverage measures exercised features
and combinations.

## Proposed

### D16. Baseline and always-current Evidence are separate

The Baseline would contain grammar and lexicon; scripture and approved occurrences would be versioned
Evidence. Every Trial and Measure would record both. Paratext chapter changes would re-read the chapter,
re-anchor judgements, bump Evidence, mark affected Progress and Proposal results outdated, and rerun only
changed words. Apply would remeasure against current Evidence. Scripture would still remain in Paratext
under ADR 0024. Only the legacy FieldWorks USFM-import path would refresh `StText` through LibLCM when
FieldWorks is closed, using AnalysisAdjuster for unchanged words; author-written Notes remain Proposals.

A non-parsing word would be a possible misspelling, never self-justifying grammar Evidence. It would become
Evidence after an approved analysis, or after being marked correct with at least three in-context occurrences.
AI and MCP would draft stem/rule additions only from that Evidence, showing verse links or an evidence table.
SpellingStatus remains unanchored; Paratext's spelling list takes precedence when available. Builds would
publish temporary output atomically only on success, retain the last good parser and show its age and the
build error, as also proposed in [ADR 0026](0026-a-project-home-is-fwdata-or-a-harmony-log-everything-else-is-built.md).

## Open

The concrete representation of a durable human regression override remains Open. It must preserve the
explicit override required by D5 without restoring the actor-bearing Decisions/sign-off table removed
by ADR 0007. The override policy is accepted; a new storage representation is not decided here.

## Consequences

Readers can distinguish agreement, completion, missing input and stale evidence. Measurements remain citable
without indefinitely retaining all raw files; explicit recomputation creates new evidence under its actual
inputs. Neither a favourable Report nor a completed Trial supplies a linguistic verdict or permission to Apply.
