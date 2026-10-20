# Dry Run a Proposal

Queues a Dry Run of a Draft or finalized Proposal on a throwaway copy of its Baseline. Its effects are bound to that content and project state. Read the effects, revise the Draft when needed, then Trial it before Finalize.

If the wait ends before the job finishes, poll its current state with `motif jobs show <jobId> --project <fwdata> --json`, using the returned job id. See [jobs show](cmd:jobs%20show).

