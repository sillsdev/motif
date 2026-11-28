# Finalize a Proposal

Finalizes a Draft as an immutable Proposal revision for a person to review. Evidence for unchanged content on the same Baseline remains current.

The Draft must have both a short label and a Rationale before it can be finalized. If either is missing, set the label with `motif label` or the Rationale with [`proposal rationale`](cmd:proposal%20rationale), then retry Finalize. Finalize also refuses a Rationale with findings, and lists them with their lines and columns. When the Rationale links to the project and the Baseline cannot be read to check it, Finalize refuses until it can.
