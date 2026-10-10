# Parsimony dry run wait

`parsimony --dry-run <job-id> --wait` queues the comparison of one measure before and after the Proposal behind a completed Dry Run job, and waits for the stored before and after Reports. It does not change the FieldWorks project. Use `--wait-timeout-ms` to set the wait bound; see [Review grammar parsimony](cmd:parsimony).
