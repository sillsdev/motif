# ADR 0116 - Same-shape additions need a rule attempt, and Parsimony runs weekly with opt-in blocking

A package that adds five or more variants of one shape must carry a recorded rule attempt; Parsimony runs weekly with a trend line; a closed subset of measures may block in autonomous mode when a project opts in; three redundancy measures run and one waits on PanGloss.

**Status:** accepted, 2026-11-13. Amends [ADR 0060](0060-parsimony-evidence-and-advisory-dispositions.md) decision 6 for opted-in projects in autonomous mode only. Follows the CEB audit of 2026-11-13 (46 CVAR and 24 CSPELL variants added with no rule attempt).

## Context

Nothing required a rule attempt before a list, so same-shape objects piled up. Parsimony ran as an event, and no measure could block. ADR 0060 keeps findings advisory, which is right in a linguist's window and left the autonomous loop with no gate.

## Decision

### 1. A rule attempt is required at five

The shape of an added variant is its variant type, environments, features and string correspondence. The correspondence is the minimal difference between main form and variant form with every vowel written `V`, an anchor (initial, medial, final) and a trailing-hyphen flag, so `angka` to `angk-` and `ayri` to `ayr-` share a shape. A package that adds five or more objects of one shape must carry a rule attempt for that shape: the rule, its prediction-test result and why it failed, all non-blank. A reviewer HOLDs a package whose large group has none. Shapes for features and tags beyond string correspondence are not defined here.

The attempt is read through `IRuleAttemptLedger`, which has one read-only method, `Find` by shape key, because only the CLI writes the ledger (ADR 0117). `HypothesisRuleAttemptLedger` backs it with the hypothesis ledger: a hypothesis answers a group when its status is `passed` or `failed` (each needs a recorded test) and its `change` is the group's full shape key, or its string correspondence (such as `V>@final`) with an environment that is blank or the group's environment ids. A passed or a failed attempt clears the hold for that shape; a proposed or superseded hypothesis does not. The lookup is one seek on the ledger's shape index per group, O(log n + k). This ADR builds no store.

Where it runs:

- **Trial review.** At capture, `VariantFormCapture` reads from the Baseline the lexeme forms of the entries and senses the Proposal's `createVariantRef` operations name (O(variant links) object lookups) and freezes them, with the opt-in list, in `FrozenReviewInputs`. The publisher then groups the variants from the operations (`VariantOperationShapes`, O(operations)) and writes `summary.ruleAttempts`. It is a report line like the gain gate; Apply does not read it.
- **Plan file.** `motif parsimony --project <fwdata> --rule-attempts --plan <intents.json>` runs the same check over a `compose-batch` intents file, reading main forms from the current Baseline.
- **Readiness.** The Trial review summary is the one place the check is attached; no separate Readiness field was added, as with the ADR 0114 gain gate.

### 2. Blocking is opt-in, closed, and needs a measured wrong reading

`BlockingPolicy` is advisory unless a project opts a measure in. Only `R-word-disapproved-produced`, `R-word-negative-accepted` and `P-variant-same-shape` can ever block, only in autonomous mode, and the first two only when a Trial measured a wrong reading (the disable-Trial-bisect-delete method). Other measures, and any consultant-mode window, stay advisory under ADR 0060.

The opt-in lives in `<project>.motif.toml`, like ADR 0114's `[gain-gate]`, and is frozen into the Trial inputs:

```toml
[parsimony]
block-on = ["P-variant-same-shape"]
```

`fold = ["iy>y"]` in the same table sets the near-duplicate fold rules. `config show` prints both. A missing table means advisory and no fold. An id that cannot block is a configuration error naming its line. The Trial report sets `ruleAttempts.hold` only for an opted-in project with a group that has no complete attempt; the other two block-capable measures are wired to `Decide` by their callers when autonomous Apply gates on a measured wrong reading, which this ADR does not add.

### 3. Redundancy measures

Three run from the grammar-facts file the sweep already writes, with one query each and no parse:

| Measure | Reads | Big-O |
|---|---|---|
| `P-lexeme-near-duplicate` (forms equal after NFC, case folding and iy to y; exact repeats stay with `P-entry-duplicate`) | lexeme form per entry | O(entries), one hash bucket per folded form |
| `P-variant-same-shape` (five or more existing variants of one shape) | variant links, both forms, phone environments | O(variant links), one hash bucket per shape |
| `P-tag-polarity-majority` (partial: a tag that at least half of five or more stems in a rule's or affix's category scope carry; hosts ignore the rule's phonological context) | stems per category, tag carriers per category, rule and affix requirements | O(MSAs + rules), three grouped reads |

`P-tag-polarity-majority` is marked partial in the catalog (`MeasureDefinition.Partial`) and in every run's detail. It counts hosts by category scope only, so a rule selected by the form of the stem, such as d>r after a vowel or a word-initial cluster, is not detected: on CEB it cannot fire on either case, and no finding is not a clean result (`TagPolarityIsPartialAndDoesNotDetectARuleThatOnlyItsFormContextSelects`). Context-aware hosts need PanGloss's `carriers_matching_context` and a separate design.

`P-lexeme-near-duplicate` folds case and Unicode form, plus the rules the project declares in `[parsimony] fold`, each `from>to`. There is no built-in rule; Cebuano's setting is `fold = ["iy>y"]`. A reused Report keeps the fold it was computed with until the Baseline is refreshed.

`P-tag-implied-by-context` stays declared and `not-available`. It needs, per tag and rule, how many carriers match the rule context; that is a pattern match over every carrier and PanGloss already holds the compiled context (see the PanGloss ask). `P-allo-alternation-family` is left as it is.

### 4. Weekly sweep

`motif parsimony --measure all --wait --trend <tsv>` appends one row per counted class after the sweep. Motif installs no scheduled task; the help page shows Task Scheduler and cron lines.

### 5. Compatibility

`ParsimonyGroupKind` gained `tag-polarity`, `tag-implied-by-context`, `variant-same-shape` and `near-duplicate-lexemes`. They are appended with explicit wire names, so an older report's text and ordinals are unchanged and still read (`GroupKindNamesWrittenBeforeTheNewKindsStillReadAndTheNewOnesRoundTrip`). The reverse is not true: a report holding a new kind is not readable by a build that predates this ADR, because the group-kind converter is closed. `ReviewSummary.ruleAttempts` is optional and omitted when absent, so older reports read with it null (`AReportWrittenWithoutRuleAttemptsReadsWithNull...`).

## Cost (owner rule: no analysis over one second)

Measured on a read-only copy of the live CEB grammar-facts file (24,992 entries, 3,631 variant links, 91 tag requirements), `RedundancyMeasureRealFactsTimingTests` with `MOTIF_REAL_FACTS` set, load and measure apart (milliseconds, one cold run):

| Measure | Load | Measure | Groups found |
|---|---|---|---|
| `P-lexeme-near-duplicate` | 121.0 | 84.1 | 129 groups, 150 redundant entries |
| `P-variant-same-shape` | 89.1 | 35.9 | 88 groups, 2,120 redundant variants |
| `P-tag-polarity-majority` | 62.1 | 3.4 | 0 |

Each is well under one second. On synthetic data at CEB size (8,000 entries, 1,200 stems, 60 rules) `RedundancyMeasuresAtCebScaleEachRunInUnderASecond` asserts the same bound. The Trial rule-attempt check is O(Proposal operations) plus one object lookup per variant link at capture; the trend rows are O(measures) over stored reports. No check parses a word or compares entries pairwise.

## PanGloss asks

`P-tag-implied-by-context` needs, per tag and rule, how many of the tag's carriers satisfy the rule's context. PanGloss compiles every rule context and holds each stem's form, so it can emit one `(tag, rule, carriers, carriers_matching_context)` row in the grammar-facts file at no extra parse; Motif would otherwise match patterns itself, which is the slow hand-rolled version the owner rule forbids. Everything else here reads tables PanGloss already writes.

## Consequences

- A same-shape list needs a rule attempt on record, or the Trial review reports it; an opted-in project's autonomous loop holds.
- Nothing blocks by default, so CEB is not held unexpectedly. CEB's own facts show 88 same-shape groups among existing variants, so opting in changes only future additions.
- The hypothesis ledger backs the rule-attempt lookup (ADR 0117), so an opted-in hold can clear: record a hypothesis on the shape, test it, and decide it passed or failed.
