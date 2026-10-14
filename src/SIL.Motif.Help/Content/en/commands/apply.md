# Apply with Readiness override

This CLI command applies a Proposal using its already bound Dry Run. The optional `--force` switch lets a person proceed when a Readiness reason remains.

## Usage

```text
motif apply <proposalId> --project <fwdata> [--force] [--json]
```

## Readiness and `--force`

Without `--force`, Apply can be refused with `apply.not-ready` when its current evidence does not satisfy Readiness. A project with no stems, or no approved morphology comparisons in the Trial, may have no Correctness candidate; timing-only evidence cannot satisfy the Correctness requirement. A Trial does not create a Correctness result when it has no approved morphology analyses to compare.

`--force` skips the Readiness reasons for this command. It does not create or change an Assessment, and it does not bypass checks that the proposed changes still fit the current project. Changes that no longer fit, or whose fit is uncertain, remain refused.

Only a person can Apply. The other Released paths are [Apply one Proposal](cmd:apply%20--proposal) and [Apply pending changes](cmd:apply%20--all-pending).
