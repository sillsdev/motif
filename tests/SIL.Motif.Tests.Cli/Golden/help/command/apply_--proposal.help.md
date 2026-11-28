# Apply one Proposal

`motif apply --proposal <id>` applies one Proposal to the FieldWorks project in one unit of work, then records a Receipt. A Proposal that is still a Draft is finalized first. It takes the same path as `apply --all-pending`: every change is checked once against the project, and a refused Apply reopens the Draft under the same id. FieldWorks is checked first: while it holds the project, Apply is refused before anything else is done.

## When to use it

Use it after reviewing a Proposal and trying it. The Proposal's latest Trial must be current: neither the Proposal nor the project may have changed since. Close the project in FieldWorks first, and reload it afterwards.

## Example

```text
motif apply --proposal <id> --project <project> [--autonomous] [--message <text>] [--note] [--effects] [--json]
```

## Mode, message and note

Autonomous Apply requires a gate declared in `<project>.motif.toml`. Without one it refuses with `apply.gate-undeclared` and prints the exact TOML to add. Declare your gold word list as a named review set and list its id in `[gate] word-sets`; [project settings](cmd:config%20show) shows the format. The gate reuses the existing `[gain-gate]` policy and `[parsimony] block-on` list.

The newest Trial must have measured the required sets under the current declaration and have a saved `green` gate. Motif checks the loaded live project's semantic state and writing-system files under the Apply lease before writing. A changed project, word-set source or policy, missing or historical evidence, a failed or pending newest Trial, incomplete evidence, an unexpected loss, or a new second reading held for review is refused with `apply.trial-gate`. Motif does not fall back to an older green Trial. Capture a fresh Baseline after an external save, then start a fresh Trial with `--wait`. Inspect its named losses, stopped words and held words. Supply `--expect-loss <file>` only for intended losses when starting that Trial; it does not excuse incomplete measurement or a second-reading hold.

Every Receipt records how the Proposal was applied, `person` or `autonomous`, and a message. With no marker the mode is `person`, and the message defaults to the Proposal's label; `--message <text>` replaces it (one line, at most 128 characters). An agent declares Autonomous mode with `--autonomous` or by setting `MOTIF_AUTONOMOUS=1` in its environment, and an autonomous Apply is refused with `apply.message-required` unless it gives a `--message`; combining Autonomous mode with `--force` is refused with `apply.autonomous-force`. The window always records `person`.

`--note` also writes one Notebook record of the "Motif change" type in the same unit of work as the changes: its title is the Proposal's label, and its text is the Rationale, the Receipt reference and the message. It is off by default. A later Proposal applied with `--note` writes its own note, and the earlier note remains.

## What it prints

Text output summarizes operation counts by kind, observed effects and warnings, then gives the path to the complete effects list under the project's worker-root `artifacts/effects` folder. Add `--effects` to print the list in the terminal. The JSON response has `ok`, `applied`, a one-line `summary` and the full `effects` array inside its `receipt`, with an `effectsSummary` alongside it. A Proposal that has not been tried is refused with `apply.trial-missing`; one that changed since its Trial is refused with `apply.trial-stale`. If the response contains `apply.reconciliation-needed`, reload and inspect the project because a write may have happened. When its message says the change was saved but not recorded, run `motif apply <id> --project <p>` again: Motif records the Receipt only if the project's applied log holds the Proposal with a matching intent digest, then reports it as already applied. While a reconciliation warning stands, a Proposal the log does not hold is refused; inspect the project, then choose Refresh in the Motif window. The refusal's `detail.cause` and `detail.causeSqliteCode` name the underlying failure.

## Readiness

Apply can be refused with `apply.not-ready` when the current Trial has no Correctness Assessment for this revision. A timing-only Trial supplies no Correctness Assessment. A Correctness result with zero approved expectations supplies no comparison of approved readings. In a project with no stems, inspect the recorded parse results and comparisons. `--force` does not skip Readiness here; only [`motif apply <proposalId> --force`](cmd:apply) does, and Autonomous mode refuses `--force` everywhere, so collect the required evidence before applying. Apply and the Review window read the newest Correctness Assessment of the revision that leaves the Proposal ready, checking the newest ten at most and stopping at the first ready one; an older Assessment past those ten is not read. A Trial names any grammar the parser dropped with only a warning, such as an environment it could not read. When that dropped grammar is in what the Proposal adds or edits, the Trial is not clean, and Apply is refused with `apply.dropped-grammar` until it is fixed, in the Proposal or in FieldWorks, and the Proposal is tried again. When the parser could not check for dropped grammar, or the Trial was made before the check existed, the Trial says so and is not clean; Apply is then refused with `apply.not-ready` until the Proposal is tried again.

## Related commands

See [Review a Proposal's changes](cmd:proposal%20review) and [Apply pending changes](cmd:apply%20--all-pending).

Apply does not report before/after project hashes. Its save check verifies the applied-log entry; the effect digest identifies the effects list.

The summary shows at most ten warnings, each limited to 240 characters, with total and omitted counts. Complete warnings remain in the effects list. Effects files expire after seven days; a per-project 64 MiB budget may remove files older than one day sooner.

## Require a Trial receipt

`--receipt <file>` makes Apply stop unless the file is a green receipt written by `proposal trial delta --write-receipt` for this Proposal's current revision, the current Baseline and this build of the gate rules: `apply.receipt-invalid` for a missing, edited or non-green file, `apply.receipt-stale` for a changed revision, Baseline, Trial or gate build. It is checked before the project is loaded, again against the loaded FieldWorks data (a save made after the Baseline is `apply.receipt-stale`), and also with `--force`.
