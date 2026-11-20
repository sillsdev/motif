# Show a Proposal

Reads one saved Proposal by its canonical id, or a Draft by name or canonical id, with its status, label, Rationale, Comments and operations. The Rationale shows whether it is out of date and whether you have looked at it; the Comments show whether a person's is newer than the Rationale. It does not change the FieldWorks project.

In the JSON Comment objects returned by `show` and the MCP `motif_proposals` tool, a missing `findings` field means the Comment was not checked. An empty array (`[]`) means it was checked and clean; a non-empty array lists the findings.

`--current` (MCP `current: true`) also reads, from the saved project, the value each change's field holds now, so you can read "old to new" before any Dry Run or Trial. Each operation then carries `current`, a list of `{ target, field, value }` where `value` maps a writing-system tag to its text (empty when the field holds nothing). An operation whose target the Proposal itself creates, or whose field cannot be read, has no `current`. When the saved project cannot be read at all, the Proposal still comes back with `currentUnavailable` saying why. Without `--current` the read opens no project and needs no Trial.
