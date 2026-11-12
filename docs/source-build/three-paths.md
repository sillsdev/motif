# Motif phase 1 in Paratext 10: three paths (2026-11-10)

Design only. Sources: the ten reports in `reports/`, plus four spot checks in source (noted "checked"). Items the reports mark unverified are still unverified here.

## What is the same in every path

- **Translators and consultants never open Motif.** The Interlinearizer owns their screen (text, glosses, approval, back-translation export #149). Our visible contribution there is one thing: glosses already filled in for inflected words nobody has glossed yet.
- **PanGloss runs as `pangloss serve`**, a child process speaking JSON lines (serve protocol v1). It reads a `.fwdata` directly, with no LibLCM and no lock.
- **An approved gloss is a test, not a lexicon entry.** For each approved word gloss, the parser must produce an analysis consistent with it. "Tests" = share of approved glosses explained. "Coverage" = share of next-book tokens with a parse.
- **USFM builds the grammar** through the Interlinearizer's own tokens (Book→Segment→Token from USJ), not a second USFM reader: next-book word frequencies set the work queue, unexplained approved glosses trigger Proposals, and forms sharing a stem are paradigm evidence for the AI.
- **The linguist's Motif is three pages**: Inbox (Proposals with their GDR story and verse examples), Next Book (coverage, tests, top unexplained words), Settings. The other eight rail pages are hidden.
- **The first two weeks are nearly the same** in all three paths (see the recommendation). The decision can wait for that slice.

The paths differ in one thing: **what a Proposal is applied to**.

---

## Path 1: Trunk. Proposals merge into the real `.fwdata`

**Pitch.** Keep Motif as it is: a desktop app whose Proposals apply, one-way, to the real FieldWorks project. The PR layer is Motif (`motif.db` plus the `.fwdata`). The Docs layer is the Interlinearizer's Harmony store and the FW Lite CRDT. The two meet through the LexBox merge, which already carries lexical data both ways. A CRDT-only user gets a local `.fwdata` that the LexBox merge core keeps in step. Motif gets no backend seam; the merge is the seam.

**Day in the life.**
- *Translator:* glosses in the Interlinearizer. New words arrive pre-glossed. Sometimes Paratext says "the dictionary is busy" (see risks).
- *Linguist:* closes FieldWorks, opens Motif from a Paratext menu, reviews five Proposals, presses Apply, then Sync so FW Lite sees the new entries.
- *Consultant:* reads the exported back-translation.

**Architecture on one PC.**
- Processes: Paratext 10; the Interlinearizer host (net10); the FW Lite server from the Lexicon extension (net10, port 29348); a small "Motif bridge" extension that spawns `pangloss serve`; the Motif app (net10, Avalonia).
- Stores: the `.fwdata` is the system of record for grammar and lexicon. The FW Lite CRDT is a lexical view of it. `motif.db` holds Proposals.
- `.fwdata` vs CRDT: Motif only ever targets `.fwdata`. For a CRDT user, Motif runs `CrdtFwdataProjectSyncService` locally (it needs no server) before and after Apply.
- PanGloss: the bridge points `pangloss serve` at the `.fwdata` and restarts it after each Apply (7–9 s load on 22k entries).
- Evidence: the bridge reads approved analyses through `interlinearizer.repository.*` and writes a bundle that Motif ingests; `pangloss batch --analyses` compares.
- Conflict and rebase: Motif's existing gates. When the `.fwdata` moved (FLEx edit or a merge), the Proposal shows "Update to current project"; items that no longer fit offer Use mine / Use FieldWorks / Fix with chatbot.

**Build / need / ask.**
- Build: the bridge extension, evidence ingest, the three-page shell, the local-merge choreography.
- Need: Interlinearizer read access and a way to show parser suggestions; the LexBox merge core as a supported library.
- Ask LexBox: fix #2512/#2918 (atomic merge base), and let a caller tell the Lexicon extension's server to release a project. Ask Interlinearizer: see the shared asks below.

**Risks and gotchas.**
- **Lock fights.** FW Lite opens `.fwdata` with `CreateCacheFromLocalProjectFile` (checked: `ProjectLoader.cs:71`) and keeps the cache for 30 minutes after last use (fwlite.md §5). Motif Apply needs the exclusive `.fwdata.lock` and FieldWorks closed. So the Interlinearizer's lexicon lookups can block the linguist's Apply on the same PC. This is the opposite of seamless.
- FieldWorks classic wins merge conflicts (lexbox-sync.md), so a translator's FW Lite edit can silently lose to a Proposal. A stale or lost merge snapshot corrupts later diffs (fwheadless-merge.md:61, 99).
- #229 (create entry from a morpheme) and Apply both write the same lexicon by different routes.
- A CRDT-only project has no `.fwdata` to merge into; the merge never creates one.
- Phase 2: closes nothing; this is today's design. But it pushes phase 2 from day one: every approval changes the team's real dictionary.

**Effort.** About 2–3 person-months, most of it sync choreography and lock handling. First slice: bridge + `pangloss serve` on a real `.fwdata`, suggestions for one chapter, tests report in Motif.

---

## Path 2: Fork. Proposals stack on a private copy; nothing is written back

**Pitch.** Treat the live data like `main` on someone else's repository: read it, never push to it. Motif keeps a private working `.fwdata` (the fork) that is **derived**: a fresh copy of the live lexicon plus every approved Proposal replayed in order. The grammar *is* the stack of approved Proposals. PanGloss reads the fork. The PR layer is Motif plus the fork. The Docs layer is everything live (FieldWorks project, FW Lite CRDT, Interlinearizer store), untouched. Throwing the work away is deleting one folder. Keeping it (phase 2) is one deliberate act: "Send to FieldWorks", which is today's Apply.

**Day in the life.**
- *Translator:* same as Path 1, with no "busy" messages. Their dictionary never changes behind their back.
- *Linguist:* opens Motif from Paratext with FieldWorks still open. Inbox shows "12 words in Ruth are unexplained; this change explains 9", with the verses. Approve. Two minutes later those words carry glosses in the Interlinearizer. Occasionally one card says "The dictionary changed under this change" with the same three choices.
- *Consultant:* reads the back-translation; each verse shows how many glosses nobody has approved yet.

**Architecture on one PC.**
- Processes: as Path 1.
- Stores: live data (read only for us); the fork folder beside `motif.db` (disposable); `motif.db` (Proposals, GDRs, targets: the only thing worth backing up).
- The seam is one function, `CopyLive(source) → .fwdata`:
  - `.fwdata` source: copy the file. No LibLCM lock is needed to copy, and Baseline capture already works with FieldWorks open (motif.md §2).
  - CRDT source: write the CRDT's lexicon into a blank template `.fwdata` using LexBox's `MiniLcmImport.ImportProject(importTo, importFrom)` (checked: the signature is generic over `IMiniLcmApi`; this direction is untested). Grammar starts empty, because the CRDT has none, and the Proposals build it.
  - After that both sources look identical to Motif. Apply targets the fork path instead of the live path; the engine is unchanged.
- Rebase is a rebuild: when the live data changes, `CopyLive` again and replay the approved Proposals through the existing gates. No merge code. A Proposal that no longer fits stops the replay at that card: Use mine / Use theirs / Fix with chatbot. Then the tests rerun, as CI would.
- PanGloss: `pangloss serve` on the fork, restarted after each rebuild.
- Evidence: as Path 1. Because nothing we do reaches the live lexicon, a translator's inflected-word gloss can never become an entry through us.

**Build / need / ask.**
- Build: the bridge extension; evidence ingest; `CopyLive` (two cases); "Apply to fork" plus replay; the three-page shell.
- Need: nothing from LexBox at runtime for `.fwdata` projects. For CRDT projects, LexBox's import code as a library (MIT).
- Ask: only the shared Interlinearizer asks. This path has the smallest dependence on other teams.

**Risks and gotchas.**
- **The fork diverges.** Stems the AI adds live only in the fork; the team dictionary does not grow. That is right for phase 1 and is the phase-2 cost: graduation replays the whole stack onto the real project at once. Keeping the fork rebased continuously keeps that cheap, but it is still one big review.
- Replay must be deterministic: created objects need the same GUIDs every time. The conflict rules mention canonical ids and "already-realized creation" (checked: `conflicts-and-rebase.md`), but replay from scratch is not a tested path.
- Replay cost grows with the stack. Hundreds of Proposals are fine; cache the last good fork if not.
- CRDT → blank `.fwdata` is unproven. FW Lite's entry model is flat (MSAs collapsed to POS, no allomorphs; #2558/#2559), so stems arrive thin and PanGloss may need Proposals to enrich them in the fork.
- Reading a CRDT while FW Lite writes it: SQLite multi-process behaviour is unverified (fwlite.md open question 2). Reading through the FW Lite server's `/api/mini-lcm` avoids it.
- Phase 2: closes nothing. Path 1's Apply remains as the graduation step; Path 3's writer could replace it later.

**Effort.** About 2–3 person-months; the unknowns are replay determinism and the CRDT copy. First slice below.

---

## Path 3: One plugin. Motif's engine inside the Interlinearizer, writing through MiniLcm

**Pitch.** Stop being a separate product. The Interlinearizer host is net10, Motif is net10, FW Lite is net10: put the Proposal engine in their host (or beside it as a second process in one extension), render the Inbox as a Paratext web view, and write every change through `IMiniLcmApi`, the one interface that already serves both `.fwdata` and CRDT. The PR layer is a Proposal store holding pending change lists outside Harmony (Harmony has no branch or preview; every `AddChange` commits). The Docs layer is FW Lite. One install, one window, one write path.

**Day in the life.**
- *Translator:* one extension. A quiet "Grammar" tab exists but is empty for them.
- *Linguist:* a "Grammar" tab inside Paratext, no second app: Inbox, Next Book.
- *Consultant:* as before.

**Architecture on one PC.**
- Processes: Paratext 10; one combined extension host; the FW Lite server; `pangloss serve`.
- Stores: FW Lite (either backend) is the system of record. Proposals, GDRs and targets live as records in the Interlinearizer's per-source Harmony SQLite, or in a file next to it.
- The seam is `IMiniLcmApi`. It models entries, senses and POS only. Grammar needs upstream model work first (allomorphs, MSAs, then affix slots, templates, rules).
- PanGloss: a new producer writes the `pangloss-project` JSON snapshot from MiniLcm; none exists (pangloss.md §6c).
- Conflict and rebase: we build it. Each Proposal records the before-state it read; before applying, reread and compare. After applying, Harmony is last-writer-wins and FieldWorks classic wins the server merge, so an approved change can be altered later with no signal. The only defence is rerunning the tests after every sync and raising "an approved change was altered".

**Build / need / ask.**
- Build: a second Proposal writer and gate set on MiniLcm (Motif's 473-field vocabulary targets LibLCM directly; motif.md §6c); the snapshot producer; the web-view UI (the Avalonia UI does not carry over).
- Need: LexBox #2558 and #2559 plus grammar entities; an atomic multi-entity write route; `updateEntry` (unimplemented in the entry service). From the Interlinearizer: agreement to host us, on their release train.
- Ask: all of that.

**Risks and gotchas.**
- The CRDT has no grammar beyond POS, and LCM Grammar JSON is explicitly "not an editing, synchronization, or storage format" (liblcm.md §2). We would be asking another team to model the hard part of FieldWorks.
- Harmony stores every change forever and hashes only ids (harmony.md §5): Proposal content is unprotected and machine churn is permanent.
- The Interlinearizer is 0.8 alpha with a milestone on 2026-11-13; the Lexicon extension is 0.0.1 with no tests. Combining couples a few dozen real users to two alphas.
- Paratext is AGPL since 2026-08-07; a combined plugin needs a licence check.
- Revives ADR 0013 ("Harmony is the change mechanism") in a new form, against Plan A.
- Phase 2: the best end state if FW Lite becomes the future of FieldWorks; it abandons the LibLCM Proposal engine if it does not.

**Effort.** 9–12 person-months and calendar-bound by two other teams. First slice: the MiniLcm → snapshot producer spike, plus a design conversation with LexBox on #2558.

---

## Shared asks (all paths)

To the Interlinearizer team (the interlinearizer team), before their suggestion and lexicon-port shapes set:
1. **A suggestion-provider hook**: a network object the engine asks per surface form, derived on read. The fallback, writing `status: 'suggested'`, `producer: 'pangloss'` records, works today by inference (interlinearizer.md §5), but Harmony keeps every rewrite forever and the suggestions change whenever the grammar improves.
2. **A supported read of approved analyses** for other extensions (form, gloss, morphemes, status, user, token refs). `repository.findLinksInBooks` has the shape; third-party access is unverified.
3. **#229 guard**: create lexicon entries only from morpheme-level items, never from a whole inflected word.
4. #149 on a date we can plan around; it is the consultant's deliverable.

To Platform.Bible: confirm that one extension may call another's commands, and that a spawned child gets more than about 1.5 s before SIGKILL at exit (or may outlive the host). Motif jobs must be resumable either way.

## Comparison

| | 1 Trunk | 2 Fork | 3 One plugin |
|---|---|---|---|
| Seamless on one PC | Poor: FieldWorks closed, lock fights, manual Sync | Good: nothing is locked or written; second window for the linguist only | Best when finished: one window |
| Phase-1 time to value | 2–3 months | 2–3 months | 9–12 months |
| Dependence on other teams | Medium (merge core, lock release) | Low (shared asks only) | Very high |
| `.fwdata` / CRDT parity | Weak: a CRDT-only project has no trunk | Good after `CopyLive`; CRDT stems are thin | Equal by design, once grammar exists upstream |
| Phase-2 upside | Already there, and pushed on users | Graduation is one deliberate step | Highest, if FW Lite wins |
| Risk | Data-loss class (merge, locks) | Fork divergence; replay unproven | Schedule and scope |
| "Can throw it away" | No: the real project has changed | Yes: delete a folder | No |

## Recommendation

**Path 2, Fork.** It is the only path that fits "may throw it away" and "must not push phase 2", it removes the FieldWorks-closed rule and the lock fights instead of managing them, and it gives `.fwdata` and CRDT users the same experience with one small seam and no merge code. It also matches the owner's analogy most literally: the live data is `main`, the Proposals are a stack rebased onto it, and the tests are CI. Path 1's Apply stays in the code as the phase-2 "Send to FieldWorks". Make the Path 3 asks now, because they are cheap and time-sensitive, but depend on none of them.

Candid cost: for a few months the best grammar knowledge lives in a private fork that FieldWorks users cannot see. If the linguists are mainly FLEx users who want every approved stem in their dictionary today, choose Path 1 and accept its friction.

**First two-week slice (useful for any path):**
1. A bridge extension spawns `pangloss serve` on a `.fwdata` and offers glosses for one chapter in the Interlinearizer, through the persisted-suggestion fallback.
2. Read the approved analyses for one book and show the tests and Next Book numbers in Motif.
3. Fork proof: copy a live `.fwdata` with FieldWorks open, apply one Proposal to the copy, restart PanGloss, watch the gloss appear in Paratext. Rebuild twice and compare the two forks.
4. In parallel, a spike: CRDT → blank `.fwdata` through `MiniLcmImport`, then `pangloss serve` on the result.

**Questions the owner must answer.**
1. In phase 1, must an approved Proposal change the team's real dictionary, or may it live only in Motif's fork until someone chooses to keep it? (Fork vs Trunk.)
2. Do phase-1 linguists accept a second window opened from Paratext, or must everything sit inside Paratext? (Only Path 3 gives the latter.)
3. For a CRDT-only project, is "grammar starts empty and the Proposals build it" acceptable, or will every pilot team have a classic FieldWorks project with existing grammar?
4. Is the back-translation the Interlinearizer's joined glosses (#149), so we own no output path? What if #149 slips past the pilot?
5. Is "no parse" shown to translators in phase 1 (the spell flag), or not at all? The pitch and the 2026-11-07 note disagree.
6. Do we supersede ADR 0013 now? Paths 1 and 2 follow Plan A.
7. Who talks to the interlinearizer team this week, and do we offer to write the provider hook ourselves?

## Verify before deciding

| Fact | How to verify |
|---|---|
| Another extension may call `interlinearizer.repository.*` and write `producer`-tagged suggestions | Ten-line test extension against a dev build; ask the team whether they will support it |
| Persisted suggestions really outrank the pool and render sensibly | Same test; read `withPendingAnalyses` (`suggestion-engine.ts:159`) |
| Motif replay is deterministic (same GUIDs, same result) | Build a fork twice from one copy and one Proposal stack; diff the two `.fwdata` files |
| A `.fwdata` can be copied safely while FieldWorks or FW Lite holds it | Copy during a FLEx save loop on Windows; open each copy with LibLCM |
| `MiniLcmImport` can fill a blank `.fwdata` from a CRDT, and what it drops | Spike 4; compare entry counts and fields |
| PanGloss parses usefully from such a thin `.fwdata` | Run `pangloss batch` on the spike output with a hand-made Proposal adding two affixes |
| FW Lite takes the exclusive `.fwdata.lock` and holds it 30 minutes | Start the Lexicon extension on a project, then try FLEx and Motif Apply |
| kSharedXML would let Motif and FieldWorks share a project (only matters for Path 1) | LibLCM test with two processes; check that FieldWorks itself uses the shared backend |
| `pangloss serve` and the CLI build and run on Windows x64 beside Paratext, and the load time on a pilot-size project | Build once; time the `ready` line |
| Parser output distinguishes "no parse" from "search capped" | Read the serve result schema in `PanGloss/docs/formats/` |
| A translator's word gloss can be matched to a parse reliably enough to act as a test | Take 200 approved glosses from a PT9 import; have the AI judge matches; a linguist checks 50 |
| Child-process lifetime and cross-extension command rules in packaged Paratext 10 | Read `create-process.service.ts` and test in a packaged build; ask the Platform.Bible team |
| Paratext 10's packaged runtime, and AGPL reach for a combined plugin (Path 3) | Read `paratext-10-studio`; ask SIL legal |
| Whether pilot teams have classic FieldWorks projects, FW Lite only, or neither | Ask the pilot coordinators; this decides how much the CRDT case matters |
| LexBox's appetite and dates for #2558/#2559 (Path 3) | Ask the LexBox team on the issues |
