# Spike 4b: where the Build time goes, and is a LibLCM batch mode needed

Measured 2026-11-12 on the 20,000-entry trial log (copy of the spike's own generated sqlite, never the ProgramData originals). Spike worktree `~/work/sb-spike-trial`, branch `trial-timing`; harness `tests/SbSpike.Tests/BuildProfile.cs` plus stopwatch laps in `Build.cs`. Debug build, one machine, one run per row (the earlier 80 to 170 s noise applies).

## Short answer
The bridge is not the slow part. The earlier "100 s" (this run: 142 s) is almost all one line of the spike's hand-written grammar pass, where it re-points each sense to the log's MSA. That line makes LibLCM scan every MSA in the project once per entry, so it is quadratic. Removing the trigger in the spike takes the same Build from 142 s to 16 s with a byte-for-byte equal set of objects and links. No LibLCM "batch creation mode" is needed.

## Time breakdown (20k entries, seconds)
| step | now | with fix |
|---|---|---|
| 1 create blank project from template, open cache | 5.9 | 5.0 |
| 2 read the log through the CRDT API (POS plus entries) | 2.3 | 2.0 |
| 3 bridge `CreateEntry` x 20,000 (its lookups, writing-system resolution, factory Create, one UOW each, PropChanged, read-back of the entry) | 4.7 | 3.7 |
| 4 read the Motif objects | 0.3 | 0.3 |
| 5a grammar pass: replace lexeme-form allomorph (factory Create, delete old) | about 0.4 | 1.3 |
| 5b grammar pass: re-point senses to the log's MSA and drop the bridge's duplicate | **about 125** | 0.5 |
| 5c end of the grammar UOW (PropChanged, undo bookkeeping) | about 1 | 0.6 |
| 6 `fw.Save` | 1.5 | 1.3 |
| 7 `cache.Dispose` (writes the XML) | 0.9 | 0.9 |
| total | 142.5 | 15.9 |

Rows 5a to 5c are from the stopwatch laps (accumulated tick timers around each LibLCM call); 5b is the `sense.MorphoSyntaxAnalysisRA = msa` set (127 s phase total minus the other laps). Scaling check on the old path: 2,500 entries 2.4 s in that call, 5,000 4.9 s, 10,000 40 s, 20,000 about 125 s. Linear at first, then worse, as a full scan per entry predicts.

Not separable with this method: the bridge's lookups versus its LibLCM calls inside step 3. They are 4 s together (0.2 ms per entry), so they are not worth splitting. A sampling trace (EventPipe) was captured but the file was corrupt at 227 MB and would not convert; the laps replaced it.

## Root cause (evidence)
- The bridge makes a random-GUID stem MSA for each sense that has a part of speech. The Build needs the log's GUID, so it creates the right MSA and sets `sense.MorphoSyntaxAnalysisRA` to it.
- That set runs `LexSense.MorphoSyntaxAnalysisRASideEffects` then `HandleOldMSA` then `oldMsa.CanDelete`. `MoMorphSynAnalysis.CanDeleteIfSenseDeleted` (liblcm `OverridesLing_MoClasses.cs` lines 183 to 201) does `IMoMorphSynAnalysisRepository.AllInstances().Count(msa => msa.ComponentsRS.Contains(this))` and the same style scan over all ad hoc prohibitions. Each call reads every MSA in the project. 20,000 calls against up to 40,000 MSAs.
- Confirmed by the timers: that one call is 98% of the grammar pass; every other LibLCM call is under 1 s in total.

## Does an existing LibLCM mode suffice?
Nothing is needed for the bridge, and the existing pieces would not have fixed this anyway.
- `NonUndoableUnitOfWorkHelper`: skips the undo "after" XML of new objects and the DateModified pass. Does not touch the `CanDelete` scan, and the delete still serialises the old object.
- One outer UOW: `DoUsingNewOrCurrentUOW` already runs the body inside a current UOW without a new one. I wrapped all 20,000 `CreateEntry` calls in one UOW: step 3 went 4.8 s to 4.1 s, total 18.3 to 18.9 s (noise). Per-entry UOW and PropChanged cost is about 3 percent of the Build.
- `ISilDataAccessManaged`, `BackendBulkLoadDomain`: load-time only. LIFT and FlexText import in FieldWorks use `NonUndoableUnitOfWorkHelper` or one undoable task and no special create mode (LibLCM has no suppress or batch flag).
- The one LibLCM cost that matters is the homograph dictionary and the full-scan helpers (`AllInstances().Where(...)`) used as reference checks. They stay cheap only if the Build never replaces or deletes a referenced object.

## The fix (in the spike, done)
Strip the part of speech from each sense before the bridge sees it; then attach it ourselves: set `msa.PartOfSpeechRA` and `sense.MorphoSyntaxAnalysisRA = msa` when the old value is null. LibLCM returns at once for a null old MSA (`HandleOldMSA` line 5983), so no `CanDelete` runs and no duplicate MSA is ever made. About 15 lines in `Build.cs` (`BUILD_FAST=1`). Equality test `Fast_equals_slow` at 1,500 entries: same 6,026 objects, same sense to MSA links, same MSA POS and owner, same allomorph environment links; both modes 0 Build errors. Full 20k run: 15.9 s, 0 errors.
Same idea for the allomorph: do not let the bridge create the lexeme-form allomorph then replace it; that is 1.3 s now and would be about 0.3 s if built once (bridge change, see below). Not worth doing yet.

## If a "batch creation mode" were still wanted (smallest design)
Not recommended, but the real remaining waste is the bridge creating objects the Build then replaces. The smallest honest design is in the bridge, not LibLCM: a `CreateEntryOptions.Preassigned` (or an `Entry` with optional MSA and allomorph ids) so `FwDataMiniLcmApi.CreateEntry` calls `IMoStemMsaFactory.Create(guid)` and `IMoStemAllomorphFactory.Create(guid)` with the supplied ids. About 40 lines in the bridge plus 20 in MiniLcm models. Saves roughly 2 s of 16. If a LibLCM change is ever wanted for FieldWorks itself: replace the two `AllInstances()` scans in `CanDeleteIfSenseDeleted` with `ReferringObjects` (incoming-ref lookup, as `ClearIncomingReferences` already does), about 10 lines; it speeds every sense re-point in FW on big projects. I did not prototype it and did not touch `/mnt/h/repos/liblcm` or create a worktree.

## Expected times for 20k entries
- Today's spike Build: 100 to 142 s (80 to 170 s range).
- With the POS strip: about 16 s (5 s template and open, 2 s log read, 4 s bridge, 2 s grammar, 2 s save).
- Free-ish extra: reuse an opened blank project instead of creating it each time (saves about 5 s), plus pre-assigned GUIDs in the bridge (about 2 s). Floor near 8 to 9 s, so a full rebuild at 20k entries is realistic near 10 s. The incremental Build proposed in spike-trial.md is then not required to reach 10 s at this size, but is still the right shape for 700k entries (the scan fix removes the quadratic term, the rest is linear at about 0.8 ms per entry).

## Risks
- Debug configuration, one machine; Release should be faster, not slower.
- Test data: one MSA per entry and one POS. A log with several MSAs per entry, or senses with different POS, needs the real mapping (which sense points to which log MSA); the spike's original logic had the same assumption.
- The Build code with `BUILD_FAST` is spike code; the 2.5k/5k/10k limit rows used the old path.

READY
