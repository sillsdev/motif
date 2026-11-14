# ADR 0115 - Same-shape additions need a rule attempt, and Parsimony runs weekly with opt-in blocking

A package that adds five or more variants of one shape must carry a recorded rule attempt; Parsimony runs weekly with a trend line; a closed subset of measures may block in autonomous mode when a project opts in; four redundancy measures are declared.

**Status:** accepted, 2026-11-13. Amends [ADR 0060](0060-parsimony-evidence-and-advisory-dispositions.md) decision 6 for opted-in projects in autonomous mode only. Follows the CEB audit of 2026-11-13 (46 CVAR and 24 CSPELL variants added with no rule attempt).

## Context

Nothing required a rule attempt before a list, so same-shape objects piled up. Parsimony ran as an event, and no measure could block. ADR 0060 keeps findings advisory, which is right in a linguist's window and left the autonomous loop with no gate.

## Decision

### 1. A rule attempt is required at five

The shape of an added variant is its variant type, environments, features and string correspondence. The correspondence is the minimal difference between main form and variant form with every vowel written `V`, an anchor (initial, medial, final) and a trailing-hyphen flag, so `angka` to `angk-` and `ayri` to `ayr-` share a shape. A package that adds five or more objects of one shape must carry a rule attempt for that shape: the rule, its prediction-test result and why it failed, all non-blank. A reviewer HOLDs a package whose large group has none. Shapes for features and tags beyond string correspondence are not defined here.

The attempt is recorded through `IRuleAttemptLedger` (`Record`, `Find` by shape key). The hypothesis ledger will back it. Until then `NoRuleAttemptLedger` finds nothing, so every large group holds. This ADR builds no store.

### 2. Blocking is opt-in, closed, and needs a measured wrong reading

`BlockingPolicy` is advisory unless a project opts a measure in. Only `R-word-disapproved-produced`, `R-word-negative-accepted` and `P-variant-same-shape` can ever block, only in autonomous mode, and the first two only when a Trial measured a wrong reading (the disable-Trial-bisect-delete method). Other measures, and any consultant-mode window, stay advisory under ADR 0060. Persisting a project's opt-in and calling `Decide` from Readiness are not done here.

### 3. Four redundancy measures

`P-tag-polarity-majority` (a tag on at least half a rule's candidate hosts), `P-tag-implied-by-context` (every carrier already matches the rule context), `P-variant-same-shape`, `P-lexeme-near-duplicate` (forms equal after NFC, case folding and iy to y). They are declared with recipes and named capabilities and are not yet executable (`not-available` in a report). The pure detectors exist in `RedundancyMeasures` and `SameShapeGroups`. `P-allo-alternation-family` is left as it is; making it conclusive is separate.

### 4. Weekly sweep

`motif parsimony --measure all --wait --trend <tsv>` appends one row per counted class after the sweep. Motif installs no scheduled task; the help page shows Task Scheduler and cron lines.

## Cost (owner rule: no analysis over one second)

| Check | Big-O | CEB-scale time |
|---|---|---|
| Same-shape grouping | O(added objects), one hash bucket per shape | 62,000 variants: part of 117 ms total |
| Near-duplicate lexemes | O(entries), hash by folded form, no pairing | 8,000 entries: part of 117 ms total |
| Polarity, implied tag | O(tag and rule rows), one pass | 1,200 tags: part of 117 ms total |
| Trend rows | O(measures) over stored reports | microseconds, no parse |

The 117 ms is one run of `CebScaleChecksRunWellUnderOneSecond` on synthetic data of CEB size (62,000 added variants, 8,000 entries, 1,200 tags), including JIT; the test fails over 1,000 ms. It was not run on the live fwdata because the measures are not wired to read it yet.

## PanGloss asks

The executable measures need counts PanGloss computes cheaply in its grammar-facts file and does not expose: per rule, the candidate-host count and, per tag, the carriers among them (`tag-rule-host-counts`); per tag and rule, how many carriers match the rule context (`tag-rule-context-match`). Lexeme forms (`lexeme-form-index`) and added-variant shapes (`added-variant-shapes`) come from Motif's own load and the Proposal. No measure re-parses a word.

## Consequences

- A same-shape list needs a rule attempt on record, or it holds.
- Nothing blocks by default, so CEB is not held unexpectedly.
- Wiring `Decide` and the ledger, and the four measures' queries, are follow-on work.
