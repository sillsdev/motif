# Why a word lost a Reading

`proposal why-lost <word> --proposal <id>` compares one word's parse at the Baseline with its parse on a Proposal's latest Trial copy, and says which Readings the Proposal lost. For each lost Reading it reports the stop PanGloss recorded and the Proposal operation that names the form PanGloss says blocked it. It changes nothing, and Motif runs no parser of its own: every cause is something PanGloss recorded. Both traces run under the work limit that Trial froze, and a note says which. A trace stops a word at the parser work limit, so a word that once traced to completion can now say "work limit reached" (used 100% of the work limit). To trace it further, raise the limit with `[parser] work-limit` in the project's `motif.toml` (see [Show the project configuration](cmd:config%20show)). The MCP tool `motif_why_lost` takes the same fields.

```powershell
motif proposal why-lost mirusi --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --proposal <id>
```

## What you get

- **Lost, gained and kept.** Both traces run under one parse lease, so they describe one project state. Readings are compared by the canonical ids of their morphs: entry, analysis and form.
- **A recorded stop.** When PanGloss recorded an `allomorph_blocked` stop on some Trial path that names a form the lost Reading uses, the answer names the blocked form and the earlier form that blocked it, with the form's written text where the Trial copy holds it.
- **Overlap, not cause.** The earlier form's id is joined to the Proposal's operations. An operation is listed when it creates the form, edits it, places a form beside it, adds a member to it, or names an object the form belongs to. That is an overlap of ids, not a recorded cause. If none does, the answer says so. The Baseline's describe is reported but only the Trial's decides whether a stop could be read.
- **Not recorded stays not recorded.** Where PanGloss recorded no stop for a lost Reading, the answer says `not-recorded` and names no operation. Motif never infers a cause. A PanGloss whose `--describe` does not list an `allomorph_blocked` stop gets the same answer, with that reason.
- **Which copy.** The answer names the Trial job whose copy it read. A later Trial of the same Proposal changes the copy.

## Bounds

`--max-paths` (1 to 64, default 64) caps the terminal paths read on each side. When paths or events were left out, a not-recorded answer says a stop may be among what was not read.

## When it refuses

It refuses a request it cannot compare, a Proposal with no Trial or whose Trial details were cleared, a project with no Baseline, and two traces whose grammar hashes do not compare, each with its own code and a sentence. Every refusal of [Explain a word's parse](cmd:parse) applies to either side.

## Related commands

- [Explain a word's parse](cmd:parse) shows where each candidate path of a word stopped on one side.
- [Why a word did not parse](cmd:why-not) asks PanGloss about morphs you expected.
- [Read a Trial report](cmd:proposal%20trial) lists the lost words this command explains.
