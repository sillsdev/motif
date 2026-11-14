# List the hypotheses

`list hypotheses` lists hypotheses in id order with their tests, Proposals, GDR link and graded numbers, or shows one by id.

## Example

```text
motif list hypotheses --project <fwdata> --status passed
motif list hypotheses --project <fwdata> --id H-12 --json
```

Pages are `--limit` at a time (1 to 200, default 50). `--id` shows one hypothesis in full.

## Related commands

- [Check the neighbours of a change](cmd:hypothesis%20neighbours) is the check that runs before any new object.
