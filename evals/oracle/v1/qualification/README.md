# Judge qualification packet

Qualification asks whether the fixed pair reproduces reviewed Oracle rulings on the kinds of answers
it will grade. These synthetic examples exercise the response format and failure boundaries offline;
they do not establish that either real model has passed qualification.

## Rehearsal set

`cases.jsonl` contains 40 candidates across 20 contexts: a valid answer and one boundary or tempting wrong
answer for each. Ten contexts concentrate on affixes, MSAs, slots, templates, allomorphs, approved readings,
negative evidence and parse completion. The rest cover user needs, provenance, terminology, proportionate change, exact read-back, authority, cross-project scope,
contradictions, empty answers and answer injection. All forms and short identifiers are invented local
fixture labels, never portable entity IDs or real project data. Criteria/checklist IDs are stable here.

`anchors.jsonl` is separate control-side authored expectation data, marked `fixture`, with per-criterion
and prohibition status, decision and rationale. It contains **no** model ruling or human approval.
`fake-opus.jsonl` and `fake-sol.jsonl` supply exact quoted fixture responses to exercise the accounting.
`split-sol.jsonl` changes one criterion while retaining the same fail decision, so the pair must leave
that answer unscored. Every consultant check has a representative context.
The scorer refuses missing cases, duplicate votes, unknown IDs, invalid quotes, inconsistent decisions
and unexpected fields. Its output is always `fixture_only`, even at perfect agreement.

A disagreement on a criterion or prohibition, even when both decisions are fail, is unscored as a pair.
Missing evidence stays unscored. A human resolution is an additional trusted record; the fake scorer
reports raw pair disagreement and does not manufacture that resolution.

## Future qualification against the Oracle

1. Freeze the full recipe, exact provider-resolved models and requested/observed effort. Route Opus
   and Sol independently through the same prompt body; never replace one with a cheap judge.
2. Have the Oracle produce and attack keys, settle parser facts, escalate linguistic choices and perform
   the human spot-check rule. Accept actual keys and preserve their ruling evidence. Synthetic fixture
   labels cannot be promoted merely by changing their status field.
3. Produce fresh qualification candidates **after** freezing the Judge prompt. For the 21 current tasks,
   use two candidates per task (one valid alternative and one plausible error): **42 answers**. Add
   **16 adversarial answers**, one per consultant check, for **58 total**. This is a v1 operating budget,
   not a universal statistical gate. Oracle/human reviewers rule on these frozen answers while blind to
   candidate Judge votes. Keep those targets control-side, unavailable to the pair.
4. Assign the same hidden Arm/model labels and randomized order to both independent sessions; do not
   include those labels in prompts. Store raw votes, exact quotes, hashes and trusted session metadata.
5. Compute per-member and raw-pair agreement; report all disputes, invalid outputs and unscored targets.
   A person rules on each dispute before the pair can be used. Repair invalid evidence and regrade the
   same frozen answer. Do not rerun an agent to obtain an easier answer.

## Measures and local gate

For N accepted, answerable Oracle anchors, each member’s exact decision agreement is matching
pass/fail labels divided by **N**, retaining invalid/unscored Judge outputs as nonmatches. Exact criterion
agreement counts status matches per atomic point, reported separately by check and task family.
For prohibitions also report violations detected, false passes and false failures, per member and pair.

Raw pair coverage is agreements on every criterion/prohibition plus decision divided by N; raw pair
agreement with the Oracle is matching pair decisions divided by N. Thus excluding Judge disputes cannot
inflate agreement. Also report agreement conditional on pair coverage as a secondary descriptive number.
Oracle `unscored` anchors are excluded from pass/fail N, with their number and reasons shown; measure whether
the pair likewise abstains. Invalid outputs never count as abstentions. Report confusion counts (Oracle
pass/Judge fail; Oracle fail/Judge pass), author model, task family, and Oracle participation overlap.

Proposed local activation gate: each member and the raw pair reach **at least 95% decision agreement**;
no false pass on any critical prohibition; all quote/format defects repaired and all disputes resolved
by a person; every task family represented; the required human checks recorded. On 58 anchors, 56 exact
matches meet the percentage threshold; 55 do not. Report actual numerator and denominator, never an
inferred confidence guarantee. A member’s critical false pass fails the gate even if the pair disputes it.

If thresholds fail, keep the pair unqualified; tune on a development subset and obtain a fresh hidden
packet to requalify. Changing models, effort, prompt, checklist, task/key semantics, product or parser
requires a new qualification record and the corresponding Validity evidence. Same pair for every Arm;
if an Arm’s exact model is a member, this recipe cannot judge that comparison. No family-bias correction
or cheap fallback is presumed. The older review’s proposed 200-item statistical gate is not this lane’s spec.
