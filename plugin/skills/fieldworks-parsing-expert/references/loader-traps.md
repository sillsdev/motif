# Stored objects that fail to reach HermitCrab

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

Use this before attributing a missing parse to an engine rule. Saving an object is different from
loading it into a grammar. The following checks distill the pinned HCLoader and internal authoring
recipes; they are not direct-storage editing instructions.

- The first phoneme set supplies the character table. A phoneme needs a usable vernacular code;
  its display name is insufficient. Watch missing codes, placeholder `***`, duplicate spellings and
  mismatched default vernacular writing system. Extra sets do not give per-stratum inventories.
- A natural class containing an unloadable phoneme can fail as a whole. A segment-list class is
  not a feature-based class; their model subclasses differ. An empty/unloaded class cannot serve
  as a reliable environment restriction. Check unique abbreviations used by `[Class]` strings.
- Allomorph environments are read from `PhEnvironment.StringRepresentation`, not its structured
  context graph. Validate `/ left _ right` strings and the exact referenced graphemes/classes.
  Malformed affix environments can yield an unconstrained alternative; invalid stem environments
  can be omitted. Do not infer a restrictive effect from the presence of a malformed string.
- Entry forms need the appropriate morph type and a linked MSA. Senses refer to entry-owned MSAs.
  A zero-survivor entry/rule/template can disappear from the loaded grammar. Descriptions do not
  control rule behavior. An inflectional MSA without a slot can become a partial rule.
- Stem class defaults follow category ancestry when no explicit class is set. Class restrictions on
  an affix are different from grammatical feature structures. Check both and their ownership.
- Template slot ordering is suffix slots followed by reversed prefix slots: closest to stem first
  on each side. Legacy/general/proclitic/enclitic slot collections are not interchangeable with
  those two consumed sequences. Phonological rule sequence order affects feeding.
- A stem label with no nonempty feature regions does not supply a usable restriction. Broken
  references in MPR groups/co-occurrence data can fail the whole load rather than simply remove
  one analysis. Do not “repair” a dangling identity by guessing the intended target.
- The release loader has a 24-name array for alpha variables. A rule with a 25th distinct
  feature constraint can fail the load; a variable is not a freely reusable comment label.
- Many importer effects are silent. Collect actual load errors, surviving entries/rules and a
  diagnostic trace; absence of an error alone is not proof of successful projection.

Primary F07: [HCLoader](https://raw.githubusercontent.com/sillsdev/FieldWorks/96da794961d20d82d53719824f3049aa19572533/Src/LexText/ParserCore/HCLoader.cs), particularly
`LoadCharacterDefinitionTable`, `LoadNaturalClass`, `LoadAffixProcessAllomorphs`, stem environments
and template loading. F05 is the model pin. The internal recipes were reviewed against this release;
old PanGloss 0.5.x capability claims are intentionally not carried forward.
