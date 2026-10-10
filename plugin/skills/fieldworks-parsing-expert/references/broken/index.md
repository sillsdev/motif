# Correctness gotcha index

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

Read the one matching mechanism, then test its repair hypothesis. A topic's Reach label distinguishes
engine-only constructs from the FLEx projection. Engine-only pages do not authorize project XML edits.
The upstream snapshot has 15 correctness topics (the September inventory said 16); all 15 are distilled.
The additional loader-loss mechanisms are owned in [loader traps](../loader-traps.md).

| Topic | First discriminating question |
|---|---|
| [Stem-name category metadata is required](stemname-partsofspeech-required-not-silent.md) | Check the failed load before interpreting an empty parse set. |
| [Follow stem-label restrictions through confirmation](stem-name-affix-requirement-trace-misreading.md) | Follow that same candidate through synthesis and its stem-name rejection or acceptance. |
| [LexFamily blocking belongs to engine generation](lexfamily-blocking-generation-only.md) | Inspect generation, family membership and the complete irregular feature bundle before labeling a regular parse missing. |
| [MPR group alternatives are not conjunctions](mpr-group-matchtype-default-is-any.md) | Identify which group owns the label before expecting conjunction. |
| [MPR output can replace earlier group labels](mpr-group-output-default-overwrite.md) | Trace the input and output label set at each rule, not just the final set. |
| [An unclassified stem can miss class-restricted rules](inflection-class-no-default-silent-gating.md) | Check the stem MSA class and Default Inflection Class up its category ancestry, then the affix allomorph restriction. |
| [Realizational rules spell out a requested bundle](realizational-rule-cannot-add-features.md) | For an engine grammar, establish the requested feature bundle first and inspect realization/blocking. |
| [Realizational repetition needs a feature-based stop](realizational-rule-no-application-cap.md) | Inspect whether one application changes or satisfies the realization obligation and whether the next is blocked. |
| [Zero realization still applies a rule](null-affix-cannot-express-default.md) | Distinguish absence of a morphological category from a genuine zero realization. |
| [Independent optional slots license independent choices](single-template-independent-slots-illegal-combos.md) | List legal combinations first. |
| [Partial affixes relax ordering discipline](unclassified-affix-bypasses-template-ordering.md) | Check MSA kind, required category and slot assignment before rearranging a template. |
| [A prohibition list excludes a whole combination](coocurrence-rule-requires-all-not-any.md) | If the intent is pairwise exclusion, represent the separate pairs and check their adjacency scope. |
| [A circumfix should stay one morphological choice](circumfix-as-two-affixes-loses-atomicity.md) | Determine whether this is one discontinuous morpheme or genuinely two independent affixes. |
| [Repeated compounds can hit a one-application cap](compounding-max-application-count-default.md) | Inspect rule kind, count and loaded configuration. |
| [Compatibility does not establish explicit stem-label membership](stem-name-explicit-feature-requirement.md) | Inspect the actual bundle before asserting it belongs to a region. |
