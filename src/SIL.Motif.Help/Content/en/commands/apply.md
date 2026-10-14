# Apply with Readiness override

This CLI command applies a Proposal using its already bound Dry Run. The optional `--force` switch lets a person proceed when a Readiness reason remains.

## Usage

```text
motif apply <proposalId> --project <fwdata> [--force] [--json]
```

## Readiness and `--force`

Without `--force`, Apply can be refused with `apply.not-ready` when its current evidence does not satisfy Readiness. A timing-only Trial supplies no Correctness Assessment. A Trial that requests Correctness can record a result with zero approved expectations; that result supplies no comparison of approved readings. In a project with no stems, inspect the recorded parse results and approved comparisons before deciding whether to proceed.

`--force` skips the Readiness reasons for this command. It does not create or change an Assessment, and it does not bypass checks that the proposed changes still fit the current project. Changes that no longer fit, or whose fit is uncertain, remain refused.

Only a person can Apply. The other Released paths are [Apply one Proposal](cmd:apply%20--proposal) and [Apply pending changes](cmd:apply%20--all-pending).
