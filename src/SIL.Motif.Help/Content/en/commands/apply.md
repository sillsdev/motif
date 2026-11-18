# Apply with Readiness override

This CLI command applies a Proposal using its already bound Dry Run. The optional `--force` switch lets a person proceed (Autonomous mode refuses it) when a Readiness reason remains.

## Usage

```text
motif apply <proposalId> --project <fwdata> [--force] [--autonomous] [--message <text>] [--note] [--effects] [--json]
```

Text output summarizes operation counts by kind, observed effects and warnings, then gives the path to the complete effects list under the project's worker-root `artifacts/effects` folder. Add `--effects` to print the list in the terminal. `--json` keeps the full `effects` array and adds an `effectsSummary` object.

After Apply has saved the project, the next Dry Run or Trial of another Proposal captures a new Baseline by itself. A save made outside Motif is different: a Dry Run is refused with `change.refresh-required` until you capture a new Baseline with `motif baseline capture` (in the window, Refresh).

## Readiness and `--force`

Without `--force`, Apply can be refused with `apply.not-ready` when its current evidence does not satisfy Readiness. A timing-only Trial supplies no Correctness Assessment. A Trial that requests Correctness can record a result with zero approved expectations; that result supplies no comparison of approved readings. In a project with no stems, inspect the recorded parse results and approved comparisons before deciding whether to proceed. Apply and the Review window read the newest Correctness Assessment of the revision that leaves the Proposal ready, checking the newest ten at most and stopping at the first ready one; an older Assessment past those ten is not read. A Trial names any grammar the parser dropped with only a warning, such as an environment it could not read. When that dropped grammar is in what the Proposal adds or edits, the Trial is not clean, and Apply is refused with `apply.dropped-grammar` until it is fixed, in the Proposal or in FieldWorks, and the Proposal is tried again. When the parser could not check for dropped grammar, or the Trial was made before the check existed, the Trial says so and is not clean; Apply is then refused with `apply.not-ready` until the Proposal is tried again.

`--force` skips the Readiness reasons for this command. It does not create or change an Assessment, and it does not bypass checks that the proposed changes still fit the current project. Changes that no longer fit, or whose fit is uncertain, remain refused.

## Mode, message and note

Autonomous Apply also requires the newest Trial of this exact Proposal content and current project state to have a saved `green` gate (ADR 0017 and ADR 0021). Missing or historical gate evidence, a failed or pending newest Trial, unmeasured evidence, or an unexpected loss is refused with `apply.trial-gate`; Motif does not fall back to an older green Trial. Start a fresh Trial with `--wait`, inspect its named losses and stopped words, and supply `--expect-loss <file>` when starting that Trial only for intended losses. Every measured loss must be covered; the saved Trial verdict keeps those exemptions.

Every Receipt records how the Proposal was applied, `person` or `autonomous`, and a message. With no marker the mode is `person`, and the message defaults to the Proposal's label; `--message <text>` replaces it (one line, at most 128 characters). An agent declares Autonomous mode with `--autonomous` or by setting `MOTIF_AUTONOMOUS=1` in its environment, and an autonomous Apply is refused with `apply.message-required` unless it gives a `--message`; combining Autonomous mode with `--force` is refused with `apply.autonomous-force`. The window always records `person`.

`--note` also writes one Notebook record of the "Motif change" type in the same unit of work as the changes: its title is the Proposal's label, and its text is the Rationale, the Receipt reference and the message. It is off by default. A later Proposal applied with `--note` writes its own note, and the earlier note remains.

Apply is not available to MCP clients. The other Released paths are [Apply one Proposal](cmd:apply%20--proposal) and [Apply pending changes](cmd:apply%20--all-pending).

Apply does not report before/after project hashes. Its save check verifies the applied-log entry; the effect digest identifies the effects list.

The summary shows at most ten warnings, each limited to 240 characters, with total and omitted counts. Complete warnings remain in the effects list. Effects files expire after seven days; a per-project 64 MiB budget may remove files older than one day sooner.
