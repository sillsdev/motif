# List Corpora

`corpora` lists the Corpora recorded for a FieldWorks project with their provenance summaries.

## When to use it

Use this to find a Corpus id before inspecting it or adding a Document. A Corpus is material Motif holds with provenance; it is not a Text inside the FieldWorks project.

## Example

```powershell
motif corpora --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

Human output lists the recorded Corpora. JSON returns their ids and descriptive details for scripts that need to select one.

## Related commands

- [Describe a Corpus](cmd:add-corpus) records a new Corpus.
- [Show a Corpus](cmd:show-corpus) reads one Corpus in detail.
