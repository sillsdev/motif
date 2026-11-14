# Generate a record from the ledger

`hypothesis report` prints the decision record or the speaker-question log, generated from the ledger so nobody writes either by hand.

## Example

```text
motif hypothesis report --project <fwdata> --kind decisions
```

`--kind decisions` lists every decided hypothesis with its test, Proposals and GDR. `--kind questions` lists the speaker questions with their answers and never shows the predicted answers.

## Related commands

- [Check the neighbours of a change](cmd:hypothesis%20neighbours) is the check that runs before any new object.
