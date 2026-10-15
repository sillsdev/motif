# ADR 0064 — Every operation can cite its source

A person or agent building a grammar from published descriptions can now record, on each change, which grammar
section, dictionary entry or corpus passage it came from, and read that back when reviewing, Dry Running and
after Apply. A grammar built this way can show where every rule and entry came from without anyone retracing
the work afterwards.

**Status:** accepted, 2026-10-13.

## Context

A real-language grammar built through Motif must cite, for every rule and entry, the section or source it was
taken from. Adding those citations after hundreds of Proposals have been Applied would mean redoing the
research, so they have to travel with each change from the moment it is authored.

The Proposal contract already reserves an operation-level `provenance` array for supporting evidence and
excludes it from the intent digest, but nothing filled it: a Draft operation had no such field, so finalize and
reopen dropped it, and no read-back showed it. Composer intents are closed schemas (rule 1), and `extensions`
is reserved for tool data (rule 7).

## Decision

1. **A citation is a typed `provenance` entry.** `{"type":"citation","source":"...","locator":"..."}`: `source`
   required (1–300 characters), `locator` optional (at most 120), no other property, at most one per
   operation. Proposal parsing refuses a malformed or repeated citation; other `provenance` entries stay open.
   `decisionEvidence` is a second typed entry on the same channel, with the same rules: closed, at most one per
   operation, outside the digest and never naming an author of the Proposal.
   No new envelope field, no `extensions` key, and no change to the intent digest: two Proposals that differ
   only in their citations have the same digest.
2. **A citation is supplied beside an intent, never inside it.** An authoring request takes an optional
   citation as a sibling of its intent (on the CLI `--cite-source` and `--cite-locator`; in a batch file an
   entry's `citation`). Motif copies it onto every operation that request lowered to. Intent schemas stay
   closed and unchanged, so a composer never sees it. `add-set-gloss` takes one first; the composing verbs and
   batch entries take one through the single place their operations join a Draft.
3. **The latest author's citation wins.** When a request rewrites an operation already in the Draft (the same
   slot, or a replace-in-place composer), the operation's citation becomes the one that request supplied, and
   is removed if it supplied none: a citation always describes the value it sits beside.
4. **It travels and is shown.** Drafts store it, finalize writes it into the committed revision, reopen,
   duplicate and split keep it, and Proposal show and the Dry Run report list it per operation (`cites:` in
   text, `citation` in JSON and MCP results).
5. **It names a source, never a person.** There is no author, agent or reviewer field, now or later; Proposals
   have no author.
6. **It is not project data.** LibLCM has nowhere to keep it, so the project's semantic snapshot and diff do not
   carry it. Which Applied operation cites which source is read from the Applied Proposals in Motif's store:
   `motif citations --project <fwdata> [--uncited]` lists every Applied operation with its citation, and only the
   uncited ones with `--uncited`. It stays a Developer command until a grammar project has used it.
   Apply responses also include cited operation rows projected from the Proposal's existing `provenance`; citations
   are not copied into the FieldWorks applied-change log or stored Receipt.

## Consequences

- A citation can be corrected without re-running a Dry Run or Trial: reopen, cite again, Finalize. Evidence is bound
  to the intent digest, which a citation does not move, so finalizing to the same digest rewrites that revision's
  stored JSON rather than adding a revision. A review entry's looked-at mark does reset, because the reviewer has not
  yet seen the new citation.
- MCP authoring tools do not yet accept a citation: adding a field changes the measured agent surface, so it
  waits for an A/B screen. Scripts drive the CLI and batch files.
- A source cited by many operations is repeated on each; a shared bibliography is not part of this decision.
