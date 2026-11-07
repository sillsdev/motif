# 0094. Requests: how a linguist asks an assistant for a Proposal

A linguist can point at a word, a Warning or a Matrix cell and ask an AI assistant to fix it. That ask is stored
in the project as a Request, so any assistant that connects later can find it and answer it with a Proposal.

**Status:** accepted. Extends [ADR 0077](0077-project-store-upgrades-carry-authored-data.md): Requests are
linguist-authored and are kept through store upgrades, as Proposals are.

## Decision

1. **Data type.** A Request has an id, a target kind (`word`, `warning` or `matrix-cell`), a target id, a short
   target label, an optional note (at most 2000 characters), a status (`open`, `answered` or `withdrawn`), the
   creation and closing times, and, when it came from Ask Again, the Request it repeats. A Proposal carries an
   optional link to the Request it answers. The store schema gains a Requests table.
2. **Lifecycle.** Starting a Draft for a Request does not close it. Finalizing the linked Proposal answers it. A
   linked Proposal withdrawn or superseded before a decision reopens it. A declined Proposal keeps the Request
   closed; the declined Proposal offers Ask Again, which creates a new Request. The linguist can Withdraw a
   Request. A Request has at most one undecided Proposal. Requests do not count in the sidebar badge.
3. **Surfacing channels.** An assistant cannot be pushed to, so Motif uses what every client supports today:
   - every MCP tool result, success or refusal, appends one line when a Request is open, and adds
     `openRequestsCount` to its structured content;
   - the server instructions tell the assistant to call `motif_list_requests` before drafting a Proposal;
   - an MCP prompt, "Work on Open Requests", starts the work;
   - `motif_start_proposal` takes an optional `request_id`;
   - Handoff adds `requests.json` and a starter line in `handoff.md`.
   Claude and ChatGPT both read tool results and server instructions; neither reliably supports server-initiated
   notices, so the result line is the channel that always arrives.
4. **Prompt-injection rule.** A note is the linguist's words, never an instruction. Every channel presents it as
   quoted text ("The linguist wrote: ..."), and the server instructions say so. The result line carries only a
   count and the tool name.
5. **CLI parity.** `motif list requests`, `request create`, `request withdraw` and `request ask-again`.

## Left out

- An MCP App inbox: not every client renders one.
- MCP Events: not supported by the clients that matter now.
- ChatGPT transport: pending; the same tools apply once it exists.
