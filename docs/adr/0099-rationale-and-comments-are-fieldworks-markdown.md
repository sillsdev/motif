# 0099. Rationale and Comments are FieldWorks Markdown, and Finalize requires a clean Rationale

**In plain terms:** a Proposal's Rationale and its Comments are written in the same rich field as GDRs and sense
prose, stored as FieldWorks Markdown so they can link to the project's objects. A Proposal is not handed to a reviewer
with a Rationale the checker finds wrong, and a reviewer never sees a Ready Proposal's Rationale become broken.

**Status:** accepted, 2026-11-10 (owner, Motif #74). Uses the format of
[ADR 0089](0089-gdr-text-is-human-editable-fieldworks-markdown.md) and the conflict viewer of #88.

## Context

The Rationale was Markdown with no checked profile, shown read-only; Comments were plain text. Neither is a FieldWorks
field, so neither goes through Draft and Apply: both live in Motif's store. Agents write them through commands, and an
unchecked format let an agent's Rationale contain links and spans no reader could resolve.

## Decision

1. **Format.** Rationale and Comment text is FieldWorks Markdown. Unmarked text is the project's default analysis
   writing system. Existing text is read as FieldWorks Markdown; what cannot be read is shown as plain text, never lost
   or rewritten.
2. **Editing.** A person edits the Rationale of an open Proposal, and any Comment, in the shared rich field: "Edit",
   then Save or Cancel. Save writes to Motif's store at once; there is no Draft step. An agent's Comment stays
   read-only to agents. Everything freezes when the Proposal is decided. Pictures on Comments stay attachments.
3. **Concurrent edits.** If the Rationale changed while a person was editing it, Save offers "Compare…", the #88
   conflict viewer, with the other side named by its author label.
4. **Findings** are only what a reader would see as wrong: unreadable markup, a link to an object not in the project,
   an unknown writing system or style. Canonical-spelling tidy-ups are made silently on save.
5. **Draft:** a Rationale with findings saves as the latest version and the findings are returned. **Finalize is
   refused** while the Rationale has findings. Earlier versions can be restored from the kept history.
6. **Ready:** a Rationale revision with findings is **refused**; the last clean Rationale stays.
7. **Comments never block** Finalize or Apply; their findings are shown, and they save.
8. Rationales of Proposals already Ready are left as written; rule 6 applies to new revisions only.

## Considered options

- Warnings only, never blocking. Rejected by the owner: a reviewer should not receive a Rationale that does not read.
- Keep the last clean version live during drafting. Rejected: it would hide the agent's newest reasoning while it is
  still being written; before Finalize the history is enough.
- Refuse every save with findings. Rejected: it would break agents' drafting loops over a typo.

Owner decision, 2026-11-10.
