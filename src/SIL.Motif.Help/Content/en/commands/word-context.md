# Read one word's context

`word-context` reads one word's captured FieldWorks presence and all of its stored analyses, including Approved, Disapproved and Unknown opinions. It uses the same query as Try a Word and does not require an Assessment or membership in a Selection.

## Example

```text
motif word-context --project Koro.fwdata --word dogs --json
```

## What it prints

The response includes the exact Baseline identity, its source-save and publication times, word membership, each stored analysis with its identity and individual opinion, and an exact Word Analyses link when available. `isStale` says whether the saved FieldWorks project is newer than the Baseline's source save. A later save does not replace the captured analyses.

Before a Baseline exists, membership is unknown and no analysis context is supplied; this does not mean the word is absent. A missing Baseline file is refused. When several wordforms have the same form, their stored analyses retain their own identities and no unique navigation target is invented.

## Related commands

- [Capture a Baseline](cmd:baseline%20capture) records the saved project state.
- [Inspect approved analyses](cmd:analyses) reads the approved-analysis aggregate.
- [Trace a word](cmd:trace) records the parser's search.
