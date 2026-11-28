# Withdraw a Request

`request withdraw` takes back an open Request the linguist no longer wants answered.

## Example

```text
motif request withdraw <requestId> --project <fwdata>
```

Give the full `requestId` that [list requests](cmd:list%20requests) returns. Only an open Request can be withdrawn; an answered one stays as it is.
