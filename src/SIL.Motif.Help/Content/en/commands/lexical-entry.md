# Read a lexical entry

`lexical-entry` reads an entry from the current Baseline by its id, or by the id of one of its senses, and returns the headword and every sense with its glosses. A Proposal change names a sense by id; this read says which entry and which sense that is, and what the other senses are, before any Dry Run or Trial.

## Example

```text
motif lexical-entry --project Koro.fwdata 6ZbA3mwkQkS3EhX0tQ5ihQ --json
```

## What it prints

One lookup per id, in the order asked. A found lookup has `entryId`, `headword` and `senses`; each sense has `senseId`, `outline` (FieldWorks' sense number, such as `2` or `2.1`), `gloss` (the best analysis-language gloss) with its `glossWritingSystem`, and `glosses` for every writing system. When the id named a sense, `senseId` on the lookup is that sense, so it can be highlighted among the others. An id the Baseline does not hold as an entry or a sense comes back with `found` false. The response also names the Baseline and the FieldWorks save it represents.

At most 100 ids are read in one call. The read uses the saved Baseline copy, never the live project, and needs no Trial. It is refused when no Baseline has been captured.

## Related commands

- [Capture a Baseline](cmd:baseline%20capture) records the saved project state.
- [Show a Proposal](cmd:show) with `--current` returns the value each change's field holds now.
