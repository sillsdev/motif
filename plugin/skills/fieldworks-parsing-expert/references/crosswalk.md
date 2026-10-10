# Terminology crosswalk

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.

**Packaging:** canonical authored source is `plugin/references/crosswalk.md`; the package sync step
copies it into each expert/consultant folder. Read this local copy; no parent-folder lookup is needed.

Use this conceptual bridge to explain a fact after checking it with the appropriate expert. A row
is not a promise that a particular product version imports the construct or exposes a control.

| Linguist term | FLEx label / explanation | LCModel vocabulary | HermitCrab construct |
|---|---|---|---|
| Lexeme / entry | Lexical Entry; a lexical item, with forms and senses | `LexEntry` | Lexical entry and morpheme identity |
| Stem / root form | Lexeme Form or Alternate Form | `MoStemAllomorph` | Root allomorph |
| Affix / affix variant | Affix form; Allomorph | `MoAffixAllomorph`, `MoAffixProcess` | Allomorph of an affix process rule |
| Morphosyntactic analysis (MSA) | Grammatical Info.; a form’s category and grammatical properties | `MoMorphSynAnalysis` subclasses | Rule/entry syntactic features and restrictions |
| Stem category and properties | Stem grammatical information | `MoStemMsa` | Lexical syntactic feature structure |
| Inflection | Inflectional Affix; grammatical contrast within a paradigm | `MoInflAffMsa` | Inflectional rule used in a template |
| Derivation | Derivational Affix; input/output grammatical properties | `MoDerivAffMsa` | Derivational affix process rule |
| Affix position class | Slot | `MoInflAffixSlot` | Affix-template slot |
| Ordered pattern of inflection | Affix Template | `MoInflAffixTemplate` | Affix template |
| Lexically conditioned paradigm class | Inflection Class | `MoInflClass` | MPR features (rule restriction labels) |
| Grammatical contrast / agreement feature | Inflection Feature and value | `FsClosedFeature`, `FsSymFeatVal`, `FsFeatStruc` | Syntactic feature system / unification |
| Phonological conditioning | Environment; neighboring sounds / boundary | `PhEnvironment` and allomorph references | Allomorph environment |
| Set of sounds sharing behavior | Natural Class | `PhNCSegments`, `PhNCFeatures` | Natural class |
| Sound alternation rule | Phonological Rule | `PhRegularRule`, `PhSegRuleRHS` | Rewrite rule and subrule |
| Sense / gloss | Sense; Gloss | `LexSense` | Display information, distinct from rule licensing |
| Proposed word reading | Analysis Candidate (Opinion Unknown) | `WfiAnalysis` and related judgments | Parser analysis, not human approval |

MPR features are labels used for rule restrictions in HermitCrab; lexical classes use these, while
semantic agreement uses syntactic features. The model and engine need separate lookup: lexical class
membership cannot substitute for number or gender agreement just because both use features.

Parser agreement about morphology does not claim agreement about a sense or word-level category.
For Motif explanations use Proposal, Draft, Dry Run, Assessment and Trial in their distinct roles:
a Dry Run records effects on a throwaway copy; an Assessment records measurements; a Trial combines
both for one attempt at a Proposal. Finalize hands a Proposal to a person; Apply belongs to that person.
