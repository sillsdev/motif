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

## A stem two obligatory rules both want

Phonological rules in FieldWorks are obligatory, and there is no optional rule and no per-stem rule order. When two rules each need their own exception feature and the first removes what the second needs (syncope bleeding a d-to-r rule), a stem tagged for both surfaces only the first outcome. This is Black's route, not an optional rule: exception features that gate rules (Black 2.1.6, and 6.1.2.2.2, "Rule applies only with certain properties"), tried before any ad hoc rule. Give the stem one analysis per outcome, each tagged for one rule only:

1. Tag the existing analysis for the first rule with `motif_edit_exception_features` (`compose-edit-exception-features`).
2. Add a second analysis tagged for the other rule with `motif_add_morphosyntax_analysis`, giving `msa.exceptionFeatures` and a `gloss` for its sense. The original sense stays on the original analysis and the gloss adds a second sense, so an entry with one sense ends with two; each analysis needs a sense of its own, because a sense points at exactly one analysis and FieldWorks removes an analysis no sense uses; one sense cannot serve both. Make the gloss say what the sense is for, in the form `<original gloss> (second analysis: <rule or form it carries>)`, for example `to open (second analysis: d-to-r form)`. FieldWorks shows this sense in the entry beside the original, and a person can merge or reword it there. Do not move the only sense with `senses`: the old analysis would be dropped and the split becomes a retag.
3. Run a Trial with both surface forms and a word neither rule should make. Split only stems whose second form is attested: each split costs one more parse path for that stem.

A form that may surface either way takes a third analysis with no tag. A whole-word variant listed as its own entry also works, but it misses the productive forms built on the stem.

## Name an object by its form, not its id

An id is a GUID, and an object a Proposal creates gets a new one every time that Proposal is composed, so a plan written against one project (a private copy, say) names ids that do not exist in another (the live project). Any string in an intent that starts with `lookup:` is read as a name and replaced by the id of the object in the project the Draft is composed against, including one an earlier step of the same Draft created: `lookup:entry:ika-` (an entry by lexeme form, with the affix hyphen to pick the affix; add `|gloss` to separate homographs), `lookup:sense:FORM|GLOSS`, `lookup:msa:FORM|GLOSS` (the sense's grammatical info), `lookup:phoneme:GRAPHEME` and `lookup:category:NAME`. A name that matches nothing, or more than one object, is refused with the ids of the objects it matched. Stored Drafts keep the resolved ids, so a Draft replays the same way afterwards.

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

## Composer notes

- **Exception features are set as a whole list.** `compose-edit-exception-features` replaces the exact list on a stem or an inflectional affix. To add a feature to an entry that already has some, read its current features first and send the merged list: the old ones plus the new one. Sending only the new one removes the rest. `compose-add-exception-features` adds without replacing, to stems, inflectional affixes and derivational affixes (From Exception Features by default, To with `"side":"to"`).
- **Bulk retype uses `compose-batch`.** One file with an intent per stem (`compose-set-stem-part-of-speech`) stages hundreds in one Draft and one Proposal. `grammar-check` lists the likely stems as `lexicon.stem-typed-as-content-word`, with duplicate entries and affixes whose hyphen is on the wrong side; the report changes nothing.
- **Reaching an object made earlier in the same batch.** Give the step an `as` label, then write `@label` as a whole value in a later step in place of the id the first step created. If the step created several objects, name the one you mean: `@label:msa`, `@label:senses`, `@label:codes`. A phonological rule step resolves `@label` only in `placement.after` and `placement.before` of its own intent; a later step that needs the rule it created names it with `@label:phonrules`.
- **There is no `@FEAT:` form.** A placeholder written `@FEAT:name` is not recognised: it stays as literal text and is then refused because it is not an id. A feature made in the same batch is reached by an `as` label on its `compose-author-exception-feature` step and `@label`. An existing feature has no placeholder at all: read its id from the project (`motif grammar read`) and write the id.
- **An allomorph shares its entry's analysis.** `compose-author-affix-process-allomorph` adds a form to the entry and does not create a second analysis, so one allomorph cannot carry a different tense or other label than the entry. To label two forms differently, make two entries, or add a second analysis with `compose-add-morphosyntax-analysis`.
- **Do not parse does not stop FieldWorks parsing.** Motif can mark an entry do-not-parse, but FieldWorks' HermitCrab, its XAmple transform and PanGloss all parse a flagged entry, so the flag is not a way to retire one. To stop a stem parsing, retype it with `compose-set-stem-part-of-speech` or delete it (see `compose-set-entry-do-not-parse`).

## Common wrong choices

- Reaching for an affix process, ad hoc prohibition or batch when a typed composer fits the claim.
- Making one large Proposal for several grammar questions.
- Describing an unmeasured change as an improvement.
