# Apply one Proposal

`motif apply --proposal <id>` applies one Proposal to the FieldWorks project in one unit of work, then records a Receipt. A Proposal that is still a Draft is finalized first. It takes the same path as `apply --all-pending`: every change is checked once against the project, and a refused Apply reopens the Draft under the same id. FieldWorks is checked first: while it holds the project, Apply is refused before anything else is done.

## When to use it

Use it after reviewing a Proposal and trying it. The Proposal's latest Trial must be current: neither the Proposal nor the project may have changed since. Close the project in FieldWorks first, and reload it afterwards.

## Example

```text
motif apply --proposal <id> --project <project> [--autonomous] [--message <text>] [--note] --json
```

## Mode, message and note

Every Receipt records how the Proposal was applied, `person` or `autonomous`, and a message. With no marker the mode is `person`, and the message defaults to the Proposal's label; `--message <text>` replaces it (one line, at most 128 characters). An agent declares Autonomous mode with `--autonomous` or by setting `MOTIF_AUTONOMOUS=1` in its environment, and an autonomous Apply is refused with `apply.message-required` unless it gives a `--message`; combining Autonomous mode with `--force` is refused with `apply.autonomous-force`. The window always records `person`.

`--note` also writes one Notebook record of the "Motif change" type in the same unit of work as the changes: its title is the Proposal's label, and its text is the Rationale, the Receipt reference and the message. It is off by default. A later Proposal applied with `--note` writes its own note, and the earlier note remains.

## What it prints

The JSON response has `ok`, `applied`, and a one-line `summary`, as `apply --all-pending` does. A Proposal that has not been tried is refused with `apply.trial-missing`; one that changed since its Trial is refused with `apply.trial-stale`. If the response contains `apply.reconciliation-needed`, reload and inspect the project because a write may have happened.

## Readiness

Apply can be refused with `apply.not-ready` when the current Trial has no Correctness Assessment for this revision. A timing-only Trial supplies no Correctness Assessment. A Correctness result with zero approved expectations supplies no comparison of approved readings. In a project with no stems, inspect the recorded parse results and comparisons. This Released command has no `--force` option, so collect the required evidence before applying.

## Related commands

See [Review a Proposal's changes](cmd:proposal%20review) and [Apply pending changes](cmd:apply%20--all-pending).
