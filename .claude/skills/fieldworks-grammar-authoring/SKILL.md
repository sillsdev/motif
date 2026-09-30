---
name: fieldworks-grammar-authoring
description: >-
  Build or change a FieldWorks grammar through LibLCM so the parser can use it: synthetic sample
  projects, test fixtures, phonemes, natural classes, environments, affixes and allomorphs, templates and
  slots, inflection features and noun classes, phonological rules, infixes and reduplication. Use before
  writing any LibLCM grammar code, and when a built grammar does not parse the way it should.
---

# FieldWorks grammar authoring

A grammar that saves cleanly through LibLCM can still reach the parser broken: FieldWorks' parser loader
drops forms, empty templates and bad environments without an error, and PanGloss reads the project through
its own importer with its own gaps. This skill is how to author a grammar whose every construct is proven
to arrive.

**This is the direct-LibLCM path, and it is temporary.** Motif's own operations will take over grammar
authoring. Before writing LibLCM code for a construct, check whether a Motif operation already authors it
(`docs/cli-api.md`, the Developer verbs); where one does, author through Motif instead, and write the
construct's spec so it maps one-to-one onto an operation later. The recipes are ordered by construct for
exactly that migration.

## Steps

1. **Model the language first, in words.** For each construct you will author, write one line of what it
   means linguistically ("the plural is -ler after front vowels, -lar after back"). Done when every
   construct in the sample's `teaches` list has its line.
2. **Look up each construct** in [references/recipes.md](references/recipes.md): its owner, factory, the
   order it must be created in, and its *parser reach*, meaning whether PanGloss v0.5.x actually consumes
   it. A construct marked **not reached** (metathesis, bracket-pattern reduplication, custom strata) is
   modelled for FieldWorks only; state that in the sample and do not rely on it parsing.
3. **Author in dependency order** inside one unit of work: writing system → phonemes and codes →
   feature systems → natural classes → environments → parts of speech and inflection classes → entries,
   allomorphs, MSAs, senses → slots and templates → phonological rules → texts. The caller owns the cache
   and its persistence. In this repo the synthetic samples go through `tools/SampleProjects`
   (`samples/sample.schema.json` is the spec shape); extend the builder rather than writing a second one.
4. **Prove arrival, not saving.** Build with `--check`: it runs PanGloss grammar-health through Motif's
   `GrammarCheckQuery` and parses the sample's word list. Done when all of these hold:
   - zero error-level findings on the fixed project, and every warning kept has a one-line reason;
   - every construct from step 1 shows up in at least one parse (a slot's affix in an analysis, an
     environment choosing the right allomorph); a construct nothing exercises is unproven;
   - the importer's load warnings are read, not skimmed: a malformed affix environment can load as *no
     restriction*, so a word that parses is not proof the condition held.
5. **Change one thing at a time** when a word fails or is slow, re-running `--check` after each change,
   and record measured work (not wall-clock) for any speed claim. Empty allomorphs are not automatically
   costly: in the synthetic Bantu sample 160 of them added no measurable work, while a duplicated real
   affix raised it 19.5×. Measure the grammar in front of you.

## Rules

- **Synthetic data only.** Every sample says SYNTHETIC EXAMPLE and names the language it is modelled
  loosely on; no real project data enters this repository in any form.
- **Two loaders, two truths.** FieldWorks' C# `HCLoader` and PanGloss's importer differ. Cite which one a
  claim is about, with its version; a construct HCLoader accepts is not thereby parseable by PanGloss.
- **Object references, never names.** Features, values, classes and slots are linked by `…RA`/`…RC`
  references; a matching name or abbreviation links nothing.
- **Order is meaning** where LibLCM sequences are ordered: allomorph order is disjunctive (earlier blocks
  later), template slot order is morpheme order, phonological rules run by `OrderNumber`.

## Sources

The evidence behind every recipe, with file and line citations, is in `docs/research/grammar-authoring/`:
`liblcm-recipes.md` (LibLCM, FieldWorks `HCLoader`, PanGloss source), `flexicon-harvest-grammar.md`
(Flexicon, FLExTools, FlexToolsMCP), and `own-guidance-review.md` (our guidance and Andy Black's).
Linguists get the same content in plain words on the Learn page *Modelling a grammar the parser can use*
(`src/SIL.Motif.Help/Content/en/guide/learn/modelling-a-grammar-the-parser-can-use.md`).
