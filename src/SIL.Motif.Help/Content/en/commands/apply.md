# Apply with Readiness override

This CLI command applies a Proposal using its already bound Dry Run. The optional `--force` switch lets a person proceed (Autonomous mode refuses it) when a Readiness reason remains.

## Usage

```text
motif apply <proposalId> --project <fwdata> [--force] [--autonomous] [--message <text>] [--note] [--json]
```

## Readiness and `--force`

Without `--force`, Apply can be refused with `apply.not-ready` when its current evidence does not satisfy Readiness. A timing-only Trial supplies no Correctness Assessment. A Trial that requests Correctness can record a result with zero approved expectations; that result supplies no comparison of approved readings. In a project with no stems, inspect the recorded parse results and approved comparisons before deciding whether to proceed.

`--force` skips the Readiness reasons for this command. It does not create or change an Assessment, and it does not bypass checks that the proposed changes still fit the current project. Changes that no longer fit, or whose fit is uncertain, remain refused.

## Mode, message and note

Every Receipt records how the Proposal was applied, `person` or `autonomous`, and a message. With no marker the mode is `person`, and the message defaults to the Proposal's label; `--message <text>` replaces it (one line, at most 128 characters). An agent declares Autonomous mode with `--autonomous` or by setting `MOTIF_AUTONOMOUS=1` in its environment, and an autonomous Apply is refused with `apply.message-required` unless it gives a `--message`; combining Autonomous mode with `--force` is refused with `apply.autonomous-force`. The window always records `person`.

`--note` also writes one Notebook record of the "Motif change" type in the same unit of work as the changes: its title is the Proposal's label, and its text is the Rationale, the Receipt reference and the message. It is off by default. A later Proposal applied with `--note` writes its own note, and the earlier note remains.

Apply is not available to MCP clients. The other Released paths are [Apply one Proposal](cmd:apply%20--proposal) and [Apply pending changes](cmd:apply%20--all-pending).
