# Set default compounding

```sh
motif compose-set-no-default-compounding --project language.fwdata --draft <name> \
  --intent '{"expectedNoDefaultCompounding":false,"noDefaultCompounding":true}'
```

The parser joins any two stems into a word by default, even when the project has no compound rules, unless the project's parser settings say otherwise. This composer sets that choice: `true` turns the built-in default compounding off, `false` turns it on again. `expectedNoDefaultCompounding` must match the value Motif reads while preparing the Draft; a stale value is refused.

Only the one setting changes. Every other parser setting in the project is kept. Setting the value the project already holds changes nothing.

The command stages one write in the Draft. It does not change the FieldWorks project. Finish the Draft, inspect its Dry Run, and compare bounded before and after Trials with the same words, real compounds included. Review the after Report; only a person can Apply a Proposal.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on the operation this command produces.
