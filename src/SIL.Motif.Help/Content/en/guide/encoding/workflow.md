# Work through one grammar question

Start by learning what the saved project and words already show. Make one small Proposal, try it against observed examples and counterexamples, and hand the measured result to the linguist.

## Inspect

- Use `motif_list_projects`. If the intended project is not Known, ask for its exact existing `.fwdata` path if
  needed, call `motif_open` with that path, then pass it as `project` on later calls. Use the Known project name or
  recorded path for other calls.
- Read `motif_overview`, then `motif_grammar` and `motif_lexicon` for the relevant objects and forms.
- Use `motif_word` for the linguist's recorded analysis and `motif_try_word` to see how the current grammar handles a form.
- Read the guide for the phenomenon: `motif_guide` over MCP, or `motif help guide:encoding/<topic> --full` from the CLI (`motif help` lists the topics). Check whether Motif has a composer for every object the encoding needs.

## Draft one small Proposal

- State one linguistic claim and the project evidence for it.
- Use `motif_start_proposal` and the available semantic composers that fit the claim. New grammar objects include parts of speech, lexical entries with one sense and MSA, inflection features and classes, stem names, affix slots and templates, plus phonemes, natural classes, environments and phonological rules.
- Use `motif_compose_batch` to stage related intents together. Supply each as a `verb` and structured `intent` object, with an optional `citation`. Use entity ids returned by earlier completed calls or batches; an intent that needs a newly created entity's returned id belongs in a later call or batch.
- Keep the Proposal to one related change. If a required composer is missing, say which encoding cannot be made and why; do not approximate it with unrelated edits.

## Try it, then ask or defer

- Run `motif_dry_run`, then `motif_trial` with attested positive forms, contrasting forms and words that should not parse.
- Read the Trial summary in order: negatives now parsing, positives lost, unfinished cases, other changes, counts.
- Use `motif_difference` with the stored Difference id and category to page through exact before and after analyses. Unfinished cases prove no absence.
- If evidence leaves competing analyses that change the Proposal, ask one question using attested forms. Defer when the linguist cannot yet answer it.
- Revise and repeat the Trial after a meaningful change. Do not describe an unmeasured change as a parser improvement.

## Hand off

- When the Dry Run and Trial support the change, call `motif_finalize_proposal` and tell the linguist what evidence was checked and what remains uncertain.
- The linguist reviews the Proposal in Motif and decides whether to apply it. Do not claim that it was applied.

## CLI equivalents

The guides name MCP tools. From the CLI, use the matching `motif` command; run `motif help <command> --full` for its flags.

| MCP tool | CLI command |
|---|---|
| `motif_list_projects`, `motif_open` | `motif open <fwdata>` |
| `motif_overview` | `motif overview` |
| `motif_grammar`, `motif_lexicon` | `motif grammar read --kind <kind>` (`lexical_entry` for the lexicon) |
| `motif_texts` | `motif texts list` |
| `motif_word` | `motif word-context --word <word>` |
| `motif_try_word` | `motif trace --word <word>` |
| `motif_start_proposal` | `motif new --draft <name>` |
| the add and edit tools, such as `motif_add_phonological_rule` or `motif_edit_allomorph_condition` | the `compose-author-*` and `compose-edit-*` commands, such as `motif compose-author-phonological-rule` |
| `motif_compose_batch` | `motif compose-batch` |
| `motif_finalize_proposal` | `motif finalize` |
| `motif_dry_run`, `motif_trial` | `motif dry-run`, `motif trial` (add `--wait` to read the result) |
| `motif_difference` | `motif difference produce` |
| `motif_guide` | `motif help guide:encoding/<topic> --full` |

