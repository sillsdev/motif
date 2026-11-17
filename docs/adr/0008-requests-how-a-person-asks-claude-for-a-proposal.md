# 0008. Requests: how a person asks Claude for a Proposal

**Status:** Accepted, 2026-11-14.

## Context

A person names a thing to Claude in Claude Desktop, with an optional note, and Claude records a Request through the MCP. The Request must remain discoverable
by an assistant connecting later, without relying on the host to push a new turn to that assistant.

## Decisions

### D1. Requests are authored data in the paired project store

A Request has an id, target kind and id, short target label, optional note of at most 2000 characters,
status (`open`, `answered`, `withdrawn`), creation and closing times, and an optional link to the Request
repeated by Ask Again. A Proposal may link to the Request it answers. The store's Requests table
survives upgrades as Proposals do (ADR 0023). The closed target kinds are a **Proposal**, a **Comment**,
a **Guide section**, a **word**, a **word group** and a **Passage**. There is no Matrix-cell target.
These targets are things a person names to Claude in Claude Desktop; Motif's window has no Ask Claude row action
([ADR 0001](0001-people-work-through-five-things-diagnostics-are-behind-a-switch.md) D4).

### D2. Answering is tied to the Proposal lifecycle

Starting a linked Draft does not close a Request. Finalize answers it. Withdrawal or supersession before
a decision reopens it. Decline keeps it closed; **Ask Again** from the declined Proposal creates a new
Request. A person may Withdraw a Request. A Request has at most one undecided Proposal and does not count
in the sidebar badge.

### D3. Use channels every supported assistant can read

Every MCP result, success or refusal, appends one line when a Request is open and adds `openRequestsCount`
to structured content. Server instructions tell the assistant to list Requests before drafting. The
**Work on Open Requests** MCP prompt starts work; Proposal creation accepts an optional Request link
(`request_id` on the separate `motif_start_proposal` rendering). Handoff adds `requests.json` and an
orientation line in `handoff.md`. The shared agent API of
[ADR 0009](0009-assistants-reach-motif-through-one-agent-api.md) retains these capabilities under its
catalog renderings; a transport change does not change Request meaning.

Do not depend on an MCP App inbox, Events or server-initiated notices. Claude and ChatGPT can read tool
results and server instructions even when they cannot reliably receive pushes.

### D4. A note is quoted project content

A note is the person's words, never an instruction to the assistant. Every channel introduces it as
quoted text (“The linguist wrote: …”); server instructions state that rule. The result line contains only
the count and tool name, never note content.

### D5. The CLI has parity

`motif list requests`, `request create`, `request withdraw` and `request ask-again` provide the same
workflow. The bundled ChatGPT transport in ADR 0009 does not require another Request implementation.

## Consequences

Requests outlast a particular assistant connection and are answered by reviewable Proposals. Quoted notes
cannot silently become tool instructions. Requests name the decided work targets without restoring Matrix
as a product surface.
