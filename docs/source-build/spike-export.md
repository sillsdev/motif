READY 3010736

# Export spike: .fwdata -> Harmony log -> .fwdata (branch `export`, ~/work/sb-spike-format)

Claim: the export direction works. Both projects round-trip with every LCM GUID kept for the grammar and lexical classes, 0 build errors. Indonesian parses are identical; Cebuano is 96.8% identical.

## Method
- Lexical part: FW Lite ProjectImporter (snapshot + import) into LcmCrdt.
- Grammar part: a Motif pass. A generic LcmObject carrier (class, owner, owner field, order, props JSON, refs JSON, GUID kept) written as Harmony changes. Extension rows for FW Lite-owned objects get deterministic UUID v5 ids.
- Build: new project, lexical part via the bridge, then a grammar apply (create by GUID through the LCM factories, attach, set fields and refs).
- Inventory: out/inventory-*.tsv. Run logs: out/indo, out/ceb. Parse comparison: out/cmp_parse.py, out/cause.py.
- Build.cs GetEntries 1000-default bug: not hit. My pipeline uses TakeProjectSnapshot/GetAllEntries; Ceb LexEntry GUIDs are 24856 in both original and rebuilt. Errors are collected and reported (0).

## Indonesian (Motif-J5-Indonesian)
- Log: 945 export rows, small. 11387 GUIDs in the rebuilt file.
- GUID sets per class: equal for all grammar classes, and equal for the lexical classes.
- PanGloss parses: 70/70 identical (100%).
- Determinism (two exports, two builds): export rows are identical (945 rows, True). Rebuilt GUID sets differ only for template-minted objects: CmDomainQ 7938 (semantic domains minted by the blank project), 3 of 34 CmPossibilityList, LangProject, ReversalIndex. These are minted by FW's new-project template, not by us. Grammar classes are identical.

## Cebuano (ceb.fwdata, 24,856 entries)
- FW Lite import (snapshot + import): 2m35s. Export of 120,886 rows: 4.3 s reading LCM, 1m17s including the Harmony commit. Log: 1,700 commits, 409.6 MB sqlite.
- Build: lexical part 9 s; grammar part 7m02s (phase2 attach 71 s, phase3 fields 5m43 s). 0 errors.
- GUID sets: equal for every class except two. LexEtymology 418 of 418 missing (uncovered, FW Lite does not carry it, listed rather than faked). PartOfSpeech has 1 extra (template-minted). LexEntryRef 3627 of 3627 equal after run 2.
- Parse comparison on 2,000 words (1,999 parsed, 1 skipped, 0 timeouts):
  - Run 1 (variants dropped by FW Lite): 1834/2000 identical, 91.7%. Main cause: LexEntryRef (variants).
  - Run 2 (variant LexEntryRef exported through the Motif pass): 1936/2000 identical, 96.8%. 64 words differ.
  - Causes of the 64: 47 words with extra parses in the rebuilt file and 30 unclassified, with no uncovered class identified; the unclassified ones are things like nurang, pridung, danilug (rebuilt lost the parse). 1 word (munamuna) involves a MoAffixProcess allomorph. I have not root-caused the unclassified differences.
- Determinism for Cebuano: second export is running (it was still in progress when I wrote this). Result lands in out/ceb/run.log as the "determinism:" lines. Expect the same template-minted-only differences as Indonesian; not yet confirmed.

## Uncovered / risks
- LexEtymology not carried (418 in ceb); the build drops it. The CmDomainQ/template-minted GUIDs are not controlled by us, so full-project GUID determinism needs the new-project template to be seeded, or a Build that overwrites the template singletons (we already delete and recreate them).
- Grammar apply is slow (about 7 minutes for 120k rows) because it goes object by object through LCM with reflection. Faster paths exist (bulk SetObjProp batching) but were not tried.
- Remaining 64 differing Cebuano words need a per-word diff of the HC grammar (rebuilt vs original) to be attributed to a class. I would start with the 30 unclassified ones and the 47 extra-parse words.
- Out files include large generated outputs in out/; they are committed except fwdata and sqlite.

## Rerun with fixes
Earlier open item first: the Cebuano determinism run of the pre-fix export finished. Two exports give identical rows (120,886, True); two builds give identical GUID sets except template-minted objects (CmDomainQ 7938, 3 of 34 CmPossibilityList, LangProject, ReversalIndex; 7943 per side), same as Indonesian.

Fixes applied (commit below):
- (A) `LexEntry.DoNotUseForParsing`: one extension row per flagged entry (LcmGeneric.cs); LcmApply already applies Boolean props.
- (B) POS hierarchy: the POS extension row carries `__ParentPos` (owner POS guid; absent = top level). LcmApply phase 3b re-owns with `parent.SubPossibilitiesOS.Add(child)` (or moves to the list root), then deletes template POS not in the log (Pro-form deleted in Cebuano; Adverb, Verb, Pronoun, Pro-form, Noun in Indonesian, all template copies with other GUIDs). Rows rose 120,886 to 121,044 (+158 flags).
- Speed fix ported from sb-spike-trial 7f44158: POS stripped from the snapshot senses before the bridge call (`LcmApply.SensePos`), then attached to the log's MSA in RepointSense.

Results (same words, 2,000 listed, 1,999 parsed, 0 timeouts):
| | identical parse sets | build errors | GUID gaps | determinism |
|---|---|---|---|---|
| Cebuano | 2000/2000 (100.0%), 0 differ (was 1936, 96.8%) | 0 | only LexEtymology 418 (uncovered, not carried) | export rows equal (121,044); GUID sets equal except the same template-minted 7943 |
| Indonesian | 70/70 (100.0%) | 0 | none | export rows equal (945); same template-minted-only differences |

Timing, Cebuano (machine shared with other jobs, so noisy): lexical bridge part 33 s (first build) and 13 s (second); grammar part 8m33 on the first build (phase2 attach 3m38, phase3 fields 4m30), 5m45 on the second. The strip-POS fix did not give a clear speedup here: phase3 went from 5m43 to 4m30 versus the earlier run, but the load differs, so I would not claim the 142 s to 16 s effect for this pipeline. A run with fixes A and B but without the speed fix was started and stopped by me (11:01 to 11:14) once the speed fix arrived; it had not finished, so nothing is reported from it.

Nothing else was changed. Not fixed (out of scope): subsense flattening, LexEtymology, GrammarNote/Exemplar.
