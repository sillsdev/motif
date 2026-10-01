# Read grammar warnings

`warnings` reads grammar-check findings already stored for a project. It can filter by diagnostic code or show only findings at warning level.

## When to use it

Use it after [Check a grammar](cmd:grammar%20check) when you want to inspect reported findings. Combine `--kind` and `--left-out` to narrow a large result without rerunning the check.

## Example

```powershell
motif warnings --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --left-out --json
```

## What it prints

The response includes separate error, warning, and information counts, plus findings with descriptions, guidance, subjects, and links where available. `--left-out` keeps warning-level findings only. With no Baseline, the command says to capture one; when a Baseline has no stored check, it says the grammar has not been checked yet.

## Your words

When a stored Assessment from [Measure a Selection](cmd:assess) matches the current Baseline and Selection, each finding also lists the words in the Selection that use what it names, with how many hold each meaning, such as Lost or Kept. The response counts these words once each over all the findings, and says how many of them don't parse. Each kind of finding gets the same count.

Motif finds the words from the object the finding names, by its FieldWorks identity:

- an allomorph or grammatical info: the stored analyses that use it;
- an entry or an environment: the stored analyses that use its allomorphs;
- a sense: the stored analyses that use its grammatical info;
- a natural class: the allomorphs its environments condition, and the words the phonological rules that name it ran in;
- a rule: the words its stored per-word timings say it ran in.

A finding that names only letters or phonemes matches words by spelling instead, and says so. A finding that names nothing, or an object Motif doesn't follow to words, says it can't tell. The words show use, not cause: a word that uses what a warning names may fail for some other reason.

Without a matching Assessment, the findings carry no words, and the count is absent rather than zero.

## Related commands

- [Check a grammar](cmd:grammar%20check) records fresh findings.
- [Read the project Overview](cmd:overview) includes the warning count with other stored evidence.
