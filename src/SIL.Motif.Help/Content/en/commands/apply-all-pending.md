# Apply pending changes

`motif apply --all-pending` applies all changes in the current pending Draft Proposal to the FieldWorks project in one unit of work. Motif records a Receipt when it writes changes.

## When to use it

Use this command after reviewing the pending Proposal and while FieldWorks has released the project. FieldWorks should reload the project after Motif reports a write.

A Proposal made only of the bounded Notebook judgment writes can be ready without a Correctness Assessment when its current bound Dry Run shows only Notebook record, owned-text and reserved-field effects. Mixed Proposals still need normal Readiness evidence. The Apply result records whether Correctness was exempt and why; Dry Run, Preflight and Apply still apply.

When Apply is refused with `apply.not-ready`, the current evidence does not satisfy Readiness. A timing-only Trial supplies no Correctness Assessment. A Correctness result with zero approved expectations supplies no comparison of approved readings. In a project with no stems, inspect the recorded parse results and comparisons. This Released command has no `--force` option.

## Example

```text
motif apply --all-pending --project <project> [--revision <r>] [--autonomous] [--message <text>] [--note] [--json]
```

## Mode, message and note

Every Receipt records how the Proposal was applied, `person` or `autonomous`, and a message. With no marker the mode is `person`, and the message defaults to the Proposal's label; `--message <text>` replaces it (one line, at most 128 characters). An agent declares Autonomous mode with `--autonomous` or by setting `MOTIF_AUTONOMOUS=1` in its environment, and an autonomous Apply is refused with `apply.message-required` unless it gives a `--message`; combining Autonomous mode with `--force` is refused with `apply.autonomous-force`. The window always records `person`.

`--note` also writes one Notebook record of the "Motif change" type in the same unit of work as the changes: its title is the Proposal's label, and its text is the Rationale, the Receipt reference and the message. It is off by default. A later Proposal applied with `--note` writes its own note, and the earlier note remains.

## What it prints

The JSON response has `ok`, `applied`, `summary` and, when changes were applied, a `receipt` that carries the Readiness decision. Reload after `applied: true`. When the response carries `apply.reconciliation-needed`, a write may have happened or an earlier one was never confirmed. Reload, open Motif to check the project, and do not retry automatically. A no-op does not need a reload, and an uncertain change stays pending for review.

## Related commands

- [Read the project Overview](cmd:overview) reads stored evidence after you reload the project.
