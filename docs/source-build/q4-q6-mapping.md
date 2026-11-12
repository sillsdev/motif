# Q4-Q6 mapping: Motif change representation, FW Lite/Harmony, GUIDs, import

M = ~/work/motif-source-build; FL = ~/work/research-src/languageforge-lexbox/backend/FwLite; H = ~/work/research-src/harmony/src. No builds run. Counts are from grep.

## A. What Motif has now

**Generator (VERIFIED).** `M/src/SIL.Motif.Generator` is a console tool: `emit` reads MasterLCModel.xml joined to `M/manifest/liblcm-inventory.tsv` (898 field rows, 899 lines; ADR 0014:~40) and writes checked-in files (not Roslyn, not build-time; Program.cs:11-45 remarks). `RunEmit` (Program.cs:~63-80) chains 7 catalog writers. Output is 236 `.g.cs` files: 192 operation files under `M/src/SIL.Motif.Runner/Operations/Generated{,2..6}` plus `GeneratedRetirement`, 73 snapshotter files under `Runner/Snapshotting/Generated*`, and 6 in `M/src/SIL.Motif.Model/Snapshot`. A generated file holds the kind-name constants, a `[ModuleInitializer]` that registers the kinds and handlers, and a handler. Example: `Operations/Generated/LexSenseGloss.g.cs` registers `lexical/lexSense/setGloss` and `clearGloss`.

**Operation vocabulary (VERIFIED by grep, approximate).** About 335 `OperationKindRegistry.Register(` calls: 281 in `.g.cs` files (155 files) and 54 in 43 hand-written files. 348 distinct `group/class/verb` string literals under `Runner/Operations`. So about 84% generated and 16% hand-written. The registry is a runtime `HashSet<string>` (Contract/Parsing/OperationKindRegistry.cs:23-39). `docs/minilcm-evaluation.md:169` says 332 kinds, and its "generator does not exist yet" is stale.

**Lowering (VERIFIED).** There is no single Lowering layer. Each handler lowers its kind straight onto LibLCM objects (`IOperationHandler.ApplyAndCaptureEffect(LcmCache, OperationEnvelope, ...)`, Runner/Operations/IOperationHandler.cs:~28). `ProposalApplier` (Runner/Apply/ProposalApplier.cs:208) opens one `UndoableUnitOfWorkHelper` and dispatches through `OperationHandlerRegistry`. The host saves later (ProposalApplier.cs:33-34, 241). CONTEXT.md:58 defines Lowering as "one Motif operation into concrete changes against a store".

**Construct-level abstraction (VERIFIED).**
- Intents: 91 `*Intent` records and 27 `*Composer.cs` in `Runner/Composers/` (120 files), e.g. `AuthorLexemeFormIntent` (AuthorLexemeFormIntent.cs:40), `AuthorFeatureStructureIntent`. They are hand-written. A composer builds the several operations an author would think of as one act.
- Intents sit on LCM classes (they take `CanonicalId` of ILexEntry, IPhEnvironment, ...), not on an abstract grammar model. The composer resolves LCM objects with `ReferenceFieldLowering.Resolve<ILexEntry>` (AuthorLexemeFormComposer.cs:72-83).
- `docs/hc-grammar-map.md` (121 lines) is prose: a table of LibLCM classes and fields that HCLoader reads. Its `manifest/hcloader-surface.tsv` twin has 235 rows. It is the requirement, not code.
- GDR project paths: only a design table of 19 path patterns (`docs/superpowers/specs/2026-11-03-gdr-markdown-design.md:489-508`, e.g. `/lexicon/<headword>` to LexEntry, `/grammar/phonological-rules/<name>`). Code only parses a path from a link (`Host/Gdr/GdrReader.cs:108-130`). It is a locator, with the GUID as identity (CONTEXT.md:125). It is not a connected construct graph.

Bottom line: nothing in Motif is a representation above LCM classes. Every layer (operation, Intent, GDR path) names LCM objects.

## B. FW Lite and Harmony

**MiniLcm models (VERIFIED).** Hand-authored C# classes in `FL/MiniLcm/Models/` (Entry.cs, Sense.cs, ExampleSentence.cs, ...): `public record Entry : IObjectWithId<Entry>` (Entry.cs:3), `Sense : IObjectWithId, IOrderable` (Sense.cs:7). The only attribute is `[MiniLcmInternal]`, which controls serialisation (MiniLcm/Attributes/MiniLcmInternalAttribute.cs). No source generator found (grep for `[Generator]` found nothing). Reflection use found is limited to LinqToDB JSON helpers (LcmCrdt/Json.cs) and TypeGen config (FwLiteShared/TypeGen/ReinforcedFwLiteTypingConfig.cs).

**Harmony object requirements (VERIFIED, H/SIL.Harmony.Core/IObjectBase.cs).** `Guid Id`, `DateTimeOffset? DeletedAt {get;set;}`, `Guid[] GetReferences()`, `void RemoveReference(Guid, CommitBase)`, `IObjectBase Copy()`, `string GetObjectTypeName()`, `object DbObject`. LcmCrdt wraps MiniLcm types with `CustomAdapter<IObjectWithId, MiniLcmCrdtAdapter>` (LcmCrdtKernel.cs:~228). Each object type is registered by hand with its EF mapping (:226-290).

**Hand-written change types (VERIFIED).** `ChangeTypeListBuilder` has 62 `.Add<>` entries (LcmCrdtKernel.cs:376-444). 23 are instantiations of 3 generics (`JsonPatchChange<T>`, `DeleteChange<T>`, `SetOrderChange<T>`), so 39 concrete non-generic change classes. `FL/LcmCrdt/Changes/` holds 41 files.

**Abstraction level vs LCM (VERIFIED).** Heavy collapse:
- Entry.LexemeForm is `LexemeFormOA.Form` (FwDataMiniLcmApi.cs:693, write :1116). Entry.MorphType is `LexemeFormOA.MorphTypeRA` (LcmHelpers.cs:32), and the allomorph class (stem or affix) is chosen from the kind (LcmHelpers.cs:236-261).
- MSAs do not exist. `Sense.PartOfSpeech` is read and written through whichever MSA subclass the sense points to (MorphoSyntaxExtensions.cs:7-31). If there is no MSA, `SandboxGenericMSA.Create(...)` makes one (FwDataMiniLcmApi.cs:1688).
- Not modelled at all: AlternateForms, allomorph environments, inflection classes, features, phonology, rules, templates (FL/FwLiteProjectSync sync diffs only WS, Publications, POS, SD, ComplexFormTypes, MorphTypes, Entries: CrdtFwdataProjectSyncService.cs:~130-153).
- Complex form components are one flat record over `LexEntryRef.ComponentLexemesRS` (FwDataMiniLcmApi.cs:759-785).

## C. GUIDs across the sync

| Object | Same GUID both sides? | Evidence |
|---|---|---|
| Entry | Yes | fwdata read `Id = entry.Guid` (:691); create `CreateEntry(entry.Id, ...)` -> `ILexEntryFactory.Create(id, ...)` (:1115, LcmHelpers.cs:268). CRDT `CreateEntryChange` reuses `entry.Id` (H-edge-cases doc §7). |
| Sense | Yes | `Id = sense.Guid` (:809); `LexSenseFactory.Create(sense.Id)` (:1459) |
| ExampleSentence | Yes | `Id = sentence.Guid` (:826); `Create(exampleSentence.Id)` (:1731) |
| Translation | Yes | `Id = t.Guid` (:832); `CmTranslationFactory.Create(parent, type, translation.Id)` (:1747) |
| POS | Yes | `Id = lcmPos.Guid` (:678); `.Create(partOfSpeech.Id, PartsOfSpeechOA)` (:321) |
| SemanticDomain | Yes | `Id = semanticDomain.Guid` (:433); `.Create(semanticDomain.Id, ...)` (:469) |
| Publication | Yes | `Id = lcmPossibility.Guid` (:378); `.Create(pub.Id, ...)` (:366) |
| ComplexFormType | Yes | `Id = t.Guid` (:531); `.Create(complexFormType.Id)` (:544) |
| MorphType | Yes, canonical GUIDs. | `Id = morphType.Guid` (:614). `CreateMorphType` throws in fwdata: "predefined" (:625-628). |
| WritingSystem | No. fwdata has none. | fwdata side sets `Id = Guid.Empty`, identity is `WsId` code + Type (:121, :127, WritingSystemSync.cs:33). CRDT assigns its own (CreateWritingSystemChange.cs:48). |
| ComplexFormComponent | Not as an id. | fwdata side reads `component.Guid` etc. only for the two entry/sense ends (:763-778) and leaves `Id` empty (`MaybeId` null). CRDT mints `component.MaybeId ?? Guid.NewGuid()` (AddEntryComponentChange.cs:29). Identity across sides is (complex form entry, component entry, optional component sense). |

All VERIFIED. The `Create(id)` overloads are the pass-through. Defaults `Guid.NewGuid()` apply only when `Id == default` (:314, :362, :462, :536, :1099, :1458, :1730).

**Objects the sync creates with no CRDT identity (VERIFIED).**
- LexemeFormOA: `IMoStemAllomorphFactory.Create()` or `IMoAffixAllomorphFactory.Create()` with no GUID (LcmHelpers.cs:240-241), assigned in `SetLexemeForm` (:274).
- MSA: `SandboxGenericMSA.Create` through `lexSense.SandboxMSA` (:1688). LCM makes the MoStemMsa etc.
- LexEntryRef (complex form ref): `ILexEntryRefFactory.Create()` (:1336).
- GUID choice: the parameterless factory path ends in `CmObject.InitializeNew` -> `ICmObjectIdFactory.FromGuid(Guid.NewGuid())` (liblcm DomainImpl/CmObject.cs:1370). Random, not derived. INFERRED: `Create()` reaches that line; I did not trace each factory.

**Re-finding them (VERIFIED).** Always through the owner: `entry.LexemeFormOA` (UpdateEntryProxy.cs:23, FwDataMiniLcmApi.cs:1116), `lexSense.MorphoSyntaxAnalysisRA` (:1686), `lexEntry.ComplexFormEntryRefs.FirstOrDefault()` (:1327-1328). Never by GUID. A re-run on a deleted-then-recreated entry makes new allomorph GUIDs (INFERRED).

**fwdata->CRDT: dropped GUIDs (VERIFIED by absence in reads).** LexemeFormOA / AlternateForms allomorph GUIDs, MSA GUIDs, LexEntryRef GUIDs for complex forms (the component link is read via ends only), and every unmodelled class (environments, templates, features, phonology, rules). Variant entry-ref GUIDs are kept (:741).

## D. The single path today

- **fwdata->CRDT import (VERIFIED).** `MiniLcmImport.Import` (MiniLcmImport.cs:20-47) creates the CRDT project and calls `ImportProject` (:49-60), which takes a `ProjectSnapshot` and runs `ProjectImporter.ImportProject` (MiniLcm/Import/ProjectImporter.cs:15-): WS, POS, publications, complex form types, morph types (synced), semantic domains (`BulkImportSemanticDomains`), then `BulkCreateEntries`. `ResumableImportApi` dedupes by id so a crashed import can resume (:56-59).
- **Commit granularity (VERIFIED).** Not one commit. `BulkCreateEntries` accumulates changes (`CreateEntryChange`, `AddEntryComponentChange`, `AddComplexFormTypeChange`, `AddPublicationChange`, sense changes) and flushes `AddChanges` when the list exceeds 1000 (LcmCrdt/MiniLcmImp/CrdtEntryApi.cs:21, 55-83, 90-134). Harmony `AddChanges` makes one `Commit` per call (H/SIL.Harmony/DataModel.cs:92-117). So about one commit per roughly 1000 changes, hundreds of entries each, no history. Writing systems, POS, etc. go one API call (one commit) each.
- **First sync vs import.** `Import` is a sync with a null snapshot (CrdtFwdataProjectSyncService.cs:39-42, 60-) and also saves fwdata. `Sync` (with a snapshot) diffs both directions (:146-175, `EntrySync.SyncFull` each way), which also writes CRDT->fwdata.
- **Timing (VERIFIED, indirect).** Import logs elapsed time (MiniLcmImport.cs:25, 37-40), no stored numbers. `FL/FwLiteProjectSync.Tests/SyncBenchmark.cs:~55-60`: first sync of Sena-3 "rough mean ~57s" on CI, bound 80s. docs/site/technical/sync/fwheadless-merge.md:25: first-ever sync "takes minutes". Entry count of Sena-3 not checked.
- **CRDT->blank .fwdata (VERIFIED).** There is no production path. `IProjectLoader.NewProject` (FwDataMiniLcmBridge/LcmUtils/ProjectLoader.cs:22-28, 84-135) makes a blank fwdata via `LcmCache.CreateNewLangProj`, but its only callers in the repo are tests (SyncFixture.cs:~103, CanonicalMorphTypeTests.cs:28). The sync fixture then runs `Sync` against that blank project (INFERRED as the closest thing to CRDT->blank). Production sync merges into an existing .fwdata.

## Implications for the design (INFERRED)
- A one-to-one fwdata<->CRDT mapping exists only for the 8 MiniLcm object kinds. Allomorph, MSA, LexEntryRef and all grammar are re-derived by owner, with random GUIDs. Pin them if grammar must round-trip.
- Harmony import has no "one commit per object": batch of ~1000 changes per commit.
- Harmony has no preview or transaction rollback (harmony-edge-cases §2); copy the SQLite file.
