# Show a Proposal

Reads one saved Proposal by its canonical id, or a Draft by name or canonical id, with its status, label, Rationale, Comments and operations. The Rationale shows whether it is out of date and whether you have looked at it; the Comments show whether a person's is newer than the Rationale. It does not change the FieldWorks project.

In the JSON Comment objects returned by `show` and the MCP `motif_proposals` tool, a missing `findings` field means the Comment was not checked. An empty array (`[]`) means it was checked and clean; a non-empty array lists the findings.

`--current` (MCP `current: true`) also reads, from the saved project, the value each change's field holds now, so you can read "old to new" before any Dry Run or Trial. Each operation then carries `current`, a list of `{ target, field, value }` where `value` maps a writing-system tag to its text (empty when the field holds nothing). An operation whose target the Proposal itself creates, or whose field cannot be read, has no `current`. When the saved project cannot be read at all, the Proposal still comes back with `currentUnavailable` saying why. Without `--current` the read opens no project and needs no Trial.

An Applied Proposal also carries `appliedRecord`, read from what Apply saved and not recomputed, so it stays true after the project moves on: `appliedUtc`, `mode`, `message`, `user`, `intentDigest`, `effects` (each `{ target, field, before, after }` as the Apply read it back, in the order it made them) and `evidence` (the counts of each Difference saved for the Proposal before the Apply, newest first, at most 50: `words`, `occurrences`, `improvedWords`, `couldBeBadWords`). The words behind those counts were dropped when the Proposal was decided. A Proposal marked Applied without a saved Receipt has no `appliedRecord`.

## Output size

`show` returns the first 10 operations. The report names `operationTotal`, `operationOffset` and, when more remain, `nextOffset`; the text ends with the `--offset` that reads the next page. `--limit <1-200>` sets the page size, `--offset <n>` or `--page <N>` picks the page, and `--all` returns every operation (MCP `all: true`). An offset past the last operation gives an empty page that still names the total. An offset or limit outside that range is refused with `paging.invalid`.
