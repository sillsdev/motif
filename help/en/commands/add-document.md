# Add a Corpus Document

`add-document` reads one source file or URL and stores its text as a Document in a named Corpus. It records the source, title, and licence without treating the Document as a FieldWorks Text.

## When to use it

Use this after creating a Corpus when you need to preserve where one article or file came from. Provide a stable Document id and the source location; include licence details for this item when they differ from the Corpus.

## Example

```powershell
motif add-document --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --corpus "stories" --doc "story-12" --source "https://example.org/story-12" --title "The River Path"
```

## What it prints

Human output confirms the Corpus and Document ids and shows the title, source, character count, content hash, and licence when supplied.

## Related commands

- [Describe a Corpus](cmd:add-corpus) records the shared provenance and tokeniser.
- [Show a Corpus](cmd:show-corpus) reads its provenance and licence capabilities.
