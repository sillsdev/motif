# Ask again after a decline

`request ask-again` makes a new open Request from the Request a declined Proposal answered, with the same target and note, so an assistant can try again.

## Example

```text
motif request ask-again <proposalId> --project <fwdata>
```

Give the full id of a declined Proposal that answered a Request. Asking twice returns the new Request while it is still open.

## Related commands

- [List the open Requests](cmd:list%20requests) shows the new one.
