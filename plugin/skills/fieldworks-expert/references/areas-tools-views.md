# Areas, tools and views

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.

## Find the task first

| Area | Shipped tools / task | Objects behind the view |
|---|---|---|
| Lexicon | Lexicon Edit, Browse, Bulk Edit Entries, dictionary preview; forms, senses and entry relationships | `LexEntry`, `LexSense`, `MoForm`, MSA subclasses, `LexEntryRef` |
| Texts & Words | Interlinear Texts, Word Analyses, Word List Concordance, Bulk Edit Wordforms; texts and word readings | `Text` / `StText`, `StTxtPara`, `WfiWordform`, `WfiAnalysis`, `WfiMorphBundle`, `WfiGloss` |
| Grammar | Category Edit / Categories Browse, Features, Exception “Features”, Phonemes, Natural Classes, Environments, Phonological Rules, Compound Rules, Ad hoc Rules | `PartOfSpeech`, `Fs*`, `Ph*`, `Mo*` |
| Notebook | Record Edit, Browse, Document; observations and hypotheses | `RnResearchNbk`, `RnGenericRec` |
| Lists | Controlled vocabularies, semantic domains, people, locations, reversal categories | `CmPossibilityList`, `CmPossibility` subclasses |

The navigation pane chooses an area then a tool. A tool can contain several panes or tabs. A
Lexicon Edit entry pane and its entry-list grid can display the same property, while one is editable
and the other only navigates. Browse and Bulk Edit need separate column/target checks: seeing a
column is not evidence that its bulk-edit operation exists.

## Trace a label to a field

1. Identify area, tool, selected object type and exact label. “Inflection class” on a stem MSA differs
   from the restriction collection on an affix allomorph.
2. In the pinned configuration, find the tool's layout/column. Lexicon Browse's columns use
   `generate="childPartsForParentLayouts"`: columns are generated from reusable Parts/Layouts.
3. Follow its part reference into `Configuration/Parts/*.xml` or `*.fwlayout`. A child traversal can
   make the field belong to a sense or MSA rather than the selected entry.
4. Resolve the class and member in the pinned model. Report any virtual/computed display separately
   from stored data. Follow the writing-system selector as well as the field name.

This sharing explains why one stored property can appear in several places. It does not mean
that all views expose every field, that every repeated label means the same object, or that editing
a displayed value always creates the same kind of child object.

Primary source: [released Lexicon Browse configuration](https://raw.githubusercontent.com/sillsdev/FieldWorks/96da794961d20d82d53719824f3049aa19572533/DistFiles/Language%20Explorer/Configuration/Lexicon/Browse/toolConfiguration.xml)
and [Grammar area configuration](https://raw.githubusercontent.com/sillsdev/FieldWorks/96da794961d20d82d53719824f3049aa19572533/DistFiles/Language%20Explorer/Configuration/Grammar/areaConfiguration.xml).
The same pinned root contains `Lexicon/Edit/toolConfiguration.xml`, `Words/areaConfiguration.xml`,
`Notebook/areaConfiguration.xml`, `Lists/areaConfiguration.xml`, and `Parts/`. F07 records provenance
in Motif's reference library; [local source register](sources.md) carries it for standalone use.
