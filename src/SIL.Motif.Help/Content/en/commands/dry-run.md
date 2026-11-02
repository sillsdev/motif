# Dry Run a Proposal

Queues a Dry Run of a Draft or finalized Proposal on a throwaway copy of its Baseline. Its effects are bound to that content and project state. Read the effects, revise the Draft when needed, then Trial it before Finalize.

After Motif's own Apply, the next Dry Run captures a new Baseline by itself, so no step is needed. After a save made outside Motif, such as one from FieldWorks, a Dry Run is refused with `change.refresh-required` until you capture a new Baseline with `motif baseline capture` (in the window, Refresh). The evidence it would give is otherwise about a project that no longer exists.

If the wait ends before the job finishes, poll its current state with `motif jobs show <jobId> --project <fwdata> --json`, using the returned job id. See [jobs show](cmd:jobs%20show).

