# Why a word did not parse

`why-not <word> --expect <morphs>` answers one question: why did this word not parse as the morphs you expected? You give the word and one or more candidates, each a morph sequence in surface order. For every candidate PanGloss says whether the word parses along it and, if not, the first constraint that fails. It reads the project's current Baseline, or a Proposal's Trial copy with `--at <proposal-id>`, and changes nothing. Motif reasons about no grammar itself: every answer is something PanGloss reported. The MCP tool `motif_why_not` takes the same fields, with `expect` as a list.

```text
motif why-not kibakek --project <fwdata> --expect kib,ak,ek --expect kib,ak|ik,ek --json
```

## Writing a candidate

- `,` separates positions, in surface order. `|` offers several choices at one position, and the answer says which one worked.
- A morph is its `grammar.xml` id. `id@allomorph-id` pins an allomorph.
- To name a morph by FieldWorks identity, write `entryGuid:msaGuid`; each side is a GUID or a Motif id. `id=entryGuid:msaGuid` gives the id and has PanGloss check the guids belong to it.
- Candidates by id and by guid can share one call. Pinning an allomorph on a guid-only choice works only when the guid names a single morpheme; otherwise PanGloss refuses it.
- Up to 16 candidates, 32 positions each and 16 choices per position. `--expect` repeats.

## What you get

- **Accepted, rejected or unknown, per candidate.** An accepted candidate lists the morphs of the derivation. A rejected one gives the failure `kind`, a sentence, the 0-based `position`, the morphs involved, and whether HermitCrab reported it or PanGloss inferred it. An unknown one gives the stop reason, such as `work_cap`, and what was reached.
- **The kinds that can be answered.** The output lists the failure kinds the installed PanGloss answers, read from its `--describe`. Any other failure comes back unknown, never guessed.
- **The grammar object and its FieldWorks target.** A rejection names the object a fix would touch: its type, its `grammar.xml` id and, when PanGloss supplies one, its FieldWorks guid. An environment failure also gives the allomorph, the environment's place in its list, its authored text such as `/[V]_` and the text around the allomorph. An allomorph's environments are any-of, so when every one failed the output names the allomorph and lists each environment with its text and guid. A co-occurrence prohibition lists every morph it names, with the entry, analysis and form guids. Guids appear when the grammar came from a FieldWorks project snapshot; the output says `no FieldWorks target yet` where PanGloss gave none, as for a hand-written grammar or a prohibition the parser cannot tell from an equal one.
- **Best.** The first accepted candidate, otherwise the rejected one that got furthest.

## Bounds

The output keeps at most 16 morphs and 8 feature clashes per answer and cuts each parser sentence at 400 characters; it counts what it left out. PanGloss spends at most 30 seconds on each concrete morph sequence before it answers unknown.

## When it refuses

It refuses, with its own code and a sentence, a request it cannot send, a PanGloss that does not declare the why-not capability in `--describe`, input PanGloss rejects (an unknown morph id, a guid that is not the morph's), a report Motif cannot use, and a run that did not finish. It uses the same machine parser queue, time limits and cancellation as the other parse questions.

## Related commands

- [Explain a word's parse](cmd:parse) shows where each candidate path of the word stopped, without naming a morph sequence you expected.
- [Trace a word](cmd:trace) returns the full diagnostic tree for one word.
