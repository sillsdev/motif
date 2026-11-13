# Why 64 Cebuano words differ after the round trip (96.8% -> 100% by two fixes)

Two causes explain all 64 words, and a throwaway experiment confirms it. The "47 extra / 30 unclassified" split in the spike report was a count of analyses, not words: 47 extra analyses fall in 34 words, and the 30 "unclassified" are one cause. MoAffixProcess (munamuna) is a red herring.

## Method
- `pangloss import` (spike tree binary) gave an HC snapshot JSON for the original and for out/ceb/rebuilt.fwdata (copies in ~/work/sb-spike-diag; nothing was written to out/ceb/). Diffed entries and POS by GUID.
- The parse TSVs in out/parse-ceb match: rebuilt2.* is the current out/ceb/rebuilt.fwdata (re-parsed in diag: 1936/2000, 64 differ, same).

## Findings
Snapshot diff: original has 24,698 entries, rebuilt 24,856. The 158 missing from the original are exactly the entries with `DoNotUseForParsing=True` (158 in the original .fwdata, 0 in the rebuilt). Phonology (51 phonemes, 44 natural classes, 22 environments, 34 rules), exception features, inflection types, allomorphs, MSAs, slots and templates were identical. The only other difference is the POS tree.

| cause | words | lost where | fix size |
|---|---|---|---|
| A. `LexEntry.DoNotUseForParsing` not carried: 158 entries become parseable, so the rebuilt file gets extra parses (34 words, 47 analyses) | 34 (all "extra") | Export gap. FW Lite's MiniLcm Entry has no such field, and the Motif pass walks only LexemeForm, AlternateForms, variant EntryRefs and MSAs of each entry (LcmGeneric.cs:130-140), never the entry's own scalars | ~10 lines: one extension row per entry where the flag is true, in the same pattern as LcmGeneric.cs:134 (`Row(e, null,"","",0,true, only:["DoNotUseForParsing"])`). Build needs no change: LcmApply phase 3 (LcmApply.cs:138,194) already applies Boolean props of extension rows |
| B. POS hierarchy flattened: "Proper Noun" (01a1aeca...) was a child of "Noun" and is now a top-level POS; the rebuilt file loses its parse because Proper Noun stems no longer satisfy affixes and stems that require Noun (e.g. ng/g ligatures, -an, process affix) | 30 (all "lost", each a Proper Noun stem plus ng, g, an or a process; includes munamuna and Bakalan) | Export gap plus a small Build gap. POS rows are exported only with `only: PosRefs` (LcmGeneric.cs:123), so the parent and SubPossibilities never leave LCM. The POS list is built by `fw.CreatePartOfSpeech` (Build.cs:29, Export3Pipeline.cs:76), and MiniLcm PartOfSpeech is flat | ~25 lines: export `ParentPos` (or the owner guid as OwnerId/OwnerField="SubPossibilities") on the POS extension row, and in LcmApply re-own the child (`parent.SubPossibilitiesOS.Add(child)`, which moves it in LCM). Optionally widen `only` to Abbreviation, Description, CatalogSourceId (13 POS lost them; no parse effect) |

## Confirming experiment (throwaway, text edits of the rebuilt .fwdata in ~/work/sb-spike-diag, spike code untouched)
Parsed the same 2,000 words with pangloss batch (original = 100% reference):

| variant | identical | differing |
|---|---|---|
| rebuilt as is | 1936 (96.8%) | 64 |
| A-pos: Proper Noun re-owned under Noun | 1966 (98.3%) | 34 (all DNUFP words) |
| B-dnufp: the 158 flags set to True | 1970 (98.5%) | 30 (all Proper Noun words) |
| C-both | **2000 (100.0%)** | 0 |

## Other differences found (no parse effect on these 2,000 words)
- The blank project's template adds a POS "Pro-form" with Pronoun moved under it (Pronoun is top-level in the original). Same family as B: fixed by the same reparenting plus deleting template POS not in the log (ClearTemplate).
- Subsenses are flattened (LexSense Senses lost for 879 entries, 2,834 subsenses; owner differs for 4,211). Sense-to-MSA links are kept, so parses are unaffected, but this is a latent gap for Build fidelity.
- 4 variant LexEntryRefs have a ComponentLexemes value in the rebuilt file where the original has none (the bridge fills it). Harmless for parsing.
- LexSense GrammarNote/Exemplar/ImportResidue, LexEtymology (418) are not carried; no parse effect.

## Risks and confirmation
- The remaining risk is other words using DNUFP or POS paths, but the two fixes are generic and cover all 158 flagged entries and the single hierarchy. Confirm by rerunning the real Build with the two fixes and comparing all 2,000 words (and a larger word list); expect 100%.
- Total estimated fix: about 35 lines plus one 7-minute rebuild. Did not touch out/ceb or run the pipeline; the Export3Run determinism process was still running.
