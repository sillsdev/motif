---
name: parsimony-review
description: Use when interpreting, dispositioning, or proposing a Motif Parsimony finding, including evidence review, semantic edits, parser verification, or recording a keep, ask, or defer.
---

# Review Motif Parsimony findings

A Parsimony finding is an advisory reason to review one part of a grammar. Keep the finding's evidence, its limits, and the linguist's decision connected throughout the review.

## Read the evidence

1. Open the exact Parsimony Report with `motif parsimony show`. Confirm its Baseline, evidence artifacts, Assessment references, measure, and finding identity.
2. Read the recipe for that measure in Help or through MCP at `motif://parsimony/recipes/<measureId>`. The recipe names supported semantic actions, verification criteria, and known limits.
3. Use the Report's fixed evidence views with `motif parsimony view`. For parser findings, inspect the named `parser-cases` and confirm that the cases completed and their morph identities are available. An incomplete, missing, or unattributable case is not proof of an empty result.
4. Keep restrictiveness and parsimony as separate axes. Within the same evidence tier, consider restrictiveness before parsimony. Do not combine them into a score.

## Choose a route

- **Fix:** only use an update action named by the recipe and available as a typed semantic composer. Stage it in a Draft Proposal, run Dry Run and Preflight, then compare parser results from the current and edited scratch copies when the recipe requires them. Check every stated Approved reading, negative, control, and completeness condition. Keep the update and its `fix` disposition bound to the finding's evidence. A person decides whether to Apply.
- **Keep:** use when the evidence supports the current grammar, such as a productive extension or an intentional fallback. Record the specific reason and evidence; do not edit the grammar.
- **Ask:** state one answerable question that would distinguish the supported routes. Include the exact evidence and the relevant Approved readings; do not edit the grammar.
- **Defer:** use when attribution, identity, or an update capability is missing, or when a parser case stays incomplete after a rerun. Name the missing evidence or action and do not invent a substitute.

An incomplete parser case is missing evidence, not a decision. First rerun the same words with a longer per-word time limit; defer only if a case is still incomplete.

Stage a decision with `motif parsimony dispose` or the typed `compose-record-parsimony-disposition` command. `ask` requires a question. Review its Draft with Dry Run and Preflight; a person Applies it. A pending disposition does not suppress a live finding. It takes effect after Apply and a later Refresh.

## Boundaries

- Do not use arbitrary SQL, raw property writes, or a generated LibLCM Mutation Plan as canonical input. Use the recipe's semantic composer.
- Do not Apply, even when the linguist says you need not ask again. Stage the Proposal and leave Apply to a person; the caller owns project persistence and lifecycle.
- Do not claim that a parser-incomplete case was rejected or that a single successful fixture proves whole-grammar equivalence.
- If the recipe lists no update action, explain that limitation and ask or defer. Do not promise an unsupported fix.
