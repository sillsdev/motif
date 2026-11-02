# Retire a duplicate boundary marker

Remove a stray second copy of the word boundary (`#`) or morpheme boundary (`+`) marker from the first phoneme set. Motif refuses to author a rule with a boundary while the set holds two markers for the same boundary, because it cannot tell which one the rule should name; retiring the unused copy clears that refusal.

```sh
motif compose-retire-boundary-marker --project language.fwdata --draft boundary-fix \
  --intent '{"marker":"<marker-id>"}'
```

`marker` is required and the intent is closed. The refusal that sends you here names the stray marker and the standard marker the rule must use. The command retires only a marker whose every spelling is `#` or `+` and whose standard marker, with the same spelling, is still in the set. It refuses the standard marker itself, and refuses, changing nothing, when any rule, environment or other object still refers to the marker; the message names the referrer. Retarget that use to the standard marker in FieldWorks first. Nothing is retargeted or cascaded for you.

The Dry Run shows the phoneme set's boundary markers before and after. Use this editor through the CLI. Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite the operation. Only a person can Apply the Proposal.
