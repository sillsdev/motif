# Ask an assistant to fix this

`request create` stores a Request: something the linguist wants an assistant to fix, about a word, a Warning or a Compare matrix cell. An assistant answers it with a [Proposal](term:proposal).

## Example

```text
motif request create --project <fwdata> --kind word --target <word> --label <word> [--note "Please check the plural"]
```

`--kind` is `word`, `warning` or `matrix-cell`. `--target` is the word, Warning or cell as the window names it, and `--label` is a short name a person can read. The note is optional, at most 2000 characters, and stays the linguist's own words.

A Request is answered when the Proposal that answers it is finalized. It opens again if that Proposal is withdrawn or replaced before anyone decides, and stays answered if the linguist declines it.

## Related commands

- [List the open Requests](cmd:list%20requests) reads them.
- [Withdraw a Request](cmd:request%20withdraw) takes one back.
