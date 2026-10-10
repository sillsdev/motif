# ADR 0052 — Parsimony evidence and advisory dispositions

Motif offers specific grammar recommendations with the examples and questions needed to assess them, and remembers why a person kept a particular statement. Stronger evidence comes before elegance, a narrower statement is preferred only when known valid readings survive, and relevant new evidence brings a recommendation back for another look.

**Status:** accepted, 2026-10-05.

## Context

A linguist needs to know what a recommendation is about, which examples support it, and whether the evidence is strong enough to act on. One grammar score cannot answer those questions and can hide that a measure never had the evidence it needed.

The owner has settled the name *Parsimony review*, the two axes, Motif’s ownership of recommendations, and the use of separate derived grammar-facts and project-evidence artifacts. This ADR records how those decisions govern evidence, ordering, human input, and Reports. It follows [ADR 0035](0035-reports-are-advisory-queries-over-stored-assessments.md), [ADR 0041](0041-the-database-is-the-only-store.md), and [ADR 0042](0042-a-job-produces-assessments-an-assessor-makes-them.md).

## Decision

### 1. Findings name their evidence tier and axis

A recommendation says what it saw and how strong that evidence is, rather than turning different concerns into a single score.

- **Static** evidence describes authored or loaded grammar structure. It may identify a candidate to inspect, but does not by itself say the grammar is wrong.
- **Text-grounded** evidence connects grammar facts to exact captured Text occurrences and human-approved analyses.
- **Parser-in-the-loop** evidence cites the completed or observed parser cases and their actual Assessment. A parser facts capture alone is not an Assessment.

Findings name one or both axes. **Parsimony** concerns fewer or less-duplicated statements. **Restrictiveness** concerns the narrowest statement that still covers known valid data and limits explicitly forbidden readings. Breakage remains grammar-health evidence, not a Parsimony axis.

When findings can be compared, order evidence tier before axis: parser-in-the-loop, then text-grounded, then static. Within one tier, put restrictiveness before parsimony. A finding on both axes appears once in the restrictiveness position. This ordering does not claim that two different measures or denominators are numerically comparable.

### 2. Every count has a declared local universe

A count is useful only when a linguist can see what was eligible to be counted.

Each measure declares its tier, axis, unit, threshold version, scope, finite universe, and denominator. A finding may describe a project rollup, a group, or one item; group and project totals do not erase the item identities underneath them. Token, wordform, reading, lexeme, and grammar-object counts remain distinct.

The saved Default Selection is the default Text scope and is resolved to exact words and Text identities when work is enqueued. An eligible measure may explicitly use `project-approved`; its output names that universe. All captured Approved analyses remain safety witnesses for a recipe even when its detector uses a narrower Selection.

Absence from a Selection means “not observed.” A Disapproved analysis is a negative for that reading, not for the whole word. A reviewed negative is a human-confirmed forbidden case. Unattested extension is descriptive unless the measure has a declared finite universe and explicit negative or held-out evidence.

### 3. Human judgments are FieldWorks project data

A person's judgment must survive rebuilding every analysis artifact and opening the same FieldWorks project on another machine.

Reviewed negatives and Parsimony dispositions are durable data in the FieldWorks project. Their Notebook representation, editing surface and revision history are defined by [ADR 0056](0056-human-judgments-belong-to-the-fieldworks-project.md). This ADR does not add negative or disposition tables to `Project.motif.db`.

The analysis artifacts do not store durable human judgments. Reports use exactly bound captured Notebook projections under ADR 0056; those readers are subsequent implementation work. Synthetic negatives are fixture inputs, not human judgments. An agent may suggest a probe but cannot claim a human-confirmed negative or act as a person.

A disposition is `fix`, `keep`, `ask`, or `defer`, attached to the exact finding and relevant evidence digest. `fix` records intent and does not create a Proposal or apply it. `keep` may carry a reason; it moves only that item, under that evidence, from the active list to a Suppressed set that can always be listed and revisited. `ask` records a question without sending or answering it. `defer` keeps the item for later. A relevant evidence change brings the recommendation back; an unrelated Text change or publication time does not.

An MCP disposition writer is not authorized by this decision. Any agent annotation permission must be a separate, narrow opt-in; an agent must remain identified as an agent and must not create human judgments, change FieldWorks opinions, Apply, or grant Readiness.

### 4. A recommendation has stable identity and versioned support

A person should be able to tell whether yesterday’s recommendation is the same issue with new evidence or a different issue.

Finding identity uses the measure, attachment kind, exact item identity, and canonical group identity when present. A group finding does not receive an invented object GUID. The evidence digest is separate from the finding identity and covers the relevant facts, witnesses, counterexamples, scope, measure and threshold versions, and judgment revisions. Timestamps, display labels, unrelated Texts, page limits, and artifact publication time do not change it.

Each finding names a versioned recipe or says why a safe update is unavailable. Recipes may use only supported Motif semantic operations. A recipe that needs FieldWorks work, an unanswered question, or a missing operation says so before anyone edits the grammar. Evidence queries use fixed named views with typed arguments; callers cannot supply SQL or expressions.

Verification checks the exact intended item and preserves the known valid readings and reviewed negatives in its declared universe. Incomplete parser searches cannot prove that a reading is absent. Verification produces evidence and advice; it never changes an Opinion or performs Apply.

### 5. Parsimony Reports use frozen, disposable analysis artifacts

A saved Report must keep describing the files and language data it was computed from after either changes.

PanGloss’s `grammar-facts.sqlite` and Motif’s `evidence.sqlite` are separate, versioned, derived files. Each is bound to the full Baseline token, model fingerprint, input kind, and candidate identity where applicable. Candidate scratch evidence has its own identity and never replaces Baseline evidence. File byte digests live in a Motif-store bundle descriptor because a file cannot contain its own final byte digest.

The files are disposable and rebuildable. Motif retains the Report’s frozen input bindings, measure and threshold versions, rendered findings, and limitations in its project workflow records. It may refuse or rebuild an unsupported artifact; it does not migrate an old shape. Human judgments remain in FieldWorks project data.

### 6. Parsimony Reports do not gate work

A grammar recommendation helps a person decide what to inspect; it cannot decide whether a change may be applied.

A Parsimony Report may use a frozen evidence bundle without any Assessment. Parser-in-the-loop findings cite the exact stored Assessments that supplied their cases. No dummy Assessment is created to satisfy a Report input.

Parsimony Reports are always advisory. Their findings, thresholds, and dispositions do not change Readiness, Apply Authorization, or Apply behavior. Existing configured regression behavior for other Report kinds remains as recorded by [ADR 0042](0042-a-job-produces-assessments-an-assessor-makes-them.md).

## Consequences

- A missing capability is reported by name; an empty table or absent parse cannot masquerade as a measured zero or a rejected reading.
- Durable human input remains with the FieldWorks project. ADR 0056 settles its concrete shape; initialization and pinned persistence proof must precede product writes.
- Static and Text-grounded Parsimony Reports do not need a parser run. Parser evidence always remains attributable to its real Assessment.
- Parsimony can point to supported Proposal authoring, but it cannot author arbitrary properties, alter Opinions, or Apply.
