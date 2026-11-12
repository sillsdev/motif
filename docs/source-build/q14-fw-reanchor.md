# Q14: Does FieldWorks keep analyses in step when baseline words are edited or re-imported?

## Answer
Yes, for edits made through the LibLCM text-content setter. LibLCM's AnalysisAdjuster re-aligns existing word analyses when a paragraph's text changes, and it does so for any StTxtPara, which includes Paratext scripture paragraphs. It is a best-effort, single-edit diff. I did not verify that the Paratext importer ever hits the adjuster on a scripture paragraph with analyses present. The importer writes Contents directly, so the adjuster should run, but whether ParseIsCurrent is true at that point is unverified. Source 3 (LT issues) produced nothing.

## Evidence
**Source 1 (LibLCM).**
- `/mnt/h/repos/liblcm/src/SIL.LCModel/DomainImpl/StTxtPara.cs:542,560`: `ContentsSideEffects` calls `OnContentsChanged` with `diffInfo.FAdjustAnalyses`, then `AnalysisAdjuster.AdjustAnalysis(this, originalValue, diffInfo)`.
- `/mnt/h/repos/liblcm/src/SIL.LCModel.Core/Text/TsStringUtils.cs:1777`: `ChangesRequireAnalysisAdjustment`. Writing-system-only changes are excluded (LT-20344 comment). Other style-only changes are also excluded.
- `/mnt/h/repos/liblcm/src/SIL.LCModel/DomainImpl/AnalysisAdjuster.cs:26-61` (rules), `:378` (`if (m_para.ParseIsCurrent && m_newBeginOffsets.Length > 0)`), `:1321` (`if (startWord < ichFirstDiff + CchInsert) break;`, so a word overlapping the edit loses its annotation), `:521` `UpdateAffectedReferences`, `:730` `DeleteReferences`.
- `/mnt/h/repos/liblcm/src/SIL.LCModel/DomainServices/ITextUtils.cs:246-251` (`ParseParagraph` returns early if `ParseIsCurrent`), `:311-331` (collect pre-existing analyses, reparse, set flag), `:915-960` (`TryReuseAnalysis`: match by lowercased wordform, nearest index, reject if index shift > 100).
- `/mnt/h/repos/liblcm/src/SIL.LCModel/DomainServices/DataStoreInitializationServices.cs:447`: load sets `ParseIsCurrent = false`.

Algorithm: after a Contents change, the adjuster takes one contiguous diff (first differing offset, inserted count, deleted count). Words wholly before the diff keep their analyses. Words wholly after it are shifted by the net length change. Words overlapping the diff lose their analysis (and any approval or reference tied to them). If the paragraph is not marked ParseIsCurrent, the adjuster does nothing, and a later reparse reuses analyses by wordform text and nearby index.

**Source 2 (FieldWorks import).**
- `/mnt/h/repos/FieldWorks/Src/ParatextImport/BookMerger.cs:4483` (`titleParaCur.Contents = titleParaRev.Contents`) and `:6274` (`prevPara.Contents = bldr.GetString()`): the importer sets Contents through the same setter, so the adjuster is reached.
- No `ParseIsCurrent` or `AdjustAnalysis` use in `Src/ParatextImport` (non-test) or `Src/Common/ScriptureUtils`. So the importer does not clear the flag itself, and I found no code that reparses after merge.
- `/mnt/h/repos/FieldWorks/Src/LexText/Interlinear/RawTextPane.cs:369-374` (`SetParaToReparse`) clears the flag on user edits in the interlinear raw-text pane. That is the path the owner sees in the interlinear UI.
- `/mnt/h/repos/FieldWorks/Src/ParatextImport/UndoImportManager.cs:171` (LT-15945) and `ParatextSfmImporter.cs:578` (LT-16647): comments only; they do not describe analysis handling.
- Tests: `/mnt/h/repos/liblcm/tests/SIL.LCModel.Tests/DomainImpl/AnalysisAdjusterTests.cs` (e.g. `FullIdenticalButNewWsReplacementLeavesTranslation`, tied to LT-12403). No Paratext re-import test asserts that analyses survive (only `SegmentedBtMergeTests.cs:206-288` touches AnalysesRS, for BT segmentation).

**Source 3 (LT issues / help).** Unverified. Three web searches (FLEx list, Paratext/FLEx integration, JIRA-style phrasing) returned no LT issue about analyses lost on Paratext re-import, and no FieldWorks help page on it. I did not reach the SIL JIRA (LT project) directly.

**Source 4 (PT9 re-anchoring).** Not established.
- `/mnt/h/repos/interlinearizer-extension/src/parsers/pt9/pt9-xml.md:18` lists `Interlinear_{language}_{book}.xml` as per-verse cluster selections. Its examples (lines 109-114, 138) key selections by `Lexeme Id` and `GlossId`, not by character offset. The doc has no re-anchoring rule (grep for anchor/edit/re-found nothing relevant).
- Web search returned Paratext help/blog pages that say glosses are stored per word in `Lexicon.xml`. None describes how glosses follow a scripture edit. Unverified.

## Open questions
1. Does a Paratext-imported scripture paragraph carry ParseIsCurrent = true after a merge? Test by importing, glossing, then re-importing an edited verse.
2. Does the adjuster handle a multi-edit diff (a verse with several changes)? It takes one contiguous diff, so probably not; verify with a test.
3. Who owns LT issues on this? Needs a direct JIRA search (LT project) for "Paratext re-import analyses" by a person with access.
4. PT9 behavior: needs the PT9 Interlinearizer source or a Paratext developer's answer.
