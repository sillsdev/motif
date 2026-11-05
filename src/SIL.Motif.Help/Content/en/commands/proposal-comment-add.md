# Add a Comment to a Proposal

Adds a Comment to an open Proposal, in Markdown. A person or an assistant may add one; an assistant says what it calls itself with `--author`.

## Example

```text
motif proposal comment add <proposalId> --project <fwdata> --text "Please keep the second gloss"
motif proposal comment add <proposalId> --project <fwdata> --file comment.md --author "Claude (Opus 5.5)"
```

Give the full Proposal id. Without `--author` the Comment is the person's; `--person` names the person, and defaults to the computer's user name. An author label and a person name hold at most 100 characters, and a Comment shows as "author for person". The label is only a label: Motif does not check it, and nothing depends on it.

A Comment stays outside the Proposal's changes and remembers which content it was written for. It can be added until the Proposal is applied, declined, withdrawn or superseded; reopening the Proposal allows Comments again. When a person's Comment is newer than the [Rationale](cmd:proposal%20rationale), `show` reports `commentsChanged`, and an assistant is told to read it.
