# Explain a word's parse

`parse <word> --trace` asks the PanGloss that parses the word where each candidate path stopped. It reads the project's current Baseline, or a Proposal's Trial copy with `--at <proposal-id>`, and changes nothing. Motif runs no tracing parser of its own: every stop is something PanGloss recorded.

```powershell
motif parse mirusi --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --trace --json
```

## What you get

- **Paths.** Each retained terminal path is `rejected`, `accepted` or `unresolved`. A rejected path names its earliest recorded stop: the stage, PanGloss's reason code, a sentence, and the grammar object whose gate stopped it.
- **Canonical ids.** An object PanGloss bound to authored data carries the canonical id of each authored source, with the relation PanGloss recorded. An object it could not bind, such as a compiled stratum, is marked `unknown` with the reason; its compiled key stays so it can be told apart.
- **Unknown stays unknown.** A path with no recorded stop has attribution `unknown` and says why. An unfinished search never shows a dead end it did not prove.
- **Outputs.** Every parse output PanGloss returned, with its morphs by canonical id, so a rejected path can be read beside the readings that did parse.

## Bounds

`--max-paths` (1 to 64, default 16) caps both what PanGloss keeps and what Motif shows. The response says how many terminal paths PanGloss retained, how many Motif omitted, and what PanGloss's own capture limits dropped. Operands are scalar and capped.

## When it refuses

A request Motif cannot trace, a capture PanGloss refuses, a capture larger than Motif keeps and a trace Motif cannot read each refuse with their own code and a sentence. Revision digests for `--at` and generation are not available yet.

## Related commands

- [Trace a word](cmd:trace) returns the full diagnostic tree for one word.
- [Explain a generation](cmd:generate) names what generation tracing still needs.
