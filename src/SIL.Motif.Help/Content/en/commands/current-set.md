# Set the Current chapter

`current set` makes a chapter the Current chapter of a Source on this machine, in the local file. Local and instant: no Draft. When the Source has a folder recorded the chapter is checked against its Paratext text and refused (`source.chapter-not-in-source`) if the text lacks it; with no folder it is taken as given.

## Example

```text
motif current set --project Koro.fwdata WEB "MAT 5"
```

## Related commands

- [Read the Current chapters](cmd:current%20get) reads them.
- [Forget it](cmd:current%20set%20--clear) removes the Current chapter.
