# Order alternate forms

```sh
motif order-allomorphs --project language.fwdata --draft <name> \
  --intent '{"target":"<entryId>","expectedAlternates":["<id>","<id>"],"alternates":["<id>","<id>"]}'
```

The closed intent names one lexical entry and its current and requested alternate-form order. Both lists must contain exactly the same existing forms, and the current list must still match the project. The command emits identity-anchored moves inside that entry's alternate-form sequence. The lexeme form is stored separately and cannot be moved.

Earlier matching alternate forms take precedence over later ones. A final unrestricted form can serve as an elsewhere fallback after the conditioned forms; moving it earlier can make it compete with those forms. Preserve the final position when it is intentional. Finish the Draft, inspect its Dry Run, and run a bounded Trial on the intended cases and relevant contrasts. Only a person can Apply the Proposal.
