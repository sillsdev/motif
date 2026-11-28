# Reopen a Proposal

`motif reopen --proposal <id>` opens a declined or withdrawn Proposal again as a Draft, under the same id. Its earlier revisions and [Trials](term:trial) are kept.

## When to use it

Use it when you change your mind about a Proposal you declined, or want to take up one its author withdrew. An applied Proposal is final and is refused with `proposal.applied-final`. A superseded Proposal is refused with `proposal.superseded`, which names the Proposal that replaced it.

## Example

```text
motif reopen --proposal <id> --project <project> --json
```

## What it prints

The Proposal's id, the name of its Draft and how many changes it holds. In the window, **Reopen** on a closed card on **Proposals** does the same. Try the Proposal again before applying it.

## Related commands

See [Decline a Proposal](cmd:reject%20--proposal) and [Apply one Proposal](cmd:apply%20--proposal).
