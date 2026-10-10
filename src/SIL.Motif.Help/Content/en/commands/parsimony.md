# Review grammar parsimony

`parsimony` queues an advisory review of the project's current Baseline. Select a registered check with `--measure` (it defaults to `P-adhoc-duplicate`); each Report records the measure's findings and full eligibility counts. Pass a completed Dry Run job id with `--dry-run` to compare the same measure before and after an exact Proposal on a private project copy.

## When to use it

Capture a Baseline first. This command does not capture or refresh one. Creating the Report requires PanGloss to import the Baseline and write grammar facts; the completed Report can be read later without PanGloss.

## Example

```powershell
motif parsimony --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --measure P-allo-duplicate-form --wait --json
```

To compare a Proposal, run `dry-run --wait` first, then pass its job id:

```powershell
motif parsimony --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --dry-run <job-id> --wait --json
```

The candidate Report records the same frozen Baseline source, Proposal revision, prerequisite revisions, and published Dry Run effects. Motif rebuilds the before and after evidence from their own project states and saves only the private candidate copy. A Dry Run without the required frozen evidence is refused with instructions to rerun it.

For a Baseline Report the default evidence scope is the project's saved Default Selection; add `--evidence-scope project-approved` to use every wordform with an Approved or Disapproved analysis. With `--dry-run` the default is `project-approved`, and `--evidence-scope default-selection` needs the Dry Run's own Baseline to be current. Typed words in a Default Selection keep no FieldWorks identity. Omit `--wait` to receive the queued job id, or use `--wait-timeout-ms` to set the wait bound.

Parser-tier measures use only ParseTime Assessments you name with `--assessment <id>`. Repeat the flag to supply more than one. Each Assessment must name the exact source and PanGloss executable used for the Report. Without an Assessment, a parser-tier measure reports `not-run`; a missing morphology sidecar reports `not-available`. A capped, timed-out, invalid, or unattributable case cannot establish a completed result.

For example, after `assess --json`, select its ParseTime Assessment id and name it on the report:

```powershell
motif parsimony --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --measure R-word-disapproved-produced --assessment <ParseTime-assessment-id> --wait --json
```

## What it reports

Each finding is advisory and remains tied to its exact evidence. Use `parsimony measures` to inspect registered checks and `parsimony view` to read their fixed evidence views. The `parser-cases` view includes Assessment provenance, case completion, ordered parser morphs, exact matches to Disapproved readings, reviewed-negative matches, and supported counters. An incomplete case can preserve an observed match in that view, but it cannot produce a completed parser finding or enter a completed-case denominator. The Report also names project and selected-scope counts for wordforms, readings and lexemes; it does not recommend an edit or change the project.

## Related commands

- [Read a Parsimony Report](cmd:parsimony%20show) opens a stored result.
- [List Parsimony measures](cmd:parsimony%20measures) shows registered checks and views.
- [Read a Parsimony view](cmd:parsimony%20view) reads captured evidence without rebuilding it.
- [Capture a Baseline](cmd:baseline%20capture) records the saved project state used by this review.
