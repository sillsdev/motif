# Work through one grammar question

Start by learning what the saved project and words already show. Make one small Proposal, try it against observed examples and counterexamples, and hand the measured result to the linguist.

## Inspect

- Read `motif_overview`, then `motif_grammar` and `motif_lexicon` for the relevant objects and forms.
- Use `motif_word` for the linguist's recorded analysis and `motif_try_word` to see how the current grammar handles a form.
- Read the guide for the phenomenon. Check whether Motif has a composer for every object the encoding needs.

## Draft one small Proposal

- State one linguistic claim and the project evidence for it.
- Use `motif_start_proposal` and only the available `motif_add_*` composer that fits the claim.
- Keep the Proposal to one related change. If a required composer is missing, say which encoding cannot be made and why; do not approximate it with unrelated edits.

## Try it, then ask or defer

- Run `motif_dry_run`, then `motif_trial` with attested positive forms, contrasting forms and words that should not parse.
- Compare the result to both the intended analysis and the changes it could wrongly allow.
- If evidence leaves competing analyses that change the Proposal, ask one question using attested forms. Defer when the linguist cannot yet answer it.
- Revise and repeat the Trial after a meaningful change. Do not describe an unmeasured change as a parser improvement.

## Hand off

- When the Dry Run and Trial support the change, call `motif_finish_proposal` and tell the linguist what evidence was checked and what remains uncertain.
- The linguist reviews the Proposal in Motif and decides whether to apply it. Do not claim that it was applied.
