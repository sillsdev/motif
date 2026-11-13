# Harmony / FW Lite edge cases for Source/Build

H = ~/work/research-src/harmony/src/SIL.Harmony; L = ~/work/research-src/languageforge-lexbox/backend; FL = L/FwLite. VERIFIED = read in code; INFERRED = reasoned. No builds or tests run.

## 1. Unknown change types
- Default is Throw (H/Config/HarmonyConfig.cs:47). LcmCrdt sets Fallback (FL/LcmCrdt/LcmCrdtKernel.cs:449). VERIFIED.
- Server: stores commits as `ServerJsonChange{Type, ExtensionData}` (Harmony.Core/ServerCommit.cs:30-36), jsonb round-trip (L/LexData/Entities/CommitEntityConfiguration.cs:30-33), no type check on `Add` (CrdtController.cs:41-48). It stores and relays opaque JSON. VERIFIED.
- FW Lite: keeps the commit; the change becomes `OpaqueChange` with raw JSON, written back unchanged (H/Changes/PeekThenConcreteChangeConverter.cs:47-55, 68-94). Replay skips it: null for new entity (H/SnapshotWorker.cs:112-116), no-op on existing (OpaqueChange.cs:19-20). It does not throw. VERIFIED.
- Replay later: `ReconcileModelVersions` replays from the oldest commit whose type a new `ModelVersion` lists (H/ModelVersionService.cs:20-24, 50-60; test `UnknownTypeIsAppliedAfterUpgrade`, ModelVersionTests.cs:176). The new type must be listed in a new version or no replay happens. A newer-version change that throws is also skipped (SnapshotWorker.cs:84-88). VERIFIED.
- Gap, INFERRED: a known EditChange on an entity whose create was opaque would call `NewEntity` and throw (EditChange.cs:8-12), caught only if authored by a newer model version (SnapshotWorker.cs:84).
- Registration: `ConfigureCrdt` in LcmCrdtKernel.cs: `ObjectTypeListBuilder` (:226-290), `ChangeTypeListBuilder` (:376-430), `ModelVersionBuilder` (:452). The change list freezes when JSON options are first built (HarmonyConfig.cs:124). VERIFIED.

## 2. Preview without committing
- No public API. Each `AddChange(s)` commits in its own transaction (H/DataModel.cs:62-129), and a fresh DbContext is made per call (H/Db/CrdtRepository.cs:17-27), so a caller cannot hold an outer transaction and roll back. VERIFIED.
- Internal `SnapshotReplay.ReplayCommits` applies commits "without persisting anything" (SnapshotWorker.cs:41-50) but is only used for at-commit reads (DataModel.cs:347-358). InternalsVisibleTo is Tests/Benchmarks only (per harmony.md). Preview at head needs a small upstream API or a fork.
- Working route, VERIFIED: copy the SQLite file. `OpenTempProjectCopy` uses `BackupDatabase` and opens a full CRDT project on the copy (FL/LcmCrdt/CrdtProjectsService.cs:303-330). The sync dry-run uses it (FL/FwLiteProjectSync/CrdtFwdataProjectSyncService.cs:70-76). `SnapshotAtCommitService` forks the same way (:40-55, 85-92). Cost is one file copy, O(file size), plus replay of the applied commits (checkpoint every 100 changes, HarmonyConfig.cs:41). Not timed.
- Local-only commits: allowed, but no per-commit sync filter. Sync sends everything newer than the remote, per ClientId (Harmony.Core/QueryHelpers.cs:43-73), and `ShouldSync` is all-or-nothing (ISyncable.cs:10). Drafts needing to stay local need a separate, never-synced DB file. VERIFIED.

## 3. References to deleted objects
- Delete is soft: `DeletedAt` set from commit time (H/Changes/DeleteChange.cs:35-39).
- On deletion, replay finds every referrer (`WhereReferences`, includeDeleted) and calls `RemoveReference` on a copy, recursing if that deletes the referrer (SnapshotWorker.cs:97-100, 195-225). VERIFIED.
- The outcome is per-type host policy, silent either way:
  - Sense: PartOfSpeech reference nulled; Entry deleted so Sense deleted (FL/MiniLcm/Models/Sense.cs:24-40).
  - ExampleSentence: deleted with its Sense (ExampleSentence.cs:21-29).
  - ComplexFormComponent: deleted (ComplexFormComponent.cs:55-69).
  - Sample Word: antonym nulled (Harmony.Sample/Models/Word.cs:28-35).
- A change that adds a reference must itself check the target (`SetPartOfSpeechChange.cs:15-21`; creates are born deleted if the parent is, CreateEntryChange.cs:53 region). Otherwise it leaves a dangling guid (INFERRED from the sample comment, SetAntonymReferenceChange.cs:13-16).
- INFERRED: children cascaded by a parent's delete are not revived when a create change revives the parent (SnapshotWorker.cs:122-126).

## 4. Ordering
- Only a `double Order` with last-writer-wins; no list CRDT. `SetOrderChange` writes an absolute double (FL/LcmCrdt/Changes/SetOrderChange.cs:9-18; Harmony's generic one H/Changes/SetOrderChange.cs:45-82, `Between` = midpoint). VERIFIED.
- The author's client picks the double from the sibling orders at authoring time: midpoint, previous+1, next-1, max+1 (FL/LcmCrdt/OrderPicker.cs:11-40). Nothing is rebased on merge.
- Concurrent moves of one item: later commit (HybridDateTime, Counter, Id) wins (QueryHelpers.cs:192-198).
- Concurrent inserts at the same spot: both compute the same midpoint and get equal Order. Reads tiebreak by Id (`order by "Order", Id`, FL/LcmCrdt/Data/EntryQueryHelpers.cs:54; WritingSystemsOrdered, LcmCrdtDbContext.cs:23): deterministic, interleave arbitrary. I checked those two paths only.
- Precision exhaustion from repeated midpoints: INFERRED.

## 5. Field-level merge
- `JsonPatchChange<T>` applies a JSON Patch to the entity (FL/LcmCrdt/Changes/JsonPatchChange.cs:20-23); index paths are rejected (:30-48). Different properties from concurrent patches both survive; the same property is LWW in commit order. VERIFIED.
- Multistring: one op per writing system, `/{path}/{ws}` (FL/MiniLcm/SyncHelpers/MultiStringDiff.cs:11-32). VERIFIED.
- Rich multistring: also per writing system, but the whole `RichString` for that ws is replaced; the code notes it could be finer (MultiStringDiff.cs:44-47). Concurrent edits to one ws overwrite each other, no character merge. VERIFIED.
- Dedicated changes (AddSemanticDomain, AddTranslation) merge per collection element; I did not read each.

## 6. Validation hooks
- No host hook that rejects a commit. VERIFIED. What exists:
  - `ValidateCommits` is hash-chain only and off unless `AlwaysValidateCommits` (H/DataModel.cs:205-223; HarmonyConfig.cs:34-38).
  - `BeforeSaveObject` (HarmonyConfig.cs:23, called SnapshotWorker.cs:233): throwing fails the whole replay or sync.
  - Locally authored commits are strict: an entity-type mismatch throws `CommitValidationException` and the transaction rolls back (SnapshotWorker.cs:73-77, 178-189; DataModel.cs:121-128). Synced commits are lenient and the change is skipped (SnapshotReplay.cs:11-23).
- Remote commits are never rejected: server does no validation (CrdtCommitService.cs). On the client a throwing change is skipped only if authored by a newer model version; otherwise the exception fails the sync transaction (test `ChangeFromTheCurrentVersionThatThrowsFailsTheSync`, ModelVersionTests.cs:322). INFERRED: a unique-index violation (WritingSystem WsId+Type, LcmCrdtKernel.cs:289; component indexes :363-373) from a remote commit would raise `DbUpdateException`, dump `last-failed-import.json`, and fail every sync (DataModel.cs:167-177).
- LcmCrdt validates only at the MiniLcm API layer (`AddMiniLcmValidators`, LcmCrdtKernel.cs:109). Motif's Apply step must validate before `AddChanges`.

## 7. Determinism and GUIDs
- Entity ids: yes. `Change<T>` takes `entityId` (H/Changes/Change.cs:41-46); `CreateEntryChange` reuses `entry.Id` when non-empty (FL/LcmCrdt/Changes/CreateEntryChange.cs:14-17). Ids are in the change JSON, so replaying a Proposal on a new head gives the same object ids. VERIFIED.
- Re-applying a create to a live entity is a no-op; to a deleted entity it revives it; a different type on the same id is skipped or throws (SnapshotWorker.cs:73-77, 122-138).
- Not deterministic: commit Id and timestamp are new per `AddChanges` (H/DataModel.cs:102-111). A replayed Proposal is a later commit, so it beats concurrent edits in LWW. Baked-in doubles (Order) do not adapt to the new head.

## 8. Grammar in the FwData sync
- Sync is a diff over MiniLcm types only: WritingSystems, Publications, PartsOfSpeech, SemanticDomains, ComplexFormTypes, MorphTypes, Entries (FL/FwLiteProjectSync/CrdtFwdataProjectSyncService.cs:130-153), then `fwdata.Save()` (:114). VERIFIED.
- No code touches PhPhoneme, PhRegularRule or MoInflAffixTemplate (grep of FwDataMiniLcmBridge). "Untouched" is a consequence of design; no test or doc states it as a guarantee. VERIFIED absence.
- Modeled-class side effects that do change grammar-adjacent objects, VERIFIED:
  - Setting a sense POS writes the MSA's POS field (FwDataMiniLcmBridge/Api/MorphoSyntaxExtensions.cs:7-31), or creates an MSA through `SandboxMSA` if none exists (FwDataMiniLcmApi.cs:1686-1688).
  - Creating an entry creates a MoStemAllomorph or MoAffixAllomorph as `LexemeFormOA` (LcmHelpers.cs:236-278).
  - Lexeme form edits write `LexemeFormOA.Form` (FwDataMiniLcmApi.cs:1116).
- CRDT entry deleted: the sync calls `lexEntry.Delete()` (FwDataMiniLcmApi.cs:1444-1453). LibLCM `LexEntry.DeleteObjectBasics` clears LexemeFormOA, MorphoSyntaxAnalysesOC, SensesOS, AlternateFormsOS (/mnt/h/repos/liblcm/src/SIL.LCModel/DomainImpl/GeneratedClasses.cs ~51248-51300). Allomorphs and MSAs owned by the entry are deleted with it. That is LCM behaviour, not sync code. Sense delete leaves entry-owned MSAs (INFERRED: the sense holds `MorphoSyntaxAnalysisRA`, a reference). Cleanup of non-modeled objects that reference a deleted allomorph (environments, templates) is LCM's; not traced.
