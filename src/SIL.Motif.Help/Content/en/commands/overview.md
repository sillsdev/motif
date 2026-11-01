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

Human output shows the available project measurements and warnings. The **Work** line gives the words' parser work in work steps: the total, the median and 95th percentile per word, the costliest word, and the split by kind of rule, with the part no rule recorded shown as not attributed. Work steps do not change with how busy this computer is, so compare them when judging a grammar change. The **Time on this computer** line follows as a secondary figure. Warnings give the counts by level and, when a stored Assessment matches, how many words use a named object by identity; spelling candidates are shown separately and are not confirmed uses. Each kind keeps its recorded level: errors, warnings or information. The window does not say **None of your words** when spelling or membership matches exist. [Read grammar warnings](cmd:warnings) explains how those words are found, including allomorph refusals recorded by parsing. JSON emits the same Overview in a structured response. If nothing has been measured, the response reports that no evidence is available.

The warning word headline counts exact uses only. Membership candidates and spelling candidates have separate counts; spelling candidates are not confirmed uses of the named item.

## Related commands

- [Measure a Selection](cmd:assess) records new parser evidence.
- [Read grammar warnings](cmd:warnings) inspects stored grammar-check findings.
