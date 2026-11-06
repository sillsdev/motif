# ADR 0091 — Proposal review report: three levels and red flags, no verdict

**Status: Accepted.**

A Proposal needs a readable account of what its Trial measured, with the wordforms and analyses behind
the numbers. What is good enough differs by Proposal. Motif presents the evidence and a short list of
particularly concerning changes; the person or agent reads as much of the report as applies and judges.
There is no verdict, yes/no recommendation, PASS/BLOCK result, score, or combined quality ranking.

## Decision

### 1. The report belongs to the Trial

The CLI reads it with `proposal trial`; MCP uses `motif_trial` with `motif_difference` drill-downs.
It is a presentation of frozen Trial, Baseline, Assessment, warning and Parsimony evidence, bound to the
measured intent digest, prerequisite content, scope and evidence identities. A Draft need not have a
revision, and a later edit does not rewrite what an earlier Trial measured. Reading stored results
does not run the parser or refresh evidence. An explicit one-word parse trace may request new diagnostic
work against the recorded candidate, labelled separately from the stored measurement.

### 2. There are exactly three disclosure levels

- **summary** is the default and fits one screen: red flags first, Incomplete when applicable, the
  principal measures and their denominators, freshness, scope and links to detail.
- **failures** shows sections with **Wordforms to review**, top wordforms grouped by observed cause,
  before/after evidence and the number not shown. The level name does not turn every row into a failure.
- **full** provides complete, paged lists with filters, including unchanged cases, exact analysis
  identities and a one-word **parse trace** on request.

Summary counts do not change with disclosure level, filters or page size. Missing evidence is never
rendered as zero. A cause is an observed outcome unless producer evidence establishes a linguistic cause;
participation in a parse trace alone does not prove causation.

### 3. Only these five facts are red flags

1. **New word failures:** a word that parsed before now gets no parse, or an Approved word that parsed
   before no longer gets an Approved analysis it previously received.
2. **Confirmed correct readings lost:** a previously produced, confirmed correct reading disappears,
   whether confirmed by an Approved analysis or by a project-named reference/control set, such as Wolff
   forms. Each distinct confirmed reading counts; keeping another reading does not hide its loss.
3. **Known non-words now accepted:** a word on a project-named negative set, confirmed not to be a word,
   gains a parse. A Disapproved analysis alone is not a forbidden whole word.
4. **Parser work more than 20% worse:** total parser work increases by strictly more than 20% against
   the Baseline over the same scope. Report percent change only, never milliseconds or raw work counts.
5. **New warnings or new Parsimony failures:** new grammar warnings or new Parsimony findings compared
   with their bound Baseline evidence. The report identifies each measure and finding, rather than
   subtracting aggregate totals or making a Parsimony score.

An Approved-reading loss can meet both the first and second definitions; the report labels the overlap
and never adds those counts as if they described different words. Existing failures remain visible in
the sections. Gains and resolved findings do not cancel new losses or warnings. Reference assertions
retain their source and actor: a source-validated assertion never becomes a person's Opinion.

The summary always says **"No red flags means only 'not bad', never 'good'."** If a comparison could not
be made, it names that limit; absence of an observed flag is not evidence that no flag exists.

### 4. Incomplete is prominent and is not a failure

**Incomplete** is a separate shortlist entry immediately after the red flags, repeated at the failures
level: required words were not measured, or their searches stopped at a work, time or memory limit.
It says Incomplete, never Failed. New stops alone, their number or their percentage change do not create
a red flag. An unfinished search cannot prove that a reading disappeared. A known forbidden parse that
was actually returned can still be reported even when the remaining search is incomplete.

A report distinguishes observed loss, observed presence, unknown absence and unmeasured input. It
identifies run-level memory refusals without inventing per-word memory outcomes. Missing warning or
Parsimony comparisons, incompatible work evidence and unavailable expectations are visible limitations,
not extra red-flag categories or clean results.

### 5. Project-named word sets determine the questions, not an acceptance policy

Sampled-token coverage, verb-paradigm-cell coverage, and reference, regression and negative controls
are project-configured named word sets. Motif supplies no language-specific built-in list, coverage
target or Wolff oracle. Names, roles, source references and resolved membership are frozen with the
Trial; token positions/weights and declared cell identities preserve their own denominators. Merely
parsing a reference form does not establish that it received the expected reading.

Membership configuration is separate from human judgement. Human Opinions and reviewed negatives stay
FieldWorks project data; source-validated expectations cite the existing attributed evidence machinery.
Capture must supply exact word/writing-system and reading identities where existing evidence lacks
them. The candidate cannot improve its own comparison by changing the frozen expectations.

### 6. Complete report lists do not reinterpret bounded Differences

The report reads a streamed, indexed projection over its frozen inputs, including all requested cases.
Its disposable row index is separate from ADR 0067's bounded, saved Difference rows and from the older
Trial-summary Assessment. Neither existing Difference format is silently expanded or renamed. Existing
Differences remain useful links and retain their original completeness and retention statements.

The new index obeys the store budget and open-Proposal evidence protection in ADR 0082. It does not
duplicate complete trace trees or create a durable external judgement store. Once detail is purged it
says unavailable; a surviving summary never advertises complete pages it cannot supply. Rebuilding
requires the exact retained inputs and explicit action, never live opinions substituted on a page read.

### 7. Review does not authorize Apply

MCP never applies. CLI Apply remains a separate human action. This report grants no permission,
changes no Readiness rule and supplies no approval token. It contains no Trial/Apply parity check:
Motif's own tests establish Motif's correctness; this report describes the grammar.

## Relationship to existing decisions

- [ADR 0033](0033-three-systems-and-who-owns-which-measure.md) keeps measurement ownership separate and
  linguistic judgement with the person or agent. Flags are factual changes, not a judgement of quality.
- [ADR 0042](0042-a-job-produces-assessments-an-assessor-makes-them.md) keeps Assessments immutable,
  scope embedded by content and the improvement loop outside Motif. This decision clarifies "never
  renders a verdict" only to permit the listed factual flags. General word-joined comparisons still
  annotate differing contexts; the particular work flag requires the same scope and comparable work.
  Existing configured regression behavior at Apply is separate and unchanged.
- [ADR 0046](0046-pages-not-stages.md) makes review a reading task, not a lifecycle gate. Its
  FieldWorks wording and explicit reruns govern this report too.
- [ADR 0060](0060-parsimony-evidence-and-advisory-dispositions.md) keeps Parsimony advisory, preserves
  each measure's universe and distinguishes human judgement from derived evidence. "Parsimony failure"
  here means a new finding; a detector that failed to run is missing evidence, not a grammar finding.
- [ADR 0067](0067-proposal-difference-against-human-decisions.md) supplies exact analysis agreement,
  frozen human/source-validated references and honest incomplete states. Its bounded Differences
  remain separate from the exhaustive report index authorized here.
- [ADR 0079](0079-linguistic-decisions-follow-black-and-judgement-calls-are-the-linguists.md) keeps
  judgement calls with their maker and Health and Parsimony advisory. No flag overrides that boundary.
- [ADR 0081](0081-motif-counts-and-limits-parsing-in-parser-work.md) supplies parser-work semantics.
  This decision narrows review-facing cost presentation to percent change against the same Baseline
  scope; raw counters remain internal evidence and configured limits remain configuration.

## Consequences and alternatives

A one-screen report leads into bounded, citable evidence without deciding whether a Proposal is good
enough. Empty flag lists, omitted controls and interrupted searches cannot masquerade as approval.
Complete lists require additional frozen membership, expectation and identity evidence, rather than
relabelling existing samples. A verdict, a combined score, configurable extra red flags and a parity
gate were rejected because they would answer a different question from the grammar evidence report.
