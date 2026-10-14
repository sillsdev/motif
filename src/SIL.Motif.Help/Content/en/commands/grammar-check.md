# Check a grammar

`grammar check` runs the grammar checker against the project's current Baseline and stores its findings. If the project has no Baseline, the command refuses the request; run `motif baseline capture --project <fwdata>` and try again.

## When to use it

Use this to collect diagnostics about the grammar, then read them with `warnings`. It is separate from an Assessment, which measures parsing over a Selection.

## Example

```powershell
motif grammar check --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## How much of the comparison runs

One part of the check re-parses each word to see whether its stored analyses still parse, so it is the part that slows down as a project grows. `--stored-analyses` chooses how much of it runs, for this check only:

- `budget` compares words in order until a fixed amount of work is spent, then reports how many words were left. This is the default.
- `all` compares every word. A large project can take half a minute or more.
- `off` skips the comparison; the checks that need no parsing still run.

Without the flag, the check follows the **Compare stored analyses** choice under *Default proposal checks* in Settings. The same choice applies to the grammar check a Trial runs for a Proposal, so a Proposal's check and the project's are comparable only when they used the same choice.

## What it prints

The response includes the Baseline's lexical entry and rule counts when that inventory is available. Motif advisories are separate from PanGloss findings, so an empty-lexicon note does not change parser severity or Assessment readiness. Error-level findings appear separately from warnings and information. PanGloss can exit nonzero after writing a report with errors; Motif still reads and stores that report. A nonzero exit without a report is a refusal.

## Related commands

- [Read grammar warnings](cmd:warnings) filters and displays stored findings.
- [Measure a Selection](cmd:assess) measures parser behavior rather than grammar diagnostics.
