# ADR 0022 — Parsimony and grammar warnings advise; blocking is opt-in

Grammar advice shows the statement, examples and uncertainty behind each recommendation. It helps people
choose what to fix or keep; only a narrow, explicitly configured autonomous policy can hold work.

**Status:** Accepted, 2026-11-14; D12 added 2026-11-16 (owner).

## Context

Structure, attested use and parser counterexamples provide different strengths of evidence. Combining them
into a score hides missing evidence, while retaining every intermediate facts file wastes storage.

## Decisions

### D1. Findings declare evidence, axis and a local universe

Evidence tiers are Static (authored/loaded structure), Text-grounded (exact captured occurrences and human
Approved analyses), and Parser-in-the-loop (actual observed/completed cases citing their Assessment).
A parser facts capture alone is not an Assessment. Static evidence identifies a candidate, not an error.

Parsimony favours fewer or less-duplicated statements; restrictiveness favours the narrowest statement
preserving known valid readings and limiting explicitly forbidden ones. Breakage is grammar-health evidence,
not another axis. Order comparable findings by parser-in-the-loop, text-grounded, static, then restrictiveness
before parsimony within a tier. A both-axis finding appears once in the restrictiveness position. No combined
or per-group score makes different denominators comparable.

Each measure names tier, axis, unit, threshold version, scope, finite universe and denominator, preserving
individual identities under group/project rollups. Tokens, Wordforms, readings, lexemes and grammar objects
are separate units. Selection absence means not observed. Disapproval forbids a reading, not a word;
reviewed negatives are human-confirmed forbidden cases. Unattested extension is descriptive without a
finite universe and explicit negative or held-out evidence. All captured Approved analyses remain recipe
safety witnesses even if a detector uses a narrower Selection.

### D2. Human judgements remain durable FieldWorks data

Reviewed negatives and rulings live as revisioned Notebook records under ADR 0003, never canonical
negative/ruling rows in `Project.motif.db` or analysis files. Initialization is deliberate and proven
before product writes. Frozen projections are evidence, not the judgement store. Synthetic probes are not
human judgements; an agent cannot pose as a person, change Opinions, Apply through MCP or grant Readiness.
This ADR authorizes no MCP ruling writer; agent annotation would need a separate narrow opt-in.

Ruling values are fix, keep, ask and defer, attached to exact finding and relevant evidence digest.
Fix records intent without creating/applying a Proposal. Keep may record a reason and suppresses only that
item under that evidence; the Suppressed set remains queryable. Ask neither sends nor answers a question.
Defer keeps it for later. A relevant evidence change reactivates it; unrelated Text changes and publication
times do not. Suppression is derived from captured judgement revisions, never deletion from a frozen Report.

### D3. Findings have stable identity and versioned Recipes

Identity comprises measure, attachment kind, exact item and canonical group identity, with no invented group
GUID. The separate evidence digest covers relevant facts, witnesses, counterexamples, scope, measure/
threshold versions and judgement revisions, excluding labels, timestamps, unrelated Texts and page limits.
Every recommendation names a versioned Recipe or states why a safe update is unavailable. Recipes use only
supported semantic operations and state required FieldWorks work, unanswered questions or missing operations.
Evidence queries use fixed named views with typed arguments or a **closed typed query record**. The
record permits length-capped **non-backtracking regular expressions on string columns**. Callers cannot
supply SQL, joins, nesting or other expressions.

Verification addresses the exact intended item and preserves known valid readings and reviewed negatives
in its declared universe. Incomplete searches prove no absence. Verification produces advice, never an
Opinion change or Apply.

### D4. Grammar and Text are distinct groups

The Grammar group reads frozen grammar facts with no Text, Selection or Assessment requirement. It captures
a Baseline if none exists but never recaptures an existing one merely because it is old. Context views use
`project-approved`: all project wordforms with Approved or Disapproved analyses. A measure needing words
belongs to Text regardless of its catalogued tier. Incomplete facts can still make Grammar abstain.

The Text group reuses a manually chosen saved Parsimony Selection until changed. This is a separate pointer
from the Default Selection; they may name the same stored Selection but neither derives from the other.
Resolve exact words, Text identities and digest at enqueue. Text-tier measures read its Approved analyses;
parser-tier measures reuse a complete Assessment for that Baseline token/Selection digest or submit a real
Assess Job. Explicit `--assessment` overrides. Do not widen the Selection for outside reviewed negatives or
Disapproved readings: report their not-checked counts. No placeholder Assessment is invented.

CLI `parsimony --group grammar|text|all` and MCP `group` share one definition. A named measure outside its
group is refused. With no saved Parsimony Selection, all runs Grammar and gives configuration guidance once
as a note; text alone refuses with the same explanation. Source-validated non-human decisions do not enter
`HumanApprovedAnalyses`, Approved evidence or reviewed negatives. Later presentation would have to label
them separately.

Grammar measures include `P-adhoc-duplicate`, `P-allo-duplicate-form`, `P-statement-unused`,
`R-allo-unconditioned` and `P-allo-alternation-family`. Text-tier measures include `B-adhoc-is-slot-order`,
`B-affix-null-vs-optional`, `B-affix-unslotted`, `R-env-broad`, `R-nc-excess`, `R-slot-blocking` and
`R-tmpl-precedence`; parser-tier measures are `R-word-disapproved-produced` and `R-word-negative-accepted`.
Grouping follows actual input dependencies, not a fixed catalog size. Alternation-family findings set aside
different selection gates/unloaded allomorphs and name those limits. Unconditioned families with unknown
abstract flags, multiple source allomorphs or inconsistent compiled order name their abstention reason.

### D5. Keep Reports and referenced views, discard grammar facts

PanGloss `grammar-facts.sqlite` and Motif `evidence.sqlite` are separate versioned derived artifacts, bound
to full Baseline token, model fingerprint, input kind and candidate identity. Candidate evidence never
replaces Baseline evidence. Final byte digests belong in the database bundle descriptor.

A retained bundle holds evidence.sqlite only. Check facts-file integrity and the verifying session's
digest, render, then delete the facts. Retain their digest, schema and filename as identity without an
existing-file path. Store Report bindings, measure/threshold versions, rendered findings and limitations.
Unsupported derived formats are refused or rebuilt, never migrated as authored data.

While facts are open, compute each full filtered view a finding references. `ParsimonyFrozenViews` records
bundle, view/filter identity, version, rows digest and JSON. Each kept view is at most 1 MiB and all bundle
views at most 3 MiB, measured as UTF-8 bytes. Keeping is best effort; all failures except Job cancellation
omit the view. Kept rows preserve the live result byte-for-byte; deleting a bundle cascades its views.
Facts-dependent views not kept say Unavailable and ask to rerun measures. Evidence-only join-quality,
parser-case, active/suppressed/history views remain available. Finding Search reads the stored Description.

Reuse needs matching material key/evidence digest, Reports whose input digests match the bundle, and a
Report for every requested measure. It opens no facts session and runs no parser. Candidate-before reuse
strips ruling projections as a fresh candidate does. Reverify the evidence file and Report/bundle
bindings; the discarded facts digest is a recorded fact. A new measure or unkept facts-view requires a new
bundle, not interpretation of missing rows. `ReadMsaDescription` orders by ordinal, writing system, sense GUID.

Facts requests use `--light`. Fixed queries accept complete sections or partial sections whose exact reason
is light for entries, conversion_inventory and source_census; other partial/missing sections stay unavailable.
Recovery deletes an unreferenced published bundle only with no bundle row or live/expired build claim,
no writes for an hour and a fresh row/claim recheck. It never scans staging or deletes the work root.
ADR 0023 governs retained size and release; the ordinary whole-Proposal target is under 10 MB.

### D6. Ordinary advice does not gate; the autonomous exception is closed and opt-in

Grammar warnings, linguistic preferences and ordinary Parsimony findings advise. Findings, thresholds and
rulings do not grant Readiness or Apply Authorization. Other Report kinds can have the configured
regression policy of ADR 0020. Consultant-mode windows stay advisory.

Only `R-word-disapproved-produced`, `R-word-negative-accepted` and `P-variant-same-shape` are block-capable,
only in autonomous mode and only if listed in `[parsimony] block-on` in `<project>.motif.toml`. The first two
need a Trial-measured wrong reading through disable/Trial/bisect/delete evidence; this policy does not add
those callers' integration. Invalid block-on ids are configuration errors naming the line. Freeze the
policy into Trial inputs. A missing table means advisory and no fold. A rule-attempt hold is a Trial report
field, not an additional Readiness field or a universal direct Apply gate.

### D7. Five same-shape additions need a recorded rule attempt

A variant shape comprises variant type, environments, features and minimal string correspondence, with
vowels replaced by V, initial/medial/final anchor and trailing-hyphen flag. Five or more added objects of one
shape require a nonblank rule, prediction-test result and reason for failure. A reviewer holds a package
without that complete attempt; opted-in autonomous policy reports `ruleAttempts.hold`. Shapes beyond this
string-correspondence variant contract are undesigned.

The read-only `IRuleAttemptLedger.Find` uses ADR 0011's CLI-written hypothesis ledger, never another store.
A tested passed/failed hypothesis clears a group if its change is the full shape key, or its correspondence
with a blank or matching environment. Proposed/superseded hypotheses do not clear it. Lookup is one indexed
seek per group. Freeze Baseline lexeme forms named by `createVariantRef` (one lookup per link) and policy in
FrozenReviewInputs; group operations and publish `summary.ruleAttempts` without parsing. The same check runs
over a compose-batch plan with `parsimony --rule-attempts --plan`. Apply does not consume that report line.

### D8. Redundancy measures use bounded grammar queries

`P-lexeme-near-duplicate` buckets lexeme forms after Unicode/case folding plus explicitly configured
`[parsimony] fold` from>to rules; there is no built-in language fold, and exact repeats stay with
`P-entry-duplicate`. Reused Reports retain their frozen fold until Baseline refresh.
`P-variant-same-shape` buckets existing variant links/forms/environments at five per shape.
`P-tag-polarity-majority` counts a tag on at least half of at least five stems in a rule/affix category scope.
It is explicitly partial: phonological-context-selected hosts are not detected and no finding is not clean.
These use respectively O(entries), O(variant links) and O(MSAs + rules) work, never pairwise comparison or
another parse. Analysis is required to remain under one second at the declared scale.

`P-tag-implied-by-context` remains declared not-available pending PanGloss carrier/context counts. Motif
does not reimplement compiled-context matching. `P-allo-alternation-family` retains its existing meaning.
Appended group kinds have explicit stable wire names; old reports still read, but old builds need not read
new closed-enum kinds. Optional `ReviewSummary.ruleAttempts` is omitted when absent and old reports read null.

### D9. Weekly Parsimony is an external schedule

`parsimony --measure all --wait --trend <tsv>` appends a trend row per counted class. Help supplies Task
Scheduler/cron examples; Motif installs no scheduled task. Existing same-shape groups inform the review;
opting in changes the treatment of future additions, not historical work retroactively.

### D10. Phonology Example Sets are immutable, cited project evidence

An example set is named/revisioned with stable example ids, underlying form, finite expected surface forms,
exact generation context and optional Citation. Initial generation requires available lexical context;
empty lexicon is unavailable. Database revisions are immutable, and each check embeds exact revision and
executable digest. Name-current pointers do not edit earlier evidence. Citations carry source/optional
locator and affect review metadata, not executable identity; retain the full cited revision. An optional
Proposal association changes no intent. Sets are not Selections or Proposal mutation content. Inputs and
results are bounded database values/references, never durable Motif sidecars or permanent fake roots.

### D11. A Phonology Check produces a real advisory Assessment

An explicit Worker Job measures a captured Baseline or exact candidate Dry Run against the frozen set.
Scope and raw shapes have an explicit examples variant, never fabricated parse words or timings. Freeze
set/context, grammar content, Baseline/Dry Run, candidate Proposal/digest/source token, parser and effective
limits, retaining producer-supplied lexical universe and rule order. Missing identities remain unknown.

For each example, agree requires a completed supported check returning every authored expected form;
disagree requires a completed supported check missing an expectation; unavailable means evidence is not
complete/supported. This tests expected membership, not exhaustive generated-set equality. Normalization
is producer-defined, never linguistic similarity. Completion and expectation observation remain separate;
preserve observed forms even when capped/timed out/cancelled. Missing context, invalid/unsupported shape,
absent producer and limits carry typed reasons/recovery. Unknown counts remain unknown; zero examples cannot
pass. The producer owns generation, completion interpretation and mechanical comparison; exit success or
diagnostic prose cannot establish completion or absence.

Shared submission/admission/claims/cancellation/recovery handle the Job; a durable accepted id is distinct
from worker admission. CLI/MCP catalog commands save/list/read sets, submit checks and page all retained
rows with typed continuations, truthful Help and recovery. No SQL, raw-op grammar surface, new pipe protocol,
transport watchdog or window workflow is introduced. Stored reads never capture a Baseline or run generation.

PhonologyExamples does not satisfy Correctness, Readiness, any configured Apply regression gate or Parsimony
Readiness. A later role would need an owner decision on coverage and unavailable/regression policy. Verification
requires supported positive/negative rule-removal and rule-order cases, empty-lexicon unavailability, retained
bindings, nonpassing capped/zero-example work, complete paging and advisory isolation, using external inputs
or runtime disposable fixtures under ADR 0002 rather than committed language payloads.

### D12. A person can hide a warning for themselves

**Hide This Warning** hides one finding, identified by its rule plus its object, for the person who chose it
and for no one else. Hidden warnings are listed under **Settings › Hidden Warnings**, where each can be
unhidden. The warning count in the health strip shows how many are hidden. Hiding changes only what that
person sees; it never changes the grammar, the finding or any other person's view.

## Consequences

Missing capabilities and unkept views remain explicit. Stored evidence retains precise identity while large
facts files are disposable. People can revisit suppressed advice, and autonomous drivers can opt into only
the declared hold policy without converting grammar preferences into validity rules.
