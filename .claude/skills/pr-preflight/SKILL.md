---
name: pr-preflight
description: >-
  Required entrypoint for writing, opening, updating, or shipping a Motif PR, and for branch-readiness
  review or review-summary work. Use before drafting PR copy so the diff, base, head, findings, and
  validation evidence are pinned.
user-invocable: true
---

# PR preflight

Read [`docs/development-workflow.md`](../../../docs/development-workflow.md) first; it is the shared
policy and provenance record. Read `CONTEXT.md`, `README.md`, and applicable `AGENTS.md` files too.

1. Confirm the branch and working tree. Preserve unrelated changes. A routine PR comes from a feature
   branch; support direct-main work only when explicitly requested.
2. Discover the repository with `git remote get-url origin`. Establish the base ref; do not assume a
   repository owner or a stale URL. Record the exact base ref/SHA, merge-base SHA, head SHA, and changed
   paths. For the current `origin/main` default, run and retain the output of:

   ```powershell
   $remoteUrl = git remote get-url origin
   $baseRef = 'origin/main'
   $baseSha = git rev-parse $baseRef
   $mergeBase = git merge-base $baseRef HEAD
   $headSha = git rev-parse HEAD
   git diff --name-status $mergeBase $headSha
   git diff --stat $mergeBase $headSha
   ```

   Use the repository's discovered default ref when it differs; ask if it cannot be established.
3. Review the changed files and relevant base versions. Report only verified findings, grouped as
   Critical, Important, or Minor, plus positive observations and required evidence. Apply Motif checks
   from the shared document; do not import source-repository-specific platform or installer rules.
4. Verify `.review/` is ignored before writing scratch files. Interview the author only about unresolved
   material findings, uncertainty, and trade-offs. Record explanations,
   dismissed findings, unresolved concerns, and any in-review fixes in `.review/summary.md`.
5. For PR copy, use `pr-pitch` after recording readiness or explicit draft limitations. Drafting needs no
   separate publication approval. Do not commit, push,
   create, edit, assign, transition, merge, or delete without explicit authority for that action.

`.review/` is scratch state. Keep durable decisions and evidence in the repository or the PR record.
Use `./build.ps1` and `./test.ps1` for applicable code validation; documentation-only work gets
proportional Markdown/link/whitespace validation and must state what was skipped.
