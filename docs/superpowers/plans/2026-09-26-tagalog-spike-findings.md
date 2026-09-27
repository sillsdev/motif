# synthetic-philippine feasibility findings

Motif parses the authored infixes through a FieldWorks Text, while the tested CV-copy form is stored by LibLCM but rejected by PanGloss v0.5.0. An explicitly written `su-` prefix parses with the same stem and gives a simpler lesson when productive copying is unavailable.

Label the eventual sample **SYNTHETIC example modelled loosely on Tagalog**. It is not real Tagalog or a description of Tagalog; the forms here are parser fixtures, and this note makes no claim about their real-world use or glosses.

## How the probe runs

`TagalogFeasibilityTests` creates a blank project with `NewLangProjFixture`, authors phonemes, natural classes, entries, approved analyses and a Text through LibLCM, saves and closes the cache, then calls Motif's `AssessCommand.Assess` with the Text's GUID. This follows the same command path as `motif assess <project> --texts <id>` and lets Motif invoke the real PanGloss batch parser.

The parser executable is `C:\Users\johnm\Documents\repos\motif\.tmp\pangloss\v0.5.0\pangloss.exe`, supplied through `MOTIF_PANGLOSS_EXE`.

## Infixes: work

The project declares one phoneme per character in `sulatmbin`, plus the `+` boundary marker. Its `C` natural class contains `s, l, t, m, b, n`; `V` contains `a, u, i`. Two `MoAffixAllomorph` entries use the FieldWorks infix morph type, with forms `um` and `in`. Each allomorph's `PositionRS` refers to a `PhEnvironment` whose `StringRepresentation` is `/ # [C] _`. The stems are `sulat` and `basa`.

The Text contains `sumulat`, `sinulat` and `bumasa`, each with an approved LibLCM analysis. Motif's Assessment returned one matching reading for each word (`covered`, 1/1). The parser's morphology references the expected allomorph and stem, in this order:

- `sumulat`: `um` + `sulat`
- `sinulat`: `in` + `sulat`
- `bumasa`: `um` + `basa`

This shows that a FieldWorks infix entry with a position environment after an initial consonant can be authored through LibLCM and parsed by PanGloss v0.5.0 through Motif's Text assessment path.

## CV partial reduplication: not imported

The project uses the same `s, u, l, a, t` phonemes, with `C = {s, l, t, m}` and `V = {u, a}`. The root entry is `sulat`. The reduplicative entry uses the FieldWorks prefix morph type and a `MoAffixAllomorph.Form` of `[C^1][V^1]-`; the approved Text analysis expects that affix followed by the `sulat` stem for `susulat`.

Motif's Assessment returned `susulat` with `no-analysis` and no parser readings. PanGloss v0.5.0 reported:

```text
warning: Allomorph '[C^1][V^1]' has a reduplication pattern that cannot be loaded as an affix rule.
warning: Allomorph '[C^1][V^1]' has a reduplication pattern that cannot be checked against the phoneme inventory.
```

The LibLCM project accepts and saves this allomorph shape, but this PanGloss importer does not turn it into a usable affix rule. In the read-only `pg-fwdata` source, allomorph forms and position references are extracted as separate fields; the downstream affix compiler emits the unsupported-pattern warnings above. The real-parser test is skipped with that limitation stated, and retains the expected-morph assertion in its body.

## Nearest working lesson: explicit prefix

An entry with the ordinary prefix morph type and literal form `su-`, followed by the `sulat` stem, parses `susulat`. Motif reports `covered`, 1/1, and the returned morphology references the explicit prefix entry followed by the root. This can teach how an affix entry combines with a stem, while making clear that `su-` spells out the copied material and does not generalize to other stems.

## Scope

The test establishes LibLCM authoring and PanGloss parsing through Motif's command path. It does not establish that a FieldWorks desktop installation restores or presents the synthetic project, or that the example strings describe authentic Tagalog morphology.
