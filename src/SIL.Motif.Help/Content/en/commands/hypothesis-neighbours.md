# Check the neighbours of a change

`hypothesis neighbours` asks the ledger whether a hypothesis already exists for a change, environment, host class and slot, and whether a passed rule stands or a rule attempt failed. Run it before authoring any object.

## Example

```text
motif hypothesis neighbours --project <fwdata> --change d>r --environment "_ an"
```

`--change` is required. The environment, host class and slot narrow the match; leave one out to match any. `hasPassed` means a passed rule stands, so widen or merge it. `hasFailedAttempt` means a rule attempt is on record as failed, which permits a list for that shape.

## Related commands

- [State a hypothesis](cmd:hypothesis%20add) records a new one.
