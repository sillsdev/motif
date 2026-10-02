# Inspect parse timing

`timing` reads timing measurements recorded in an Assessment. It can narrow the result to words and group costs by kind or rule.

## When to use it

Use this when an Assessment shows slow parsing and you want to identify which rules or kinds account for the time. Name an Assessment when the project has more than one measurement.

Without `--assessment`, Timing reads the Default Selection's current results, including explicit replacements made with `assess --replaces`. Naming an Assessment reads its original words and times. Explicit timing overrides replace only words that run measured; they cannot add other words to it.

## Example

```powershell
motif timing --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --assessment <assessmentId> --by rule --top 10
```

## What it prints

The command prints the selected words' total word time: each word's parse time, added up. Every kind or rule is shown with its own measured time and its share of that same total. **Not attributed** is the part of the total that no rule or lookup recorded, such as parser work outside every rule's timer; it is never shared out among the rules. If rules ever recorded more time than their words took, the command says so instead of showing a negative remainder.

A word that stopped at a limit counts the time it spent before stopping. Calls are counted per kind of rule, because a call to one kind is not a call to another; they are never added across kinds.

With `--by rule`, each row shows its `kind:key` address in brackets, so two rules with the same name stay apart. `--rule` takes `kind:key`, such as `morph_rule:12345678-1234-1234-abcd-123456789abc`. Names are display labels only. Use `--structural` for an exact parser key even when it looks like a GUID. A grammar-local ordinal needs `--local --scope <scope>` using the scope returned with its timing row; it cannot be joined to a different run without recorded shared grammar provenance. `--json` returns rows and totals as structured data for further analysis.

JSON identifies each word's producing Assessment, invocation and measurement time. It also records the selected run's Baseline and captured FieldWorks save, when available. Its evidence relationship distinguishes Current, Historical, SavedSince and Unknown; current-project freshness is reported separately, so a new Baseline does not make an old measurement current.

## Related commands

- [Measure a Selection](cmd:assess) creates timing evidence.
- [Query parse statistics](cmd:stats) forwards a query to PanGloss for detailed counts.
