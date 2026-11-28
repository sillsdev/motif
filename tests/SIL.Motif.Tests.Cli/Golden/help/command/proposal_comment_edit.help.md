# Edit a Comment on a Proposal

Replaces the text of a Comment on an open Proposal. Only a person edits a Comment; an assistant cannot.

## Example

```text
motif proposal comment edit <proposalId> <commentId> --project <fwdata> --text "Keep the second gloss, drop the third"
motif proposal comment edit <proposalId> <commentId> --project <fwdata> --file comment.md
```

Give the full Proposal id and the Comment number that `show` lists. The Comment then shows that it was edited, and when; the earlier text is not kept. A Comment cannot be empty: [delete it](cmd:proposal%20comment%20delete) instead. Comments cannot change once the Proposal is applied, declined, withdrawn or superseded.
