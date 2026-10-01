# Read the project Overview

`overview` presents the latest evidence Motif has already stored for a project: counts, Text Coverage, agreement with manual analyses, timing, and warnings.

## When to use it

Use it for a quick read of where the project stands. The Overview is a projection of stored evidence; returning to it does not capture a Baseline or start PanGloss.

## Example

```powershell
motif overview --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

Human output shows the available project measurements and warnings. Timing gives the words' total word time, each word's parse time added up, and its split by kind of rule, with the part no rule recorded shown as not attributed. Warnings give the counts by level and, when a stored Assessment matches, how many of your words use something a finding names, and how many of those don't parse. [Read grammar warnings](cmd:warnings) explains how those words are found. JSON emits the same Overview in a structured response. If nothing has been measured, the response reports that no evidence is available.

The warning word headline counts exact uses only. Membership candidates and spelling candidates have separate counts; spelling candidates are not confirmed uses of the phoneme.

## Related commands

- [Measure a Selection](cmd:assess) records new parser evidence.
- [Read grammar warnings](cmd:warnings) inspects stored grammar-check findings.
