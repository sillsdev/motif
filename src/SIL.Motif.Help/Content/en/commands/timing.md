# Inspect parse timing

`timing` reads timing measurements recorded in an Assessment. It can narrow the result to words and group costs by kind or rule. Add `--proposal <id>` to use the latest Trial's measurements.

## When to use it

Use this when an Assessment shows costly parsing and you want to identify which rules or kinds account for the parser's work. Name an Assessment when the project has more than one measurement.

Without `--assessment`, Timing reads the Default Selection's current results, including explicit replacements made with `assess --replaces`. Naming an Assessment reads its original words and measurements. Explicit timing overrides replace only words that run measured; they cannot add other words to it.

## Example

```powershell
motif timing --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --proposal <proposalId> --by rule --top 10
```

## What it prints

The command compares parser work only when the Trial retained a compatible Baseline Assessment for the selected words. It reports the percentage change for the selected scope and individual words. Object changes require complete work counters for the same words and a matching object identity on both sides. A new object, missing detail, a stopped search, a zero Baseline or an incompatible parser makes the affected comparison unavailable; the command gives a reason instead of estimating it.

`--rule` takes `kind:key`, such as `morph_rule:12345678-1234-1234-abcd-123456789abc`. Names are display labels; object identities distinguish rules with the same name. `--json` returns the comparisons as structured data.

JSON identifies each word's producing Assessment, invocation and measurement time. It also records the selected run's Baseline and captured FieldWorks save, when available. Its evidence relationship distinguishes Current, Historical, SavedSince and Unknown; current-project freshness is reported separately, so a new Baseline does not make an old measurement current.

## Related commands

- [Measure a Selection](cmd:assess) creates timing evidence.
- [Query parse statistics](cmd:stats) forwards a query to PanGloss for detailed counts.
