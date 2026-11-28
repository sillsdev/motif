# Describe a Corpus

`add-corpus` records a Corpus and how it was collected and tokenised. It keeps provenance and licence capabilities so later users can tell what use is permitted.

## When to use it

Use this before adding Documents to a new Corpus. State what the licence permits, including whether the material may be derived from, redistributed, or used commercially; unknown permissions should remain unknown.

## Example

```powershell
motif add-corpus --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --id "stories" --description "Published folktales" --tokeniser "motif-basic" --tokeniser-version "1"
```

## What it prints

Human output confirms the Corpus id and shows its origin, location when supplied, licence, tokenisation, and derivation note.

## Related commands

- [Add a Corpus Document](cmd:add-document) attaches a source to the Corpus.
- [List Corpora](cmd:corpora) finds recorded Corpus ids.
