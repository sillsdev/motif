# Write a Proposal's Rationale

Writes or replaces a Proposal's Rationale: what the Proposal changes and why, in FieldWorks Markdown. Whoever builds the Proposal, usually an assistant, writes it.

## Example

```text
motif proposal rationale <proposalId> --project <fwdata> --text "Plural -s was missing from the noun template"
motif proposal rationale <proposalId> --project <fwdata> --file rationale.md
motif proposal rationale <proposalId> --project <fwdata> --text "..." --author "Claude (Opus 5.5)"
```

Give the full Proposal id, the one `new` or `proposals` returned; a name or a prefix is refused. A Draft that has never been finalized has a Proposal id too, so write the Rationale before [`finalize`](cmd:finalize), which refuses a Proposal without one.

The Rationale is kept apart from the Proposal's changes, so rewriting it never changes what the Proposal does. It remembers which content it was written for: after the Proposal changes, the person sees it marked out of date, and a rewrite clears that mark. It can be rewritten until the Proposal is applied or closed, and it holds at most 64 KiB.

The Rationale is checked when it is written. Unmarked text is the project's default analysis writing system, and links, writing systems and styles are checked against the current Baseline. The findings are only what a reader would see as wrong: markup that does not read, a link to something not in the project, an unknown writing system or style. A clean Rationale is saved in its canonical spelling. A Draft's Rationale with findings is saved as written and the findings are returned, but [`finalize`](cmd:finalize) refuses it until they are fixed. Once the Proposal is Ready, a Rationale with findings is refused and the last clean one stays.

If the person left a [Comment](cmd:proposal%20comment%20add), a new Rationale is one answer to it. `--author` says what an assistant calls itself, as it does for a Comment; each rewrite is kept, with its author, until the Proposal is decided.
