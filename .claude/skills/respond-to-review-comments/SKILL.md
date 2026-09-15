---
name: respond-to-review-comments
description: >-
  Handle Motif pull-request review comments from an active PR, supplied URL or ID, or pasted threads.
  Verify each request, classify it as fix, clarify, reply, or defer, and produce thread-specific replies
  without resolving disputed, deferred, ambiguous, or unverified discussions.
user-invocable: true
---

# Respond to review comments

Read [`docs/development-workflow.md`](../../../docs/development-workflow.md), `CONTEXT.md`, and the
instructions for the touched files. Use available GitHub tools or the supplied PR/thread text. If no
comments are available, ask for them.

For every thread, verify the claim against current and base code, then keep a ledger:

- `fix`: technically sound, unambiguous, scoped, and compatible; make the smallest change;
- `clarify`: ask the focused question before editing;
- `reply`: explain why the request is already satisfied, incorrect, or out of scope;
- `defer`: record valid follow-up work and why it does not belong here.

Reply in the specific thread. Resolve only when the request is fully addressed, verification is fresh,
the thread is not disputed, and no question remains. Preserve unresolved disputed, deferred, ambiguous,
or unverified threads. Use Motif checks from the shared policy, including Proposal atomicity, LibLCM
authority, exact identity/normalization, Dry Run versus Assessment, CLI/JSON, Avalonia, and `net10.0`.

Use `./build.ps1` and `./test.ps1` when code or tests require them; `test.ps1` has no filter/project
switches. For Markdown, instruction, or skill-only edits, use proportional link/structure/whitespace
checks and report skipped full-suite validation. Commit, push, post replies, resolve threads, or edit
the PR description only with explicit authority. End with fixed, reply-only, unresolved blockers,
verification, and any commit/push result.
