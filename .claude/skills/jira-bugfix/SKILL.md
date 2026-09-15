---
name: jira-bugfix
description: >-
  Guide a Motif bugfix that names a Jira issue or asks to fix a Jira bug. Read the issue first when
  tools exist, use TDD with negative and boundary coverage, apply Motif checks, and pause before any
  external write or ambiguous scope decision.
user-invocable: true
---

# Jira bugfix

Read [`docs/development-workflow.md`](../../../docs/development-workflow.md), `CONTEXT.md`, and
`README.md`. If the task explicitly names a local plan item, use that plan without requiring Jira.
Ask which tracker only when the reference is ambiguous. For an actual Jira issue, read it through
the installed `atlassian-readonly-skills` capability first. If it
is unavailable, ask the user to paste the summary, description, status, priority, assignee, comments,
and reproduction details. Do not claim an issue was fetched or updated without successful evidence.

Do not infer a Jira project key, LT prefix, tenant, or cloud/Data-Center schema. Local Motif IDs and
plan references are not automatically tickets. Assignment, transition, comment, PR, push, merge, and
closure each require explicit authority; discover identity or transition fields only after that
authority is granted.

For an authorized code fix:

1. Confirm branch and scope, preserving unrelated dirty changes.
2. Write a regression test and run `./test.ps1` before implementation. Record the observed failure and
   confirm it exposes the defect, not a build/setup error. Then implement the smallest fix and rerun
   to demonstrate green. If reproduction is blocked, report why and agree an alternative validation
   before claiming a fix. Add relevant negative,
   boundary, isolation, or related-path tests when the risk warrants them.
3. Verify with `./build.ps1` and `./test.ps1` as applicable. These scripts are the repository commands;
   `test.ps1` has no `TestFilter` or `TestProject` switches.
4. Obtain an independent review when risk, ambiguity, or cross-boundary impact justifies it. Check LibLCM
   lifecycle/authority, atomic Proposal behavior, exact identity and normalization, Dry Run versus
   Assessment, CLI/JSON contracts, Avalonia, `net10.0`, and the pre-1.0 ban on migration/back-compat
   scaffolding.
5. Report the issue facts, test-first evidence, additional coverage, review result, remaining limits,
   and any external writes separately. Never post a Jira update or claim a PR link unless authorized
   and actually completed.
