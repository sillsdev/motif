# Check phonology examples

`phonology-examples check` queues an advisory check of one saved Example Set revision against the grammar in the project's current [Baseline](term:baseline). For each example it asks PanGloss to generate surface forms from the underlying form in the example's declared context and reports whether the expected form was observed. A check never changes the FieldWorks project, never changes Readiness, and Apply ignores it.

## When to use it

Use this after saving examples with a cited source, to see whether the grammar as captured produces the forms you expect, and which rules applied. A result is `agree` when an expected form was observed, `disagree` when the search completed without it, and `unavailable` when nothing usable was measured (for example no parser, an empty lexicon, or a context that names no entry). Unavailable is never a pass.

## Example

```powershell
motif phonology-examples check --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" <revisionId> --wait --json
motif jobs assessments --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" <jobId> --json
```

## What it prints

The id of the queued job. With `--wait` it waits for the job to finish and prints the job's status. The job records one Assessment of kind `PhonologyExamples`; `jobs assessments` gives its id, and [phonology-examples results](cmd:phonology-examples%20results) reads it. `--step-cap`, `--work-cap` and `--timeout-ms` bound the whole check. If no Baseline has been captured the command is refused; if the Baseline changes before the job runs, the job fails and the check must be requested again.

## Related commands

- [Save phonology examples](cmd:phonology-examples%20save) records the revision this command checks.
- [Read phonology check results](cmd:phonology-examples%20results) reads the stored result.
