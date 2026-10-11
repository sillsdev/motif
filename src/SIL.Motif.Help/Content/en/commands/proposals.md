# List Proposals

`proposals` lists every Proposal in a project, grouped by what to do with it, with what each one changes and the numbers its latest [Trial](term:trial) measured beside today's.

## When to use it

Use it to see what is waiting for review, what needs another try, and which changes are your own and not yet applied. The Proposals page in the window reads the same list.

## Example

```powershell
motif proposals --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
motif proposals --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --include-set-aside --json
```

## What it prints

Each Proposal carries its label, status and group: **WaitingForYou** (a complete Trial matches the project and the Proposal as they are now), **NeedsAnotherTry** (the project or the Proposal moved since the Trial, or its latest Trial is running or did not finish), **NotTried** (no Trial exists), **NotAppliedYet** (your own collected changes) or **SetAside** (closed: applied, rejected, superseded or withdrawn). Closed Proposals are counted in `setAsideCount` and listed only with `--include-set-aside`. A deferred Proposal is still open and is grouped by its Trial like a proposed one.

A Proposal also lists its change kinds, such as two rules, one allomorph and one approved parse, and the ids of other Proposals that change one of the same words. Its Trial summary pairs five numbers, each as a value before and after: Text Coverage, approved words kept, approved words, parse time and the warning count. A Trial over only the touched words still pairs the totals: every word it did not measure keeps today's value. Chips count approved words the Trial lost and changes that no longer fit.

## Trying a Proposal again

In the window, **Try again** on a card measures the Proposal on a copy of the project. The copy is thrown away and your FieldWorks project is not changed. A card whose Trial is still running says **Trying…** and offers no second button.

## Related commands

- [Read the project Overview](cmd:overview) reads one Proposal's Trial with `--proposal`.
- [Delete a closed Proposal](cmd:delete%20--proposal) removes a closed Proposal that never applied.
