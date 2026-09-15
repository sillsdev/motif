---
name: pr-pitch
description: >-
  Compose or revise a Motif pull-request description after preflight, or when the user asks to improve
  reviewer-facing PR copy. Use a concise pitch above the fold and collapsed evidence below it; do not
  treat this as permission to post, push, merge, or delete material.
user-invocable: true
---

# PR pitch

Read [`docs/development-workflow.md`](../../../docs/development-workflow.md) and use the preflight
record, pinned diff range, branch purpose, findings, and verification state as inputs. Discover the
repository from its remote when a PR target is needed.

Write one PR body with two zones:

- Above the fold: at most about 400 words, shorter for small changes, stating what changed, why it matters, the reviewer’s key risks and
  where to inspect them, deliberate non-goals, and exact verification or skipped checks.
- Below a divider: closed `<details>` sections for decisions, paths not taken, durable constraints,
  and the evidence behind the top-zone claims. Keep the pitch reviewer-focused; do not narrate the
  development process or duplicate the evidence above the fold.

Triage branch Markdown as durable, research, not-taken, process, or stale. Durable ADRs, plans,
contracts, and evidence stay. Remove other material only when the user approves each exact target and
the retained provenance is safely published. A PR description update, commit, push, or deletion needs
explicit authority; drafting the body alone does not authorize any of them. Never invent a Jira key,
PR owner, or repository URL.

When updating an existing body, preserve useful author-written content and replace stale evidence
instead of appending duplicate summaries. Link issues without closing keywords for partial fixes.
