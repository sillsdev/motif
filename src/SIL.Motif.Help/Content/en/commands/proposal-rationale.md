# Write a Proposal's Rationale

Writes or replaces a Proposal's Rationale: what the Proposal changes and why, in Markdown. Whoever builds the Proposal, usually an assistant, writes it.

## Example

```text
motif proposal rationale <proposalId> --project <fwdata> --text "Plural -s was missing from the noun template"
motif proposal rationale <proposalId> --project <fwdata> --file rationale.md
```

Give the full Proposal id, the one `new` or `proposals` returned; a name or a prefix is refused. A Draft that has never been finalized has a Proposal id too, so write the Rationale before [`finalize`](cmd:finalize), which refuses a Proposal without one.

The Rationale is kept apart from the Proposal's changes, so rewriting it never changes what the Proposal does. It remembers which content it was written for: after the Proposal changes, the person sees it marked out of date, and a rewrite clears that mark. It can be rewritten until the Proposal is applied or closed, and it holds at most 64 KiB.

If the person left a [note](cmd:proposal%20note), a new Rationale is the answer to it.
