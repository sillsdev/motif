# Delete a Comment on a Proposal

Deletes a Comment from an open Proposal. Only a person deletes a Comment; an assistant cannot.

## Example

```text
motif proposal comment delete <proposalId> <commentId> --project <fwdata>
```

Give the full Proposal id and the Comment number that `show` lists. A deleted Comment is gone. Comments cannot change once the Proposal is applied, declined, withdrawn or superseded.
