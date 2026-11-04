# Delete an entry

Delete one whole dictionary entry together with its lexeme form, alternate forms, senses, examples, grammatical info and entry references. Use it when the entry is redundant, for example a duplicate whose forms now live on another entry, or an old entry a new one replaces.

```sh
motif compose-delete-lexical-entry --project language.fwdata --draft redundant-entries \
  --intent '{"entry":"<entry-id>"}'
```

`entry` is required and the intent is closed.

The command refuses, and changes nothing, when any object outside the entry refers to the entry or to anything it owns: a variant or complex-form reference in another entry, a lexical relation, a morph bundle of a parser analysis, an ad hoc prohibition, or a reversal entry. The message names the referrers; remove or retarget each reference first, for example with `compose-link-variant` or `compose-remove-alternate-form`. References between the entry's own parts, such as a sense that uses one of its analyses, are not referrers.

The Dry Run lists the entry's headword, its lexeme form and alternate forms with their classes, text in every writing system and morph type, its senses with gloss and definition, its analyses, and the number of entry references, so a reviewer sees exactly what goes. A Draft may delete several entries, and may delete an entry and author its replacement in the same Draft.

Use this editor through the CLI. Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite every operation this command produces. Review the Dry Run effects before Finalize; only a person can Apply the Proposal.
