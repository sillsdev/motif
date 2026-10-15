# Write your note on a Proposal

Writes or clears the person's own note on a Proposal, in Markdown. The note is the person's reply to the Rationale; only a person writes it, and an assistant reads it.

## Example

```text
motif proposal note <proposalId> --project <fwdata> --text "Please keep the second gloss"
motif proposal note <proposalId> --project <fwdata> --file note.md
motif proposal note <proposalId> --project <fwdata> --clear
```

Give the full Proposal id. The note stays outside the Proposal's changes, remembers which content it was written for, and holds at most 64 KiB. It can change until the Proposal is applied or closed.

When the note is newer than the [Rationale](cmd:proposal%20rationale), `proposal show` reports it as changed, and an assistant is told to read it. Writing a new Rationale clears that.
