# Apply pending changes

`motif apply --all-pending` applies all changes in the current pending Draft Proposal to the FieldWorks project in one unit of work. Motif records a Receipt when it writes changes.

## When to use it

Use this command after reviewing the pending Proposal and while FieldWorks has released the project. FieldWorks should reload the project after Motif reports a write.

A Proposal made only of the bounded Notebook judgment writes can be ready without a Correctness Assessment when its current bound Dry Run shows only Notebook record, owned-text and reserved-field effects. Mixed Proposals still need normal Readiness evidence. The Apply result records whether Correctness was exempt and why; Dry Run, Preflight and Apply still apply.

When Apply is refused with `apply.not-ready`, the current evidence does not satisfy Readiness. A timing-only Trial supplies no Correctness Assessment. A Correctness result with zero approved expectations supplies no comparison of approved readings. In a project with no stems, inspect the recorded parse results and comparisons. `--force` does not skip Readiness here; only [`motif apply <proposalId> --force`](cmd:apply) does, and Autonomous mode refuses `--force` everywhere. A Trial names any grammar the parser dropped with only a warning, such as an environment it could not read. When that dropped grammar is in what the Proposal adds or edits, the Trial is not clean, and Apply is refused with `apply.dropped-grammar` until it is fixed, in the Proposal or in FieldWorks, and the Proposal is tried again. When the parser could not check for dropped grammar, or the Trial was made before the check existed, the Trial says so and is not clean; Apply is then refused with `apply.not-ready` until the Proposal is tried again.

## Example

```text
motif apply --all-pending --project <project> [--revision <r>] [--autonomous] [--message <text>] [--note] [--effects] [--json]
```

## Mode, message and note

Autonomous Apply also requires the newest Trial of this exact Proposal content and current project state to have a saved `green` gate (ADR 0017 and ADR 0021). Missing or historical gate evidence, a failed or pending newest Trial, unmeasured evidence, or an unexpected loss is refused with `apply.trial-gate`; Motif does not fall back to an older green Trial. Start a fresh Trial with `--wait`, inspect its named losses and stopped words, and supply `--expect-loss <file>` when starting that Trial only for intended losses. Every measured loss must be covered; the saved Trial verdict keeps those exemptions.

Every Receipt records how the Proposal was applied, `person` or `autonomous`, and a message. With no marker the mode is `person`, and the message defaults to the Proposal's label; `--message <text>` replaces it (one line, at most 128 characters). An agent declares Autonomous mode with `--autonomous` or by setting `MOTIF_AUTONOMOUS=1` in its environment, and an autonomous Apply is refused with `apply.message-required` unless it gives a `--message`; combining Autonomous mode with `--force` is refused with `apply.autonomous-force`. The window always records `person`.

`--note` also writes one Notebook record of the "Motif change" type in the same unit of work as the changes: its title is the Proposal's label, and its text is the Rationale, the Receipt reference and the message. It is off by default. A later Proposal applied with `--note` writes its own note, and the earlier note remains.

## What it prints

Text output summarizes operation counts by kind, observed effects and warnings, then gives the path to the complete effects list under the project's worker-root `artifacts/effects` folder. Add `--effects` to print the list in the terminal. The JSON response has `ok`, `applied`, `summary` and, when changes were applied, a `receipt` that carries the Readiness decision and full `effects` array, with an `effectsSummary` alongside it. Reload after `applied: true`. When the response carries `apply.reconciliation-needed`, a write may have happened or an earlier one was never confirmed. Reload, open Motif to check the project, and do not retry automatically. A no-op does not need a reload, and an uncertain change stays pending for review.

## Related commands

- [Read the project Overview](cmd:overview) reads stored evidence after you reload the project.

Apply does not report before/after project hashes. Its save check verifies the applied-log entry; the effect digest identifies the effects list.

The summary shows at most ten warnings, each limited to 240 characters, with total and omitted counts. Complete warnings remain in the effects list. Effects files expire after seven days; a per-project 64 MiB budget may remove files older than one day sooner.
