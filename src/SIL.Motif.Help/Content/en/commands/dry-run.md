# Dry Run a Proposal

Queues a Dry Run of a Draft or finalized Proposal on a throwaway copy of its Baseline. Its effects are bound to that content and project state. Read the effects, revise the Draft when needed, then Trial it before Finalize.

The frozen job input, including the Proposal and its prerequisites, must fit within 1 MiB of UTF-8 JSON. If it exceeds that bound, `job.input-too-large` reports the size and queues nothing. Split the Proposal into smaller Proposals and retry.
