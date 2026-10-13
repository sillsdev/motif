# Trial a Proposal

Measures a Draft or finalized Proposal on a throwaway copy, producing a Dry Run and Assessments. Read negatives now parsing, positives lost, unfinished cases, other changes and counts before revising.

A word list that would exceed the 1 MiB job-input bound is stored separately beside the project's Motif store and checked by its content digest when read. The rest of the frozen input, including the Proposal and its prerequisites, must still fit. If it does not, `job.input-too-large` reports the size and queues nothing; split the Proposal into smaller Proposals and retry.
