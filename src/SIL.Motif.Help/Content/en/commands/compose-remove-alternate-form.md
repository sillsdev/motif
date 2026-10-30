# Remove an alternate form

Remove one alternate form (allomorph) of an entry. Use it when an alternate form stands in for something that now has its own entry, or is redundant.

```sh
motif compose-remove-alternate-form --project language.fwdata --draft ceb-d004 \
  --intent '{"entry":"<entry-id>","form":"<form-id>"}'
```

`entry` and `form` are required and the intent is closed. The form must be an alternate form owned by the entry; the lexeme form cannot be removed this way.

The command refuses, and changes nothing, when anything refers to the form: a morph bundle of a parser analysis, an ad hoc prohibition, or any other object. The message names the referrer; retarget or remove that reference first. The Dry Run shows the entry's alternate forms before and after and a preview of the removed form, with its class, its text in every writing system, its morph type, its position and its number of environments. A writing system the project cannot resolve is shown by its numeric handle.

Use this editor through the CLI. Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite every operation this command produces. Review the Dry Run effects before Finalize; only a person can Apply the Proposal.
