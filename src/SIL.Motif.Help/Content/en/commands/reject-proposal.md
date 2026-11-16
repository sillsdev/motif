# Decline a Proposal

`motif reject --proposal <id>` declines a Proposal you do not want. It is kept, with its [Trials](term:trial), as a record of what was suggested; nothing reaches FieldWorks.

## When to use it

Use it after reading a Proposal you will not apply. A Proposal that is still a Draft is finalized first, so what you declined stays readable. Your own pending changes are not declined; take them out in **Proposals**, under **Your Changes Not Applied Yet**, instead.

## Example

```text
motif reject --proposal <id> --project <project> --json
```

## What it prints

The Proposal's id and its new status, `rejected`. In the window, **Decline** on the Proposal in **Proposals** does the same while the Proposal is open, and the card moves to **Closed** on **Proposals**.

## Related commands

See [Reopen a Proposal](cmd:reopen%20--proposal) and [Delete a closed Proposal](cmd:delete%20--proposal).
