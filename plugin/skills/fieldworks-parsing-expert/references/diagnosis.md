# Wrong, missing, ambiguous or slow

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

## Preserve the question

Record the exact surface and expected **complete** analysis, including which lexical identity and
affix chain is intended, the selected engine, current grammar evidence and whether the run completed.
A homophone with a different gloss is not proof that the required path exists. Use an invented small
contrast if the available material is private; this package contains no real project data.

| Symptom | First checks | Read next | Discriminating evidence |
|---|---|---|---|
| No analysis / intended one missing | Correct writing system, loaded phonemes/codes and forms; active rule/template; complete MSA; mandatory slot; explicit/default class | [loader traps](loader-traps.md), [class gating](broken/inflection-class-no-default-silent-gating.md), [null affix](broken/null-affix-cannot-express-default.md) | One stem alone, then one affix, then the failing chain; inspect earliest absent named path |
| Wrong analysis / wrong allomorph | Trace both unapplication and resynthesis; category/class requirements; allomorph order; malformed environment | [stem-name trace](broken/stem-name-affix-requirement-trace-misreading.md), [exception ordering](workflow/ordered-rule-exception-blocks.md) | One allowed environment and one near-negative differing only at the restriction |
| Too many readings | Distinct identities vs duplicate paths; partial/unclassified affix; independent optional slots; conjunction vs pairwise exclusion | [partial affixes](broken/unclassified-affix-bypasses-template-ordering.md), [optional combinations](broken/single-template-independent-slots-illegal-combos.md), [co-occurrence](broken/coocurrence-rule-requires-all-not-any.md) | Enumerate permitted combinations before counting; retain legitimate ambiguity |
| Slow / stalls / crashes | Grammar load vs one-word search vs queue work; optionality, free morphology, self-feeding phonology, patterns | [performance index](speed/index.md), [measurement](workflow/measuring.md) | Same content, machine and named word; complete confirmed readings plus timings/caps |

## Trace boundaries

A rule being tried in reverse analysis does not mean it survives forward synthesis. Distinguish
“could unapply”, “found a lexical candidate”, “passed feature conditions”, “reproduced the surface”
and “returned a complete result”. Locate the first failed boundary before changing unrelated
rules. A missing trace field is not proof that the condition was skipped; some conditions are
checked only during confirmation. A load warning can explain loss before search starts.

For a proposed repair, require the intended positive, a near-negative, and preservation of all
required readings. Make one conceptual change at a time. If evidence is capped, classify it as
incomplete. If the grammar is correct but costly, use the performance corpus without changing its
language to make a stopwatch look better. Linguistic acceptance goes to the consultant.

The procedure distills F09 plus the internal grammar-authoring recipes and PanGloss diagnostic
research listed in [sources](sources.md); it is not a proven diagnosis of an unseen project.
