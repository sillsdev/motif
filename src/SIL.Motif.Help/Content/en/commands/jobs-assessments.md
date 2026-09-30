# Read job Assessments

`jobs assessments` lists the Assessments produced by one completed job.

## When to use it

Use this after `jobs show` reports that a job completed and you need its evidence ids. The returned ids can be passed to reports, comparisons, or statistics queries.

## Example

```powershell
motif jobs assessments <jobId> --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

The response lists the Assessment ids and their recorded kinds. If the job has not produced Assessments, the list is empty; the command does not start another run.

## Related commands

- [Inspect a job](cmd:jobs%20show) checks its state.
- [Read an Assessment report](cmd:report) queries one stored Assessment.
