# 0011. The hypothesis ledger lives in the Motif store; the CLI writes it

**Status:** Accepted, 2026-11-14.

## Context

A builder needs to query prior rules, predictions, tests and rulings before adding another explanation of
the same pattern. Chronological prose cannot enforce that memory; a project-scoped ledger can.

## Decisions

### D1. Three tables in the paired Motif store are the ledger

`Hypotheses`, `HypothesisProposals` and `HypothesisTests` are authored workflow data, introduced at schema
70. Store upgrades retain all three. A separate `hypotheses.jsonl` would duplicate locking and indexing;
it is not a store. An existing file is loaded record by record with `hypothesis add --file`. The one-store
contract belongs to ADR 0023.

### D2. Workflow hypotheses never enter the FieldWorks project or program repository

Link the Guide by its stable `GDR-n` ID and section; copy no Guide text into the ledger. The Guide stays in
FieldWorks ([ADR 0005](0005-the-grammar-guide.md)). Hypotheses describe a particular grammar and remain in
that project's Motif store, never the Motif repository
([ADR 0002](0002-grammar-data-never-belongs-in-motif.md)). Seed rows, including language-specific alternation
rows, are loaded through the CLI rather than committed. No non-FieldWorks shape is written into a project
([ADR 0003](0003-motif-writes-only-what-fieldworks-can-create.md)).

### D3. Only the CLI and shared command layer write the ledger

All ledger verbs are Developer-surface with `ExposeAsAgentTool = false`; writes are HumanOnly in the
catalog. No ledger MCP tool exists. An agent driving the shell may run these commands with
`MOTIF_DEVELOPER_COMMANDS=1`, without a human step: HumanOnly excludes MCP, while CLI dispatch checks the
surface. A ledger decision Applies nothing. Read exposure to MCP is a later decision.

Commands are `hypothesis add | test | decide | link | grade | answer | neighbours | report` and
`list hypotheses`. Later writers call the command layer, not the tables.
`HypothesisCommands.RecordTest` accepts `control_test.py --json` output; `HypothesisCommands.Neighbours`
returns `HasPassed` and `HasFailedAttempt` for the same-shape rule-attempt check before a batch of five
objects. It does not introduce an MCP Apply route.

### D4. One permanent H-n record carries the hypothesis and its evidence

Fields are id; pattern; statement; mechanism; change, environment, host class and slot (the four-part
shape); required neighbour check; predictions; falsifier; exceptions; size delta (`objects_added`,
`attested_types_gained`); questions with private predicted answers; status (`proposed`, `passed`, `failed`,
`superseded`); decision; `superseded_by`; Guide id/section or stated reason for none; linked Proposal ids;
tests; and the four grading numbers.

### D5. Grade records four measurements as they become known

`hypothesis grade` fills: sealed-loop answer-key verdict (`match`/`mismatch`) and its giver; word questions
asked; objects and attested types before/after, deriving objects per attested type; and agent hours versus
the list-package alternative. Missing measurements are not manufactured.

### D6. Neighbour, test and contradiction guards refuse inconsistent decisions

A neighbour check is `none-found`, refused when that exact shape exists, or the existing hypothesis IDs
being widened/merged. Validate each ID; free text is insufficient. Compare all four shape parts exactly,
with blank matching only blank. A PASS test requires at least 20 predicted forms, present and zero
`trial_attested_losses`, and recomputed skill thresholds: at least 20 hits and at least twice the best
control. Refuse a contradictory PASS.

`passed` requires the latest test to be PASS and a Guide link or a stated reason none changed. `failed`
requires the latest recorded verdict FAIL or UNDECIDED. Refuse a pass when a different mechanism already
passed on the same shape until `--supersedes` names it. Self-supersession refuses. A decided hypothesis
changes only by being superseded; a superseded record names its replacement.

### D7. Reports and links are derived from this record

`hypothesis report --kind decisions | questions` generates the decision record and speaker-question log;
the latter never prints predicted answers. Proposal links store IDs without requiring Proposal lookup,
because Proposal artifacts can be released or archived. Links are queryable in both directions. The Guide
check is advisory; this ledger enforces only the passed-record link-or-reason requirement, not an absence
of Guide findings.

### D8. Every check seeks an index and stays below one second

No check scans ledger text. IDs are uppercased for primary-key lookup, not searched with an ad hoc
`COLLATE NOCASE`. Shape equality uses `IX_Hypotheses_Shape`, declared case-insensitive on all four columns.
No free-text `LIKE` search is added.

| Operation | Cost | Index/path |
| --- | --- | --- |
| Append / next id | O(log n) | `IX_Hypotheses_Seq`, MAX(Seq), one insert |
| Get by id | O(log n) | Primary key |
| Neighbours | O(log n + k) | `IX_Hypotheses_Shape` |
| Record test | O(log n + log t) | Primary key and test key |
| Link p Proposals / Guide | O(log n + p log n) | Primary keys |
| Proposal to h hypotheses | O(log n + h) | `IX_HypothesisProposals_Proposal` |
| Decide / contradiction | O(log n + k + k log t) | Neighbour seek and test keys |
| List page | O(log n + limit) | `IX_Hypotheses_Seq`, `IX_Hypotheses_StatusSeq` |
| List total | O(n) over one index | `IX_Hypotheses_StatusSeq`, not a table scan |
| Report | O(n log n) | On demand, never inside a check |

Here n is the hypothesis count, k the matching shape count, t a hypothesis's tests. The list total and
on-demand reports are explicit linear exceptions, not permission for a lookup scan.
`EveryLookupSeeksAnIndexAndStaysFastOnTenThousandHypotheses` checks lookup query plans and commands through
store-open on 10,001 synthetic hypotheses with links/tests, failing any check at or above 1000 ms.

## Open

Is a linguist reading the ledger after the fact sufficient review? Should read verbs become MCP tools?
Neither is decided by exposing the shell CLI to a builder.

## Consequences

Builders can query earlier passed and failed explanations before repeating them. The ledger, generated
reports and contradiction guard share one source; project text and project authority remain separate.
Workflow history is authored data preserved by upgrades, while machine-specific performance samples are
not a new correctness threshold beyond the one-second check budget.
