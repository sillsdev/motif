# Grade a hypothesis against the answer key

`hypothesis grade` records the four numbers for a hypothesis once the answer key is known: whether the sealed loop reached the verdict, the word questions it needed, objects per attested type before and after, and agent hours against the list alternative.

## Example

```text
motif hypothesis grade H-12 --project <fwdata> --sealed-verdict match --verdict-source Wolff --word-questions 6 --objects-before 395 --types-before 400 --objects-after 10 --types-after 400 --agent-hours 3 --alternative-hours 20
```

Each flag is optional but at least one is needed; grade in steps as numbers arrive. The response shows objects per attested type before and after.

## Related commands

- [Check the neighbours of a change](cmd:hypothesis%20neighbours) is the check that runs before any new object.
