# Withdraw a Proposal

`motif withdraw --proposal <id>` takes back an open Proposal and marks it withdrawn. The Proposal and its Trials stay in the project's closed list; the changes are not applied.

## When to use it

Use it when an author no longer wants an open Proposal to go forward. Motif finalizes a Draft before withdrawing it, so its content remains available if someone reopens it. This command does not withdraw your own pending changes; undo those on **Review changes** instead.

This command requires Advanced AI mode. Turn it on in **AI assistants** or run `motif settings advanced-ai on`.

## Example

```text
motif withdraw --proposal <id> --project <project> --json
```

## What it prints

The Proposal's id and new status, `withdrawn`. A person can reopen a withdrawn Proposal as a Draft under the same id, with its history kept.

## Related commands

- [List Proposals](cmd:proposals) shows withdrawn Proposals with `--include-set-aside`.
- [Reopen a Proposal](cmd:reopen%20--proposal) opens a withdrawn Proposal as a Draft.
- [Delete a closed Proposal](cmd:delete%20--proposal) removes a withdrawn Proposal that never applied.
