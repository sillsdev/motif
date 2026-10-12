# Repository facts for the “Working grammar for every language” pitch

Reviewed on 2026-10-12, covering work from October 4 through the latest commits on October 10.
Source refs: Motif `a709b96bf`, PanGloss `45eabfd6`. Two Luna reviews inspected history and
implementation; this fact sheet distinguishes source behavior, published releases, measured examples,
and future plans. It is not a record of a fresh product test run.

## What has reached testers

Motif remains a working demonstration. [Beta002](https://github.com/sillsdev/motif/releases/tag/v0.2.0-beta002)
was published October 10, with Windows Setup, Linux x64 AppImage preview and macOS arm64 ZIP preview.
The Windows package needs no developer checkout or .NET SDK. Unix previews do not establish compatibility
with every installation; automatic updates are not enabled. The release includes PanGloss 0.8.1.
The later AI and Proposal work below is in current source, not evidenced as part of that beta.

PanGloss [0.8.3](https://github.com/sillsdev/PanGloss/releases/tag/v0.8.3) was published October 10,
with Windows x64, Linux x64 and macOS arm64/x64 binaries and SHA-256 files. Publication and asset names
were verified from GitHub's release API on October 12. Current Motif source pins 0.8.3; its release
manifest checks both executable identity and the formats Motif consumes. PanGloss 0.8.0 was tagged but
not published after a Windows build failure; 0.8.1 repaired that path.

Sources: Motif `docs/release-notes/0.2.0-beta002.md`, `pangloss-release.json`, commits `f688fbed0`,
`1afdf8016`, `fc7bbaa1b`; PanGloss `CHANGELOG.md`, `.github/workflows/release.yml`.

## Motif: the demonstrated workflow

The beta's seven main pages are Overview, Texts, Try a Word, Timing, Warnings, Review changes and
AI Handoff. A person stages Approved, Disapproved or Unknown Opinions one analysis at a time, adds
parser analyses, or removes a stored analysis. Review changes shows the pending effects; Apply is
explicit and refuses while FieldWorks owns the project. Trial and Dry Run use a copy, not the live project.

The week added opening a `.fwbackup` into a separate restored folder, paged Text/Word list reads,
shared word rows and cards, writing-system fonts and direction, editing shortcuts, Settings, a quieter
window frame, and more reliable Apply availability. Install/update/uninstall validation runs against
packaged applications without the SDK. These are usability and release-readiness changes, not changes
to the two-phase vision.

Sources: Motif beta001/beta002 notes; shared Help under `src/SIL.Motif.Help/Content/en/guide/`;
commits `a3cb84d91`, `a4c508ccf`, `5c46cd7bb`, `86c8a634c`, `3ff2cb858`, `2703c3c73`, `e8174018d`,
`6a8611f26`.

### Measured parallel throughput and scale

- Beta002 records 300 Mbugwe words on one 20-processor Windows machine: 1,608 s before, 424 s after,
  with the same analyses. One capped word still took 397 s. Motif caps parsing at half the processors;
  this is throughput evidence, not a general Rust-versus-C# speedup or the 50× Phase 1 target achieved.
- On a 22,948-wordform/129-Text test project, opening went from about 6 s to 2.5 s and reopening from
  4.5 s to 1.6 s. Reading 21,604 Word list rows went from 12 s to 9 s. Scrolling to the end of a
  224-line Text became slower (about 1 s to 2.3 s). Do not quote only the gains as universal guarantees.

Source: Motif `docs/release-notes/0.2.0-beta002.md`; commits `e83791a74`, `a4c508ccf`.

## AI assistance: Handoff and connected tools

Handoff remains a manual outbound folder. It contains the grammar, selected Texts, a helper reader and
orientation; measured results are present for an Assessment, and a word-specific Handoff can preserve
its displayed diagnostic without re-parsing. Current names include `parse-results.json` and
`read_results.py`, replacing the older fact sheet's names. Motif sends no Handoff files itself; a person
chooses what to share. Avoid a fixed five-file claim for all modes.

Advanced AI mode is opt-in. While off, MCP exposes no tools. A connected assistant names a Known
project per call; it cannot add projects or Apply. Tools come from the typed command catalog. It can
draft supported semantic changes, Dry Run and Trial a Draft before Finalize, read a paged Difference,
revise, then Finalize for a person's review. Evidence is bound to the exact content and Baseline;
editing or changing the project makes earlier evidence need another try.

Claude Desktop, Claude Code and Codex connection/package paths and five expert/workflow skills exist
in source. Packaging and tool-shape tests do not prove that every installed assistant integration has
passed the manual client workflow. The A/B evaluation harness now distinguishes agent failures, cloud
failures and harness defects, grades Proposal effects by independent read-back, and includes failed
Episodes in Price to solve. This is evaluation machinery, not published evidence that AI can build a
working grammar in weeks.

Sources: Help `guide/ai-handoff.md`, `guide/ai-assistants.md`; `docs/current-architecture.md`;
ADRs 0058/0059; `plugin/CONTEXT.md`; `docs/agent-plugin-release-smoke.md`; commits `602fecffe`, `12fa14efa`.

## Proposal review: infrastructure and remaining window work

Current source has Trial records, named evidence sets, command queries for a Proposal's evidence,
Trial freshness and retry handling, and word-card staging of Approved or expected parses into a Proposal.
These effects are measured on the Proposal's scratch project and applied with its other changes.

Do not claim that every page already presents today beside the Proposal. At the reviewed ref,
`WorkspaceContext.OpenProposalAsync` has no app call site; Overview, Timing and Warnings still read
`lens.Today` or issue project-only queries. The full navigation and paired window display remain work
ahead. Proposal authoring covers supported composers, not arbitrary properties or the whole grammar
surface. The FieldWorks save-boundary integration contract is specified, not evidence of a shipped
FieldWorks integration.

Sources: ADR 0061; `src/SIL.Motif.App/ViewModels/WorkspaceContext.cs`, `OverviewPageModel.cs`,
`TimingPageModel.cs`, `WarningsPageModel.cs`; Help `guide/change-an-analysis.md`; commits `ca07aa36e`,
`a709b96bf`; `AGENTS.md` FieldWorks integration contract.

## Parsimony: advisory findings, not a grammar score

Current source reads PanGloss grammar facts and project/parser evidence to report grammar tidiness:
duplicate statements, slot structure, broad allomorph/environment conditions, and alternates that a
sound rule might replace. Findings name their unit, scope, evidence tier and limitations. Parsimony
(fewer/less duplicated statements) and Restrictiveness (narrow statements preserving valid data and
limiting forbidden readings) remain distinct; no combined score decides correctness.

The optional Parsimony page reads the latest stored Report without starting a check. Keep, Ask and
Defer stage Notebook decisions for a person's Apply. Suppressed decisions can be revisited, and relevant
evidence changes bring a finding back. Fix is a guide note, not an automatic repair button; unsupported
updates say so. Reports do not grant Readiness or gate Apply. Repeated requests reuse Baseline evidence
and run only the requested measure.

Sources: ADR 0060; `docs/parsimony-evidence-contract.md`; Help `guide/parsimony-findings.md` and
`guide/parsimony/`; commits `602fecffe`, `7747c392d`, `9beb760ba`, `e281ceee8`.

## PanGloss: compatibility and useful diagnostics

Compatibility is behavioral conformance with tracked divergences, not universal source or output
identity. Recent releases improve named narrowing-rule, bare word-edge and right-to-left rule cases;
some fixes address defects also present in C# HermitCrab and are proposed upstream. XAMPLE is a
separate comparison oracle reached through FieldWorks, not an input grammar format or proof that
PanGloss reproduces every XAMPLE interpretation.

Underdefined phonology can run with reported provisional definitions; fatal/malformed source problems
are refused. Grammar facts/stats now use schema v8, exposing compiled identities, allomorph gate facts,
resolved environments and statement references. The README's remaining v7 sentence is stale relative
to the format contract and changelog.

Grammar health can attribute a stored analysis no longer produced to an authored phonological rule.
Version 0.8.3 reports capped comparisons individually and work not reached under its deterministic
budget, continuing the report. Parallel workers fold results in wordform order. Its changelog records
4.4× faster Amharic, 3.0× Indonesian and 1.4× Sena checks; peak memory rises. These are grammar-check
measurements, not batch-parse factors. Import drops redundant/quadratic provenance work with unchanged
snapshot output. A separate facts-file transaction change records 423→17 ms Windows and 723→22 ms Linux;
those times concern artifact writes alone.

Sources: PanGloss `CHANGELOG.md`, `docs/grammar-diagnostics.md`, `docs/grammar-facts-format.md`,
`docs/divergences/083-*`, `087-*`, `088-*`, `089-*`; commits `51599ba9`, `99095595`, `03bb031d`,
`e764c789`, `2902640e`, `44ad2248`.

## Speed ambitions and deployment still ahead

The 50× Phase 1 figure is a target. Near-instant parsing remains a Phase 2 goal. October FST work
expands support for repeated alpha environments, ambiguous disagreement and partial explicit classes,
and records capability evidence/refusals. It does not supply a new cross-language performance matrix.
The July 26 matrix refuses the three reference grammars by default; its Indonesian developer-only
11× result on 121 words is uncertified. Do not present that historical admission result as a fresh
October check, or support for more compiler rule shapes as general production readiness.

Native CLI releases are public. C ABI, WebAssembly and Language Pack source do not establish deployment
in FieldWorks, Paratext, browsers or keyboards. WASM consumes prebuilt packages rather than compiling
grammars. Spell correction, general prediction, multilingual natural-phrase back-translation and a
shared grammar library remain future work. Feature coverage remains a separate design; Parsimony and
grammar-health findings do not prove it has shipped.

Sources: PanGloss `docs/benchmark-matrix.md`, `CHANGELOG.md`, `docs/spell-checking-plan.md`,
`docs/natural-phrases-plan.md`, `rust/README.md`; Motif `docs/grammar-coverage-design.md`, `CONTEXT.md`.

## Engineering work behind the week

Motif added worktree build locks/process cleanup, private writing-system repositories and scratch
roots, explicit incomplete-search behavior, platform line-ending consistency and narrower long-running
screenshot/sample tests. PanGloss improved managed lint/build/release gates, Windows file-identity
handling and process cleanup. These strengthen repeatable validation; they are not fresh evidence that
all product, real-parser, screenshot or installed-assistant checks passed for this pitch revision.

Sources: Motif `AGENTS.md`, commits `635e3f3d7`, `4c79d4976`, `9eb4428b3`, `91d20f9ba`;
PanGloss `AGENTS.md`, `CHANGELOG.md`, `rust/tools/pg.ps1`.

## Earlier speed measurements

These numbers come from different machines, grammars, word sets, and parser modes. Keep each with its method and date. Most are research measurements, not a product-wide performance guarantee.

### (a) PanGloss Rust versus C# HermitCrab

- **Amharic, one word, same machine:** the O2 report gives Rust at about 145 seconds before a hash-backed deduplication fix and 54.3 seconds after it, against a C# oracle at about 64.0 seconds. That is about 1.18× faster than C# for that one word after the fix, after being slower before it. The fix landed 2026-07-10; the report does not give a separate date for the original timing run. The Rust run used a release build, one thread, and three repetitions; the C# command is only partly recorded. (PanGloss rust/docs/o2-profile-findings.md:3–11, 17–18, 81–103.)
- For another Amharic word, Rust fell from about 303 seconds to 53 seconds after the same fix. No fresh C# run was made because the historical C# run had not completed. A 341-to-346 ms control stayed essentially flat. Do not turn these two difficult words into a cross-language average. (PanGloss rust/docs/o2-profile-findings.md:7–12, 110–127, 190–203.)

### (b) PanGloss optimizations versus PanGloss baselines

- **Analysis file-system port, 2026-09-10:** on 199 common words across five languages, the Rust branch ranged from 0.42× to 6.0× relative to its baseline; the reported parse result sets were identical. Indonesian was slower/noisy, while Mbugwe was 2.8× and Aweti 5.9× faster. (PanGloss docs/research/2026-09-10-hc-analysis-fs-port-measurements.md:1–7, 19–41, 43–59, 68–75.)
- **Exact analysis file-system mode, 2026-09-11:** on another 199-word A/B, reported speed ranged from 0.78× to 3.56× with identical completed parse sets and two status gains. Exact mode intentionally finds cases the C# master loses, so this is not simply a speed-only comparison with identical semantics in every case. (PanGloss docs/research/2026-09-11-exact-analysis-fs-measurements.md:1–5, 15–39, 41–57.)
- **Hash-backed deduplication, fix dated 2026-07-10:** the one-word Amharic runs improved from about 145 to 54.3 seconds and from about 303 to 53 seconds; the recorded distinct-scan time fell from 86,633 to 488 ms and from 235,057 to 414 ms. Signatures and input-side counters remained identical. The small sample makes this a credible local optimization result, not a language-wide speed estimate. (PanGloss rust/docs/o2-profile-findings.md:3–12.)

### (c) FST propose-and-confirm versus full HermitCrab search

- **2026-08-06 Motif measurement:** on a 1 MB grammar, grammar load was about 0.1 seconds including process startup. Full HermitCrab reanalysis measured 1 ms/word on Indonesian, 151 ms/word on a mid-profile grammar, and 1,327 ms/word on Amharic. At 6,973 wordforms, the estimates were about 7.5 minutes for the mid-profile grammar and 29 minutes for Amharic, with roughly one sixth of Amharic words abandoned at a five-second cap. (Motif docs/plan-cross-repo.md:67–74.)
- The same 2026-08-06 note reports one-off FST compilation of 0.13 seconds for Indonesian, 4.9 seconds for Amharic, and 12.1 seconds for the mid-profile grammar. The FST-confirm path was measured at 12–19× faster with no timeouts in that experiment; the 6,973-word runs were about 16 seconds for mid-profile and 29 seconds for Amharic. On 40 mid-profile words, full HermitCrab took 2.3 seconds and FST propose-and-confirm took 12.3 seconds because compilation did not amortize; the estimated crossover was about 87 words mid-profile and four words for Amharic. PanGloss compare reported matching outcomes on those 40 words. (Motif docs/plan-cross-repo.md:75–97.)
- **Earlier FST-plan measurements, 2026-07-15:** Indonesian, 121 words, 505 to 60 ms; Sena, 300 words, 106.5 to 4.52 seconds; Sena full-FST, 7,121 words, 125.9 seconds while the HermitCrab leg stopped at about 4,400 words; Amharic, 673 words, 1,000.2 to 20.7 seconds with 54 default timeouts. Seven Sena single-word speed ratios ranged from 12.6× to 1,215×, and one word had a signature mismatch. This was a noisy single-thread release run, not a reliable headline. (PanGloss docs/fst-plan/foma-fst-plan.md:292–339.)
- **PanGloss README timings, 2026-07-17:** the table reports FST compile/per-word figures of about 85 ms/0.7 ms for Indonesian, 225 ms/29 ms for Sena, and 5.7 seconds/41 ms for Amharic; the Amharic median was reported near 1 μs with a heavy tail. Those figures lack the capability-certification detail supplied by the later matrix. (PanGloss README.md:167–172.)
- **July 26 capability check:** that historical benchmark matrix says the optimized FST refuses all three reference grammars under default capability enforcement because of MprGroupOverwrite. Indonesian alone was force-compiled with the development-only --allow-unproven flag: 121/121 signatures matched, about 11× total speed and 5× p95/worst-case speed. The matrix explicitly does not certify that as production-publishable; it reports no corresponding Amharic or Sena speed result. Thus none of the older 12–19× or 11× figures is a defensible general current-product headline. (PanGloss docs/benchmark-matrix.md:1–15, 24–110, 114–150.)
