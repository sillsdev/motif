# Set rules on clitics

```sh
motif compose-set-not-on-clitics --project language.fwdata --draft <name> \
  --intent '{"expectedNotOnClitics":true,"notOnClitics":false}'
```

The parser runs the project's phonological rules on a word before any clitic attaches to it, unless the project's parser settings say otherwise. This composer sets that choice: `true` keeps the rules off the clitic, so a clitic keeps its own spelling; `false` runs the rules on the word with its clitic attached, so a rule can change the clitic's sounds too. A project that never set the choice holds `true`. `expectedNotOnClitics` must match the value Motif reads while preparing the Draft; a stale value is refused.

Only the one setting changes. Every other parser setting in the project is kept. Setting the value the project already holds changes nothing the parser reads.

The command stages one write in the Draft. It does not change the FieldWorks project. Finish the Draft, inspect its Dry Run, and compare bounded before and after Trials with the same words, clitic forms included. Review the after Report; only a person can Apply a Proposal.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on the operation this command produces.
