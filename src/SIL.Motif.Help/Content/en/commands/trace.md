# Trace a word

`trace` runs PanGloss for one word against the project's current Baseline and reads the diagnostic used by Try a Word. Add `--proposal <id>` to trace against that Proposal's Trial copy. It does not change the project or store a new Assessment.

## Trace a word now

```powershell
motif trace --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --word mirusi
```

Add `--json` to get the typed response, including the shared `reading` and the diagnostic's recorded provenance. A clean process exit does not establish that the parser's search limit was not reached; when the diagnostic cannot report that, completion is unknown.

If the project has no Baseline, capture one with `motif baseline capture <project>` before tracing. Trace uses that saved grammar; it does not inspect unsaved FieldWorks changes.

For an unparsed word, text output names the shape whose recorded lexical lookup found no root and explains recorded refusal codes. A stem that reaches an unapplied required template has a different reason from an attempt that builds the wrong surface. A recorded code can be explained without an owner's detailed explanation; absent details remain unavailable. A trace without recorded reasons says so, and an unfinished search cannot establish that the word has no analysis.

When Motif captures a trace, `hostCapture.lexiconEntryCount` records the number of lexical entries in the exact Baseline or Trial copy used. A completed trace with an empty lexicon names that fact and the word, instead of guessing which affix or spelling is wrong. A missing count is unknown. Saved traces retain this count and their recorded reasons without opening the current project.

## Read a saved trace

```powershell
motif trace --load "C:\Temp\mirusi.trace.json"
```

`--load` reads the saved PanGloss diagnostic without opening a project or running a parser. It uses the same reader as a live trace, so the returned word, analyses, attempts, and recorded tree share one interpretation. Try a Word translates the recorded refusal code into a sentence while retaining the code and the original evidence. A rule or required feature is named only when recorded; neighboring events do not establish which rule caused a failure. The window folds forward building events into Plain and keeps the full parser order in Expert.

## Related commands

- [Load a saved trace](cmd:trace%20--load) reads a diagnostic without a parser.
- [Prepare an AI Handoff](cmd:handoff) writes project context and retained parse evidence.
- [Measure a Selection](cmd:assess) records parse results for chosen words.
