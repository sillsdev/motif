# Read a Proposal's Parsimony status

`parsimony status` says whether Parsimony has compared a Proposal's candidate grammar with the Baseline. A committed Trial queues that comparison on its own, behind all other work, so the answer is usually "queued", "running" or "ready" without anyone asking for it.

The overall state is one of `not-set-up` (PanGloss is missing), `not-yet-run`, `queued`, `running`, `ready`, `stale` (an input changed since the run, with the reasons listed) or `failed` (with a plain category such as `timed-out` for a very large grammar). The Grammar group runs every time. The Text group needs a saved Parsimony Selection and reports `not-set-up` until there is one.

Parsimony is advisory. A failed, timed-out or cancelled run never changes the Trial, Readiness or Apply, and there is no single score: each measure reports its own new, resolved, unchanged and changed-evidence findings. This command reads stored jobs and Reports and never starts a parse.

## Example

```powershell
motif parsimony status --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --proposal proposal/AbCd --json
```

## Timeouts and freshness

When PanGloss ran past its time limit on a grammar, Motif does not queue another automatic run for the same Baseline and PanGloss; the status reads `failed` with category `timed-out`. Run `motif parsimony` by hand to try again. A new Baseline or a new PanGloss clears this by itself.

A `ready` status was checked against the Baseline, the Proposal, its prerequisites, PanGloss and the measures. A changed human judgment or Parsimony Selection is not checked.

## Related commands

- [Review grammar parsimony](cmd:parsimony) runs a measure or group by hand.
- [Show a Parsimony Report](cmd:parsimony%20show) reads a stored Report.
