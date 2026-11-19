# Dry Run a Proposal

Queues a Dry Run of a Draft or finalized Proposal on a throwaway copy of its Baseline. Its effects are bound to that content and project state. Read the summary, revise the Draft when needed, then Trial it before Finalize.

With `--wait`, Motif prints operation counts by kind, the number of effects, warnings and the Baseline's semantic project hash when available. It writes the complete effects list under the project's worker-root `artifacts/effects` folder. Add `--effects` to print that list in the terminal too. `--json` keeps the full `operations` and `effects` arrays and adds an `effectsSummary` object.

## Usage

```text
motif dry-run --project <fwdata> <proposalId> --wait [--wait-terminal] [--wait-timeout-ms <ms>] [--effects] [--json]
```

After Motif's own Apply, the next Dry Run captures a new Baseline by itself, so no step is needed. After a save made outside Motif, such as one from FieldWorks, a Dry Run is refused with `change.refresh-required` until you capture a new Baseline with `motif baseline capture` (in the window, Refresh). The evidence it would give is otherwise about a project that no longer exists.

Add `--wait-terminal` to cancel this Dry Run at the wait limit or on Ctrl+C. Motif allows up to eight more seconds to record cancellation and confirm cleanup. The refusal includes the job ID, the last observed status and whether cleanup was confirmed.

With an ordinary wait, poll a job that is still running with `motif jobs show <jobId> --project <fwdata> --json`, using the returned job id. See [jobs show](cmd:jobs%20show).


The summary shows at most ten warnings, each limited to 240 characters, with total and omitted counts. Complete warnings remain in the effects list. Effects files expire after seven days; a per-project 64 MiB budget may remove files older than one day sooner.
