# 0117. The hypothesis ledger lives in the Motif store, and only the CLI writes it

**In plain terms:** every rule a builder proposes for a grammar gets one record: the rule, the forms it predicts, how it
tested and what was decided. Builders ask the ledger "do we already have a rule for this change, environment, host class
and slot?" before authoring anything, and the decision record and the speaker-question log are printed from it instead
of written by hand. The ledger sits in Motif's own store beside the project, never in the FieldWorks project.

**Status:** accepted, 2026-11-13. Implements motif#174. Carries the four numbers of motif#185. Builds on
[ADR 0036](0036-motif-has-its-own-data-store.md) and [ADR 0041](0041-the-database-is-the-only-store.md) (Motif's database
is the one store), [ADR 0083](0083-grammar-design-records-live-in-the-fieldworks-project.md) (GDR text stays in the
project), [ADR 0112](0112-motif-writes-only-what-fieldworks-can-create.md) and the apply principle (MCP never applies).

## Context

Rulings lived in 615 KB of chronological prose, so the process forgot them: a 5 Nov ruling forbade building an h-metathesis
alternate and prescribed an ordered rule; a builder built the alternate eight days later with no reference to it, and 25
hyphen variants of one shape were added a second time with no rule retry. The question that exposes this had no queryable
source. The loop skills (`grammar-discovery-loop`, `rule-hypothesis-test`) named a `hypotheses.jsonl` beside the project as a
stand-in.

## Decision

1. **The ledger is three tables in the Motif store** (schema 70): `Hypotheses`, `HypothesisProposals`, `HypothesisTests`.
   Not a file beside the project: the store already gives locking, upgrade carry-forward and one place per project (ADR
   0041), and a JSONL file would need its own locking and its own index. A hypothesis is authored data, so a store upgrade
   keeps all three tables (ADR 0077) the way it keeps Proposals.
2. **Nothing enters the FieldWorks project.** A hypothesis is Motif workflow data, so it cannot hold a shape FieldWorks
   cannot create (ADR 0112). The GDR is linked by id and section (`GDR-n`, ADR 0083); the GDR text stays in the project and
   no GDR text is copied into the ledger. Hypothesis rows describe a project's grammar, so they live in that project's
   store and never in the Motif repository (ADR 0076).
3. **The CLI writes it; MCP does not.** The write verbs are `HumanOnly` in the catalog and every ledger verb is
   `ExposeAsAgentTool = false`, so no MCP tool exists for any of them. They are Developer-surface commands: an agent driving the shell CLI runs `hypothesis add|test|decide|link` with `MOTIF_DEVELOPER_COMMANDS=1` set and no human step (`HumanOnly` here means never an MCP tool; the CLI dispatch gate looks only at the surface). The ledger
   records a decision; it applies nothing. A later change may expose the read verbs through MCP.
4. **One record per hypothesis, with these fields:** id (`H-n`, permanent), pattern, statement, mechanism, change,
   environment, host class and slot (the *shape*), neighbour (the check, required), predictions, falsifier, exceptions,
   size delta (`objects_added`, `attested_types_gained`), questions (with the author's private predicted answer), status
   (`proposed`, `passed`, `failed`, `superseded`), decision, `superseded_by`, GDR id and section or a stated reason for
   none, linked Proposal ids, tests, and the four numbers.
5. **The four numbers of #185** are columns on the hypothesis, filled by `hypothesis grade` as they become known: (1) the
   sealed loop's verdict against the answer key (`match` or `mismatch`) and who gave it; (2) word questions asked; (3)
   objects and attested types before and after, from which objects per attested type is derived; (4) agent hours against
   the list-package alternative.
6. **Rules the ledger enforces** (a refusal says which):
   - a hypothesis names its neighbour check: `none-found` (refused when the ledger holds the exact shape) or the ids of
     the hypotheses it widens or merges (`H-3, H-7`), each id checked to exist; free text alone is refused;
   - a PASS test needs at least 20 predicted forms, `trial_attested_losses` present and 0, and the skill thresholds
     recomputed from the hits and controls (at least 20 hits, at least 2x the best control); a contradicting PASS is refused;
   - `passed` needs a latest test of PASS and a GDR link, or a stated reason none changed (the GDR sync rule);
   - `failed` needs a recorded test whose latest verdict is FAIL or UNDECIDED;
   - the contradiction guard: a pass is refused while another hypothesis has passed on the same shape with a different
     mechanism, until the pass names it with `--supersedes`; that is how the 5 Nov ruling would have bound the ALTH variant;
   - the contradiction guard compares all four shape parts exactly, a blank part matching only a blank one;
     a hypothesis cannot supersede itself;
   - a decided hypothesis changes only by being superseded; a superseded one names its replacement.
7. **The API for later writers is the command layer.** `HypothesisCommands.RecordTest` takes the JSON that
   `control_test.py --json` prints, so the control test (#172) writes its result in one call; `HypothesisCommands.Neighbours`
   returns `HasPassed` and `HasFailedAttempt`, which is exactly the question the rule-attempt gate (#171) asks before a batch
   of five same-shape objects. Neither needs to know the tables. The CLI forms are `hypothesis add | test | decide | link |
   grade | answer | neighbours | report` and `list hypotheses`.
8. **The decision record and speaker-question log are generated** (`hypothesis report --kind decisions | questions`). The
   question log never prints the predicted answers.
9. **Proposals are linked by id and not looked up**, because Proposals can be archived or released. The link is queryable
   both ways: a hypothesis's Proposals and a Proposal's hypotheses.
10. **Seed rows are project data.** The 15 alternation rows motif#174 asks for describe CEB, so they are loaded into the
    CEB store with `hypothesis add`, never committed here.

## Performance

Owner rule: every check stays under one second and none scans the ledger. All lookups seek an index. Sizes are
for n hypotheses, k the hypotheses on one shape and t the tests or Proposals of one hypothesis.

| Operation | Cost | Index |
|---|---|---|
| append a hypothesis (next id) | O(log n) | `IX_Hypotheses_Seq` (`MAX(Seq)`), then one insert |
| get by id | O(log n) | primary key; ids are upper-cased, not `COLLATE NOCASE`, which would scan |
| neighbour check | O(log n + k) | `IX_Hypotheses_Shape`, declared `COLLATE NOCASE` on all four shape columns so equality seeks it |
| record a test | O(log n + log t) | primary key and test key |
| link Proposals and GDR | O(log n + p log n) for p Proposals | primary keys |
| Proposal to hypotheses | O(log n + h) | `IX_HypothesisProposals_Proposal` |
| decide, with the contradiction guard | O(log n + k + k log t) | the neighbour seek |
| a page of the list | O(log n + limit) | `IX_Hypotheses_Seq`, `IX_Hypotheses_StatusSeq` |
| the list's `total` | O(n) over one index, not a table scan | `IX_Hypotheses_StatusSeq` |
| `hypothesis report` | O(n log n) | on demand; never run inside a check |

Free-text search was dropped: a `LIKE` over statements cannot seek an index, and the shape fields answer the neighbour
question. `EveryLookupSeeksAnIndexAndStaysFastOnTenThousandHypotheses` checks the query plan of every lookup above (no
table scan) and times each command on a synthetic ledger of 10,001 hypotheses, with its Proposal links and tests, through
the whole command path including opening the store:

| Command on 10,001 hypotheses | Time |
|---|---:|
| add | 35 ms (131 ms on a loaded run) |
| neighbour check | 8-16 ms |
| record test | 22-39 ms |
| link Proposal and GDR | 24-34 ms |
| show one | 10-16 ms |
| decide, with the guard | 21-26 ms |
| page of fifty | 15-28 ms |
| decision report (7,500 decided; O(n)) | 461 ms |
| question log (O(n)) | 86 ms |

Opening the store accounts for most of each figure; the test fails any check at or over 1,000 ms.

## Consequences

- Schema 70; the upgrade keeps the three tables.
- `grammar-discovery-loop` and `rule-hypothesis-test` point at the CLI instead of `hypotheses.jsonl`; an existing
  `hypotheses.jsonl` is loaded with `hypothesis add --file` per record.
- The GDR sync check stays the agent's self-check (#178); the ledger only refuses a pass with no GDR link or stated reason.
- Open: whether a linguist reading the ledger after the fact is enough review (#178), and exposing the read verbs through MCP.
