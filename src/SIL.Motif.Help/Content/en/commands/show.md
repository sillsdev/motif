# Show a Proposal

Reads one saved Proposal by its canonical id, or a Draft by name or canonical id, with its status, label, Rationale, Comments and operations. The Rationale shows whether it is out of date and whether you have looked at it; the Comments show whether a person's is newer than the Rationale. It does not change the FieldWorks project.

In the JSON Comment objects returned by `show` and the MCP `motif_proposals` tool, a missing `findings` field means the Comment was not checked. An empty array (`[]`) means it was checked and clean; a non-empty array lists the findings.
