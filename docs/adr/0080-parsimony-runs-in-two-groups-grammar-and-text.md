# ADR 0080 — Parsimony runs in two groups: the Grammar group just works, the Text group uses a Parsimony Selection

**In plain terms:** Some Parsimony measures only read the grammar. Others need words and Texts. Today one command mixes them, so on a project with no Default Selection six measures say "the Default Selection is not saved" and two more say they have no Assessment. This splits the measures in two. The Grammar group needs nothing from you. The Text group runs on one set of Texts you choose by hand for Parsimony, and every run reuses that set until you change it. One command runs either group or both. Parsimony stays advisory and still has no single score.

**Status:** accepted, 2026-11-01. Provisional and reversible: the owner may reverse any numbered decision. Amends [ADR 0060](0060-parsimony-evidence-and-advisory-dispositions.md) decision 2 (the Default Selection is no longer the Text scope of a Parsimony run). Decision 6 of ADR 0060 and [ADR 0079](0079-linguistic-decisions-follow-black-and-judgement-calls-are-the-linguists.md) decision 5 are unchanged.

## Context

On a private Swahili project, `parsimony --measure all --wait` needed a manual `baseline capture` first. Of 14 measures, 4 computed; 6 said "the Default Selection is not saved"; 2 abstained on incomplete facts (`P-allo-alternation-family`: 11 allomorph pairs selected under different conditions and 2 allomorphs not loaded; `R-allo-unconditioned`: 13 families with incomplete condition or compiler-order facts); 2 had no Assessment. The project had one Approved reading.

The owner's words: "If we need to break parsimony up into two groups, one that 'just works' and the other that is configured by manually selecting a set of texts to run explicitly for parsimony - that is reused on every run. We should be able to run one set or both with one command."

Reading the code shows that the catalog's evidence tier does not say what a measure reads. `B-affix-null-vs-optional` is catalogued Static but reads Approved analyses through the evidence scope. The Default Selection is also chosen for timing and coverage, not for Parsimony evidence.

## Decision

### 1. The Grammar group needs no Texts, no Selection and no Assessment

It reads only the frozen grammar facts. It captures a Baseline itself when none is current; it never recaptures a Baseline that exists, however old. A measure that really needs words belongs to the Text group. A measure that asks for a Selection out of habit stays and loses that need. Today that is nobody: five measures read no Approved words.

Context views that show Approved witnesses for a Grammar finding use the `project-approved` scope, which is always complete. That is every project wordform with an Approved or Disapproved analysis, not a Text choice.

A Grammar-group measure may still abstain on incomplete grammar facts. "Just works" means it asks for nothing; it does not mean it always computes.

### 2. The Text group runs on a Parsimony Selection

The **Parsimony Selection** is a saved set of Texts chosen by hand, used by every Text-group run until changed. It is a Selection (a named, stored word list) and reuses the stored Selection, its resolution to exact words and Text identities, and its digest. It is its own saved pointer, separate from the Default Selection, because the two have different jobs: the Default Selection is what the project parses and times, and may be large; the Parsimony Selection is the evidence a linguist chose to judge the grammar by. Changing one must not change the other. The same named Selection can be saved as both; neither is derived from the other.

The group covers the text tier and the parser tier:

- **Text tier.** Reads Approved analyses of the Selection's wordforms, as ADR 0060 decision 2 already says of its scope.
- **Parser tier.** Cites an Assessment of the Parsimony Selection's words under the current Baseline. A run finds a stored complete Assessment with that Baseline token and Selection digest, and reuses it. When none exists it asks for a real parse of the Parsimony Selection through the ordinary Assess job and cites that Assessment. No placeholder Assessment is created ([ADR 0060](0060-parsimony-evidence-and-advisory-dispositions.md) decision 6). An explicit `--assessment` still overrides.

Reviewed negatives and Disapproved readings whose words are outside the Parsimony Selection are not parsed. The parser measures report them as not checked, by count, and do not widen the Selection.

### 3. One command runs either or both

`parsimony --group grammar|text|all` on the CLI, and `group` on the MCP tool, take one shared definition of the groups, so the two surfaces cannot disagree. `--group` combined with a named `--measure` is refused when the measure is outside the group. `all` runs both groups.

If the Parsimony Selection is not configured, `all` runs the Grammar group and says once, in plain words, how to configure the set. That is a note, not an error. `--group text` alone with no set configured is refused with the same words and the Grammar group is not run.

### 4. Source-validated decisions are not human Approved evidence

An agent's `RecordSourceValidatedDecision` (T-6c) is staged under a named non-human agent and read back for Proposal Differences (T-6d). The Parsimony evidence projection reads only FieldWorks `HumanApprovedAnalyses`. Those decisions therefore do not enter the Text group, and they must not be added to the `approved` opinion: ADR 0060 decision 3 says an agent cannot create a human judgment, and an agent's approval must stay distinguishable from a person's. A later change may show them as separately labelled agent evidence on a finding, never as Approved and never as a reviewed negative.

### 5. Parsimony stays advisory

Groups change what a run reads, not what a finding means. Findings, thresholds and dispositions still do not change Readiness, Apply Authorization or Apply, and there is no single score and no per-group score.

## The 14 measures

Grammar group (5): `P-adhoc-duplicate`, `P-allo-duplicate-form`, `P-statement-unused`, `R-allo-unconditioned`, `P-allo-alternation-family`.

Text group (9): text tier `B-adhoc-is-slot-order`, `B-affix-null-vs-optional`, `B-affix-unslotted`, `R-env-broad`, `R-nc-excess`, `R-slot-blocking`, `R-tmpl-precedence`; parser tier `R-word-disapproved-produced`, `R-word-negative-accepted`.

| Measure | Group | What it reads |
|---|---|---|
| `P-adhoc-duplicate` | Grammar | ad hoc prohibitions and their loader state |
| `P-allo-duplicate-form` | Grammar | allomorph forms and environment lists |
| `P-statement-unused` | Grammar | statement usage and referrers |
| `R-allo-unconditioned` | Grammar | compiled allomorph order and phone conditions |
| `P-allo-alternation-family` | Grammar | allomorph pairs, compiled gates, phoneme tokenization, loaded rules |
| `B-affix-null-vs-optional` | Text (was catalogued Static) | zero-realization MSAs, plus Approved analyses of each MSA |
| `B-affix-unslotted` | Text | unslotted inflectional MSAs, plus Approved morph sequences |
| `R-tmpl-precedence`, `R-slot-blocking` | Text | templates and slots, plus Approved morph sequences |
| `B-adhoc-is-slot-order` | Text | ad hoc prohibitions, templates, plus Approved morph sequences |
| `R-env-broad`, `R-nc-excess` | Text | environments and classes, plus contexts observed in Approved analyses |
| `R-word-negative-accepted`, `R-word-disapproved-produced` | Text (parser) | reviewed negatives and Disapproved readings against an Assessment's parser cases |

The two Grammar-group abstentions are grammar facts, not missing words. `P-allo-alternation-family` sets aside pairs whose allomorphs the parser selects under different conditions (a stem name or MSA gate) and leaves out allomorphs the parser does not load; it needs the comparison made within each gate, and the report should name the unloaded allomorphs. `R-allo-unconditioned` sets aside a family when a compiled output has several source allomorphs or an unknown abstract flag, or the orders disagree; it needs the report to list each such family with its reason, and the facts to publish what is missing. Neither is a reason to move the measure.

## Consequences

- A project with no Selection and no Assessment gets five Grammar-group reports from one command and no refusal.
- `MeasureDefinition` gains a group, derived from what the measure reads. A test runs every Grammar-group measure against a scope that is unavailable and fails if one throws a scope refusal, so the grouping cannot drift from the code.
- The store gains a saved Parsimony Selection pointer. Opening an older store carries it forward under [ADR 0077](0077-project-store-upgrades-carry-authored-data.md); computed Reports are recomputed.
- The Parsimony window belongs to the GUI lead and is not changed by this decision.
- The `parsimony` default of `P-adhoc-duplicate` with the Default Selection scope is replaced by a group default.
