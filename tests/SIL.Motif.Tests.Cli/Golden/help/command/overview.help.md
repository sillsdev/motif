# Read the project Overview

`overview` presents the latest evidence Motif has already stored for a project: counts, Text Coverage, agreement with manual analyses, timing, and warnings. Add `--proposal <id>` to read the latest Trial recorded for that Proposal.

## When to use it

Use it for a quick read of where the project stands. The Overview is a projection of stored evidence; returning to it does not capture a Baseline or start PanGloss.

## Example

```powershell
motif overview --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
motif overview --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --proposal <proposalId> --json
```

## What it prints

Human output shows the available project measurements and warnings. The **Work** line gives a percentage change only when an exact paired Trial and Baseline Assessment can be compared over the selected words. Otherwise it explains that the comparison is unavailable. Warnings give counts by level and, when a stored Assessment matches, how many words use a named object by identity; spelling candidates are shown separately and are not confirmed uses. JSON emits the same Overview in a structured response.

The warning word headline counts exact uses only. Membership candidates and spelling candidates have separate counts; spelling candidates are not confirmed uses of the named item.

## Related commands

- [Measure a Selection](cmd:assess) records new parser evidence.
- [Read grammar warnings](cmd:warnings) inspects stored grammar-check findings.
