# ADR 0065 — Phonology example sets are advisory evidence

An author can record the surface forms a sound change should produce and see whether the saved grammar produces them. Each result says what was tried and whether it finished, so an unavailable check cannot look like a successful one.

**Status:** accepted. Extends ADR 0042's Assessment scope and raw measurement shapes; follows ADR 0041's database storage rule and ADR 0043's shared command catalog. It does not amend Readiness or Apply.

## Context

Parsing a word asks whether the grammar can find its lexical and morphological analysis. A phonology example asks what an explicitly supplied underlying form becomes after the ordered sound rules, under declared grammatical context. They are different measurements. A completed Trial with no approved morphology does not answer the second question.

A Selection contains words to parse, not underlying forms paired with expectations. Proposal content describes changes to the grammar. Neither should acquire an unrelated meaning to store examples that an author may reuse across several candidate grammars.

PanGloss owns generation and its completion facts. Motif orchestrates, stores and presents the result. Exit status and a list of generated forms alone do not establish that the search finished.

## Decision

### 1. A Phonology Example Set is immutable, cited project evidence

A **Phonology Example Set** is a named, revisioned collection of explicit examples. Each example has a stable id, underlying form, expected surface form or forms, declared generation context and optional Citation. The context names exact source identities or explicit producer-supported context; it never guesses a grammatical class from spelling. The initial check requires available lexical context, and an empty lexicon is unavailable.

The set and each immutable revision live in the project database. Updating a name's current revision does not edit older revisions. A check embeds the exact revision and executable content digest; a newer set cannot reinterpret retained evidence. An optional Proposal association names the Proposal explicitly and does not change its authored intent.

Executable content identity includes example ids, underlying forms, expectations and context. A Citation uses `source` and optional `locator` under ADR 0064 and remains review metadata: changing a Citation does not change executable content identity. The full cited revision is retained as well, so earlier measurements still show the sources that accompanied them.

A Phonology Example Set is distinct from a Selection. No permanent fake lexical entries are inserted to make the check run. Author input and result sizes are bounded; durable payloads are immutable database values or database references covered by the shared retention policy, not Motif sidecar files.

### 2. A Phonology Check produces a real advisory Assessment

A **Phonology Check** is an explicit Worker job that measures a frozen grammar against an exact Phonology Example Set revision. Its Assessor produces an immutable **PhonologyExamples Assessment** with real per-example raw measurements. Assessment scope and raw shapes explicitly support examples; a parse Assessment with fabricated words or timings is not a substitute.

Each example reports one mechanical disposition:

| Disposition | Required evidence |
|---|---|
| `agree` | A completed supported check observed the authored expected surface form. |
| `disagree` | A completed supported check did not observe the authored expected surface form. |
| `unavailable` | The check could not supply complete supported evidence for this example. |

The first contract checks expected-form membership, not exhaustive equality with every possible generated surface. With several expected forms, the example requires each authored expectation; expectations are a declared finite set, not a prediction of all possible forms. Plain forms follow the producer's documented Unicode normalization, never a similarity or inferred linguistic equivalence.

Completion and availability are separate recorded facts. Retain observed forms and whether an expectation was observed even when a cap or timeout prevents a disposition other than `unavailable`. Missing context, invalid or unsupported shape, absent producer, capped work, timeout and cancellation carry typed reasons and recovery guidance. Unknown counts remain unknown. A zero-example set cannot yield a passing measurement.

The producer interprets its own generation/completion evidence and owns mechanical expectation comparison. Motif does not reconstruct a refusal from diagnostic prose, infer grammatical identities, or reinterpret a process exit as completed generation. Linguistic judgement remains with the author.

### 3. The job freezes all inputs and shares the existing lifecycle

The job names a captured Baseline or an exact candidate Dry Run, rather than silently capturing current project state. Candidate evidence keeps its original Proposal id, intent digest and source Baseline binding.

Before measurement, freeze the set revision and executable content, declared context, grammar source/content identity, Baseline or Dry Run identity, parser build and effective limits. The Assessment records these bindings and the measured universe, including lexical entries and rule order when the producer supplies them. Missing source facts are unavailable or unknown, never borrowed from today's project.

Submission, worker admission, claims, cancellation, recovery and completion use the shared job path. A durable accepted job id and worker admission outcome remain distinguishable. No per-transport watchdog or new pipe protocol is added. A stored read only reads evidence; it neither captures a Baseline nor runs the parser.

### 4. CLI and MCP share one catalogued contract

Typed commands save/list/read example sets, submit a Phonology Check, and page retained per-example results. Requests, schemas, surface eligibility and responses are declared once through the shared command catalog. CLI text/JSON and MCP project that contract; MCP does not add a second grammar interface or accept universal raw operations or SQL.

Authoring and submission follow Advanced AI authoring/job rules. Stored evidence reads are observations. Every exposed command has truthful Help and recovery text. Results are paged with typed continuation, not truncated by inserting prose into structured arrays. This decision introduces no window workflow.

### 5. The evidence is advisory and never Readiness

A PhonologyExamples Assessment does not satisfy Correctness, Readiness, a configured Apply regression gate, or Parsimony Readiness. Apply and force rules remain unchanged. Agreement means only that the declared examples agreed under the recorded context, grammar, parser and limits.

Any later readiness role requires a separate owner decision defining operation coverage, regression evidence and how unavailable/incomplete examples affect admission. It is outside this decision.

## Consequences

- An author can keep source-cited expectations and compare rule removal or rule order on copied grammars without changing the lexicon.
- Retained results remain understandable after the project, example set or parser changes, because they keep their original bindings.
- Existing word-based Assessment scope and raw shapes need an explicit example variant. Example results are not mixed with approved morphological analyses.
- A producer interface that exposes completion is required before a missing expected form can be reported as disagreement. A dependency repair or successful parser invocation alone cannot meet that requirement.
- Database shape changes use the current-generation refusal/recreation policy before 1.0, with no migration bridge.

## Validation contract

On a copy of the Swahili project containing the P2-4 rules, a cited set includes `m+wili` → `mbili`, `n+baya` → `mbaya`, and unchanged controls. Every row shows disposition, completion, observed forms, exact context and source binding. Removing a rule on a copy changes a supported positive row into a completed disagreement. An empty-lexicon copy yields unavailable rows rather than agreement.

An ordered-rule counterfactual records a different result when rule order changes. Tests also demonstrate capped or timed-out work remains unavailable even if it observed an expectation, a zero-example set does not pass, missing context is actionable, stored reads do not capture/run, revisions do not reinterpret historical results, paging preserves every row, and no advisory result changes Readiness.

## Rejected alternatives

- **Proposal content owns the set:** ties reusable expectations to one grammar change and conflates measurement input with mutation intent.
- **Selection stores the pairs:** changes a word-list concept into a second incompatible input shape.
- **A Trial name covers every check:** hides that this job measures examples and does not itself author or Dry Run a Proposal.
- **Inline or read-triggered generation:** loses shared durable job, cancellation and evidence behavior.
- **Permanent fake roots:** changes the measured grammar to hide missing context.
- **Exit-success or partial absence proves disagreement:** confuses invocation/completion with supported negative evidence.
- **An immediate Apply gate:** gives advisory examples an owner-reserved readiness role without the required coverage contract.
