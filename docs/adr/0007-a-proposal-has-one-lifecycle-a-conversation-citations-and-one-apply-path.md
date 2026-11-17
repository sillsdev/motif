# 0007. A Proposal: one lifecycle, a conversation, citations, one Apply path

**Status:** Accepted, 2026-11-14.

## Context

A Proposal must preserve the reason for a change, support revision and human review, and reach the project
through one Apply path. Review attention, dependencies, discussion and linguistic evidence have different
meanings and must not become competing forms of approval.

## Decisions

### D1. Grammar has one integrator; Proposals need no server

Motif does not merge concurrent grammar edits, reconcile rule branches or provide distributed grammar
authoring. Rule order and interactions make independently correct edits unsafe to combine; one integrator
at a time and the project lock protect writes. This does not make lexical and text work single-author.
Their collaboration follows their data and existing FieldWorks, Chorus and FW Lite routes; Motif does not
build an online forum or another dictionary collaboration service.

Review and discussion live with the project in Motif's local store, usable offline. A Proposal remains
meaningful without a web service, replicated review database or forum. Sharing is deliberate; external
systems may refer to its immutable content by digest. The rationale record, rather than concurrent editing,
is what must outlive a grammar's integrator.

### D2. A Draft is a phase of an identified Proposal

Creation gives the Draft a permanent Proposal id and name but no committed revision. The mutable Proposal
row points to immutable revisions; there is no separate Drafts store/table or two-table move on Finalize.
Drafts appear in lists, marked unfinished. Finalize commits the first immutable revision without moving the
Proposal elsewhere. A Draft may be Dry Run and Trialled before Finalize; unchanged finalization retains
its evidence ([ADR 0009](0009-assistants-reach-motif-through-one-agent-api.md)). A Proposal belongs to exactly
one project; the same change in two projects is two Proposals.

### D3. One lifecycle separates status from dependencies

Draft, Ready (stored `proposed`) and Deferred are open. Applied, Declined (stored `rejected`), Superseded and
Withdrawn are closed. Applied is final: never reopened or deleted; Receipts and applied-log records remain.
Deferred means still wanted and needing revalidation, not frozen applicability. Dependencies are content
(`requires`, ADR 0012) governing Apply order, never a status a person can toggle away.

A person declines with **Decline** / `reject --proposal` and reopens Declined or Withdrawn with **Reopen** /
`reopen --proposal`, as a Draft under the same id with history retained. The Proposal remains closed until
that Draft is Finalized; discarding it leaves the prior closed state. Superseded names its replacement.
An assistant withdraws its own Proposal with `withdraw`. A never-Applied Declined, Superseded or Withdrawn
Proposal may be deleted with its Trials and discussion (`delete --proposal`, **Delete**). Lists show open
Proposals by default. **Undo all** for pending changes uses `discard-draft`.

### D4. Rationale and Comments form the Conversation

A Proposal has a one-line label and a Rationale explaining what changes and why. People and agents may
revise it while open. Earlier versions remain restorable until the Proposal is decided, then are dropped;
the latest stays frozen. Rationale is required before Finalize and obeys the clean-text contract in
[ADR 0006](0006-rich-text-is-fieldworks-markdown-in-the-shared-textediting-field.md).

People may add, edit or delete any Comment. Agents may only add their own attributed Comments, never edit
or delete one. Comments can grow; they and pictures freeze at decision and stay as long as the Proposal.
Attribution is a reader's label, not verified identity, approval or Apply authority. Rationale and Comments
record the Proposal content they address so readers can see staleness. A choice resolving a change that
no longer fits is recorded as a Comment. A reply that changes Proposal content creates a revision; a
Comment alone does not alter operations. The agent API reports newly written human Comments and tells the
assistant to read them, because a server cannot start an assistant turn.

### D5. Pictures are authored data with one conversion path

Pictures live in `Project.motif.db`, count toward its 100 MB byte budget (ADR 0023), and never disappear in
computed-data pruning. Deleting a never-Applied Proposal deletes them. A Proposal permits at most ten new
attachments, shown as “3 of 10”; an existing excess is kept, but a new attach refuses with “Remove one to add
another”. An optional one-line caption has at most 200 characters, belongs to the picture rather than its
attachment location, and appears in `proposal picture show`; upgrades retain it by name.

`PictureScaler` in Commands is the single SkiaSharp implementation for the window, CLI and command API.
`proposal picture add` / `AddPictureRequest` accept WebP, PNG, JPEG, GIF or BMP up to 64 MiB and about
100 megapixels, checking declared dimensions before decode. Honour EXIF orientation; never enlarge; resize
the long side to at most 1000 px; drop metadata and colour profile; retain only the converted copy, capped
at 1 MiB. Encode WebP losslessly for PNG-like sources (PNG, GIF, BMP, lossless WebP) and at quality 80 for
JPEG or lossy WebP. If Skia cannot encode WebP, use PNG for a PNG source and JPEG quality 80 for the rest.
Refusals explain invalid/undecodable or oversized inputs. Commands and the window use the same SkiaSharp
version (3.119.4), with matching Linux native assets; native Skia ships with CLI and MCP too.

### D6. Review is one ordered list of changes

The shared catalog supplies word changes and grammar Intent changes, including Dry Run effects and the
readings a replacement must preserve. Composer provenance records produced operation ids; an operation
without provenance is its own entry. Pending Draft changes use this list too. Two Proposals changing the
same words are flagged, never merged.

**Keep** marks an entry looked at, changing no content. **Next** marks the entry left; **Previous** marks
nothing. **Undo** removes that entry's operations and declared dependents, creates a revision, clears the
removed marks and makes Trial evidence stale. These are attention and editing actions, not approval or
per-change Accept/Reject. A finalized Proposal first edited by Undo or a staged parse reopens as a Draft
under the same id; the window Finalizes at Apply.

### D7. Removal consequences are explicit

An operation can be removed individually. With no dependents, removal proceeds. Removal severing `requires`
or orphaning dependents warns, names every consequence and requires explicit force accepting that set.
If consequences cannot be enumerated, refuse rather than guess. The Proposal id stays fixed and the intent
digest moves to a new revision. The window's Undo uses this same removal path and its dependency cascade.

### D8. Looked-at marks name content and evidence, never a reviewer

Marks live only in the Motif store, keyed by Proposal, entry, operation-content digest and Trial-evidence
digest. Either digest changing clears them. They are not sent to FieldWorks or stored in Proposal
extensions, and the looked-at count never gates Apply. Citation changes also clear the affected entry's
mark even though they do not alter intent. No Decisions table, approval verdict, Apply `User` argument,
Proposal author or reviewer identity is introduced. A Receipt keeps the person's message. Comment labels
and explicit judgement attribution have their separate bounded purposes.

### D9. All front doors use one Apply orchestration

`apply --proposal <id>`, `apply --all-pending` and Developer `apply <id>` read the Proposal, refuse cheaply
while FieldWorks holds the project, Finalize a Draft, check each change once, bind a Dry Run and Apply in
one LibLCM unit of work. If anything refuses, a Draft Finalized by Apply reopens under the same id.
The first route requires the Proposal's current Trial Dry Run. At FieldWorks' save boundary,
`--all-pending` queues a fresh Dry Run and needs no prior Trial; its JSON, exit codes and reconciliation
signal retain their shared contract. Developer `apply <id>` uses the already bound `dryrun`.

Pending changes are identified by a stored role, never their Draft name; `trial --pending` uses the same
Trial as `trial <id>`. Every Draft edit, including composition, compares and swaps against the revision
read, refusing races rather than losing edits. Readiness is computed from exact evidence; no recorded
approval substitutes for it. Apply/read-back and unforceable stale-field rules are in ADR 0017.

### D10. Plugin presentation does not introduce an owner or inverse Apply

The plugin shows one proposed grammar update per person. Claude may build several alternative Drafts,
with only one shown and brought to Ready; synced changes require updating to the current project. This
limit applies only to motif-Bible. The desktop supports several Proposals and comparing alternatives under
[ADR 0001](0001-people-work-through-five-things-diagnostics-are-behind-a-switch.md).
Any person may Apply; there is no grammar-owner role. Applied Proposals remain on record. There is no
feature that undoes an Applied Proposal: Claude stages any requested reversal by hand as a new Proposal,
with ordinary stale-field checks. This is distinct from Undo of an unapplied review entry.

### D11. Citations are typed operation provenance outside the digest

An operation may have at most one closed `{"type":"citation","source":"...","locator":"..."}` entry:
`source` is required, 1–2000 characters; optional `locator` is at most 300; no other property. Malformed or
repeated citations refuse parsing. `decisionEvidence` is another closed, at-most-one typed entry on the
same channel, outside the digest and never naming a Proposal author. Other provenance entries remain open.
No envelope field, extension key or change to the intent digest is added. Two Proposals differing only
in citations have the same digest.

A citation is a sibling of the closed Composer intent, never an intent field: CLI `--cite-source` /
`--cite-locator`, or a batch entry's `citation`. The common Draft insertion path copies it to every
lowered operation; the Composer never sees it. When a request rewrites an existing slot or replaces an
operation in place, that request's citation replaces the old one, or removes it when none was supplied.
A citation describes the adjacent value, not an earlier author's value.

### D12. Citations survive authoring and remain readable after Apply

Draft storage, Finalize, reopen, duplicate and split retain citations. Proposal show and Dry Run reports
list them per operation (`cites:` in text, `citation` in JSON and MCP responses). They name a source, not a
person. They are not project data and do not enter the semantic snapshot/diff, FieldWorks applied log or
stored Receipt. Apply responses project cited rows from existing Proposal provenance.

`motif citations --project <fwdata> [--uncited]` reads Applied operations with their citations, or only
uncited operations, from Motif's store. It remains Developer until used on a grammar project. Citation
corrections require no remeasurement: reopen, cite, Finalize; same-digest finalization rewrites that
revision's JSON rather than adding a revision. Evidence stays current but the review mark resets. A shared
bibliography is not part of this contract. Adding citation input to measured MCP authoring awaits an A/B
screen; existing CLI and batch routes remain available.

### D13. Check requirements are derived from every revision

Never label a Proposal's check class at creation. Recompute from its current operations whenever content
changes: a stem Proposal that gains a rule needs grammar checks and makes earlier cheap evidence stale.
Recompilation concerns grammar changes; parser runs may still be valuable for stems and texts, using the
supplied-lexicon path in ADR 0019. Report newly contested analyses without making that lookup a gate.
Reports bind corpus identity/hash, lexicon-overlay revision and grammar identity, so text sync cannot leave
supporting evidence silently current just because changed-object footprints did not move.

### D14. Exposure follows the shared catalog

Proposal listing, review, Apply, decline, reopen and delete have Released Help; state-changing human review
commands are HumanOnly. Trial and Trial-with-wait remain AdvancedAi for an assistant's own work, as does
withdrawal of its own Proposal. `proposal looked-at` and Developer `apply <id>` remain Developer. No MCP
route Applies, discards or deletes a store. The seven-tool rendering follows ADR 0009.

## Consequences

Reasoning, revisions, evidence and attention remain readable without an approval subsystem or server.
Applied work is permanent; unfinished work and its conversation can be deliberately discarded. Presentation
may differ between desktop and plugin while the lifecycle, Draft races, citations and Apply semantics stay
shared. Computed-artifact retention belongs to ADR 0023 rather than changing this lifecycle.
