# Inspect parse timing

`timing` reads timing measurements recorded in an Assessment. It can narrow the result to words and group costs by kind or rule.

## When to use it

Use this when an Assessment shows slow parsing and you want to identify which rules or kinds account for the time. Name an Assessment when the project has more than one measurement.

## Example

```powershell
motif timing --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --assessment <assessmentId> --by rule --top 10
```

## What it prints

The command prints the selected words' total word time: each word's parse time, added up. Every kind or rule is shown with its own measured time and its share of that same total. **Not attributed** is the part of the total that no rule or lookup recorded, such as parser work outside every rule's timer; it is never shared out among the rules. If rules ever recorded more time than their words took, the command says so instead of showing a negative remainder.

A word that stopped at a limit counts the time it spent before stopping. Calls are counted per kind of rule, because a call to one kind is not a call to another; they are never added across kinds.

With `--by rule`, each row shows its key in brackets, so two rules with the same name stay apart. `--rule` takes that key, or a name only one rule carries. `--json` returns rows and totals as structured data for further analysis.

## Related commands

- [Measure a Selection](cmd:assess) creates timing evidence.
- [Query parse statistics](cmd:stats) forwards a query to PanGloss for detailed counts.
