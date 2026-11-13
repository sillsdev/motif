READY 76e200c

# Source/Build format spike: report

Repo: ~/work/sb-spike-format (5 commits, branch master). `dotnet test tests/SbSpike.Tests`: 10 passed, 0 failed, on Linux, .NET 10. Clones were read-only (only bin/obj written). Harmony used is the package SIL.Harmony 0.2.1-rc.445 that LcmCrdt references, not the clone in research-src/harmony.

## 1. Types: PASS
`src/SbSpike.Types/MotifObjects.cs`, `MotifChanges.cs`. MoStemAllomorph (EntryId, Environments list, StemName, no Form), PhEnvironment, MoStemMsa, PhRegularRule. Changes: 4 creates, MotifJsonPatchChange<T>, MotifDeleteChange<T>, SetGuidListChange<T> (whole-list set). `MotifRegistration.Register` runs as `services.Configure<HarmonyConfig>` after LcmCrdt's ConfigureCrdt, plus ModelVersion 3. No LcmCrdt edit. Test `Spike1Types.Create_patch_setlist_delete_and_remove_reference_policy`.
Three things the ADR does not say were needed:
- `Add<T>()` is not enough. MiniLcm's IObjectWithId polymorphism is attribute-based, so serialisation threw. Use `AddWithCustomPolymorphicMapping<T>(name)`.
- **Tables.** LcmCrdt applies its own EF migrations, and a new model version makes Harmony drop and re-project every registered table. Motif tables must exist before that first reconcile. I create them with a connection-open interceptor (`MotifTablesInterceptor`, hand DDL) and silence PendingModelChangesWarning. Product needs its own migration route; this is the biggest unanswered cost of ADR 0102.
- EF caches the model per process, so mixed clients in one process need an IModelCacheKeyFactory keyed by flavour (test-only concern).

## 2. Complete change: PASS
`Spike2Complete.Two_clients_complete_same_entry_independently_one_object_results`. Entry made through MiniLcm; "complete" is one commit with two creates, ids = UUID v5 (fixed namespace, entry id + role; my own v5, checked against the RFC vector). Two clients complete independently, sync: no error, one allomorph and one MSA per entry on both, both complete commits kept in the log.
**ADR 0107 is wrong that re-creating is a harmless no-op.** It is a no-op only while the object is live. A deleted object is revived by a later create, with the create's initial fields (environments wiped); observed before the fix. Fix in the types: NewEntity returns the existing tombstone unchanged. Test `Complete_keeps_edits_and_does_not_revive_a_deleted_allomorph`. Born-deleted-when-entry-is-deleted also works.

## 3. Opaque riding: PASS, with the gap CONFIRMED as a real hazard
**Test trap.** `DataModel.SyncWith(otherModel)` passes typed IChange objects, never JSON, so the plain client threw on a Motif type and the opaque path was never used. I wrote `Wire` (serialise with sender's options, deserialise with receiver's) to mimic LexBox HTTP sync. Any later test must use it.
- `Spike3Opaque.B_keeps_motif_commits_opaque_replays_and_syncs_them_back_unchanged`: B holds 4 Motif changes as OpaqueChange, does ordinary edits, full RegenerateSnapshots throws nothing, sync back to A. Every commit's JSON is identical on A and B; A's allomorph, environment and entry edits are correct, and A replays cleanly.
- `Deleting_the_entry_on_B_cascades_...`: entry deleted on B, A's allomorph and MSA get DeletedAt; the environment (not owned) stays live.
- `Gap_lcmcrdt_edit_change_...` (versions 2 and 3): neither client can author an LcmCrdt EditChange (SetPartOfSpeechChange) on a Motif id: A refuses with a type mismatch, B with NotSupportedException from NewEntity. A hand-built remote commit delivered to B **throws NotSupportedException in AddRangeFromSync and the whole batch rolls back**: a legitimate commit in the same batch is lost too, and a retry throws again. So it would fail every sync of that client until fixed. Authoring it as model version 3 (newer than B) did not get it skipped, contrary to harmony-edge-cases.md §1/§6. Full replay later is fine, because the commit never lands. Rule stands: never emit an LcmCrdt change against a Motif id; add a guard in Motif's authoring path.

## 4. Build to .fwdata: PASS
`Spike4Build.Build_keeps_log_guids_and_is_identical_twice`; code `tests/SbSpike.Tests/Build.cs`. LibLCM and FwDataMiniLcmBridge run on Linux with no environment fixes (ICU 78, templates read from the SIL.LCModel nuget contentFiles; no Windows fallback needed). `IProjectLoader.NewProject` makes the blank project.
**Key finding: every LCM factory has `Create(Guid)`** (ILcmFactory<T>), which throws on a duplicate GUID. So environment, allomorph and MSA objects can carry log GUIDs. But the **bridge's CreateEntry builds the lexeme-form allomorph with parameterless Create()** (random GUID), and its sense-POS code makes a random stem MSA. The hand bridge therefore replaces them: new object with the log GUID, assigned to `entry.LexemeFormOA`, `MorphoSyntaxAnalysesOC.Add`. Gotchas hit: read the old allomorph's Form and MorphType before replacing it (LCM clears refs on delete); LCM auto-deletes the orphaned bridge MSA when its last sense is repointed (explicit Remove then NREs). Without a bridge hook these are replace-after-create steps; a small upstream "caller-supplied companion GUIDs" option would be cleaner.
Verified from the .fwdata XML: all allomorph, MSA, environment, entry and sense GUIDs equal the log's; no leftover bridge objects; two builds give an identical set of 9 GUIDs.

## 5. Parser: PASS
`Spike5Parse.Parse_depends_on_the_allomorph_environment_held_in_the_log`. PanGloss `pangloss parse` reads .fwdata directly. Built from a copy of /mnt/h/repos/PanGloss/rust in ~/work/sb-spike-format/pg-copy (3 min, plain cargo in the copy; pg.ps1 reformats the tree so I did not run it in the repo). V1, allomorph env `/ _ #`: `kat -> |kat`. Log change swaps the list to env `/ _ a`, rebuild: `kat -> -`; control `dog` parses both times. Caveat: the phoneme inventory is added by the Build as spike-only code (template's set plus k a t d o g); a real build takes it from the log.

## Surprises, ADR corrections
- ADR 0102 "No upstream change is needed": true for code, false for storage (migrations, above).
- ADR 0102 "Not yet tested" gap: tested; it is a batch-poisoning throw, not a skip.
- ADR 0107: complete-after-delete revives (fixed in type); v5 itself is fine.
- ADR 0107 "GUIDs kept" needs the replace-after-bridge step; Build twice is deterministic.
- Not done: no change-type validation of Motif JSON on the server, no real LexBox server, no .fwdata to log export direction.

READY 76e200c
