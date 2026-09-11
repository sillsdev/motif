# Shared ordered parse morphology

People must be able to tell whether the parser reproduced every approved reading of a word, even when its search did not finish. PanGloss and Motif will exchange the ordered morphology FieldWorks already compares, using source GUIDs across the process boundary.

**Status: shared format implemented and verified in both task worktrees. Broader parser compatibility remains INCOMPLETE.** The coverage limits below remain explicit; a word whose search hits a limit is always incomplete, regardless of approved matches.

**Goal:** preserve selected allomorph, MSA, optional inflection type and guessed text from the parser; compare all approved expectations without confusing findings with search completion.

**Architecture:** the parser owns projection from runtime morphology into `ParseAnalysis` / `ParseMorph`. A versioned JSONL batch sidecar carries that projection from the same search that produces the existing conformance TSV. Motif validates and retains the sidecar, then applies FieldWorks morphology matching. Conformance remains exact multiset equality; approved expectations use positive subset matching.

**Tech stack:** Rust grammar compiler, parser and CLI; C# Motif Host, Worker SQLite storage, shared command responses and tests.

## Wire contract

Each requested case has its own row, including repeated surface words. Limits stop the search for that case without suppressing partial findings or preventing later cases.

`batch <grammar> <words.txt> <out.tsv> --analyses <analyses.jsonl>` writes one closed JSON object per finished case:

```json
{"schema":"fieldworks-parse-analysis/v1","index":0,"word":"example","elapsedMs":12,"capped":false,"timedOut":false,"invalidShape":false,"analyses":[{"morphs":[{"form":"11111111-1111-1111-1111-111111111111","msa":"22222222-2222-2222-2222-222222222222","inflType":null,"guessedString":null}]}],"unavailable":[]}
```

`form` is the selected source MoForm GUID, not its text; `msa` is its source MoMorphSynAnalysis GUID. GUIDs use lowercase hyphenated textual/network order. Arrays retain morphology order and analysis multiplicity. `unavailable` contains a reason for each result which cannot be projected authoritatively. A null optional inflection type is absence, never a wildcard. Guessed text has FieldWorks' conditional comparison semantics. Missing source identities are explicit unavailable evidence, not an empty parse result. Empty search results are represented by empty analyses and unavailable arrays.

Both limit flags can be true. Neither finding an analysis nor matching all expectations changes either flag. Rows must match exact requested indices and words; missing, duplicated, malformed, unknown-version and inconsistent artifacts are refused. The sidecar uses the same invocation's outcome, never a second parse. Initial sidecar mode refuses resume (`--start`) to avoid mixing invocations.

## Implementation and verification

Every part must be exercised before this work is called complete. A producer-only fixture or one reproduced reading does not satisfy the acceptance criteria.

1. Preserve source Form/MSA/InflType and circumfix halves through grammar compilation and runtime records; project in pg-parse. Test ordinary allomorph selection, variants, circumfix ordering, guessed behavior and unsupported source metadata.
2. Emit the JSONL sidecar from sequential and parallel batch outcomes. Update CLI surface documentation. Pin format and partial CAP/timeout behavior with tests; retain the existing TSV default for conformance.
3. Validate the closed contract in Motif. Request, hash and retain exact sidecar bytes beside source/words/TSV/stderr. Preserve structured morphology and per-word flags through persistence and command responses.
4. Read every human-approved analysis from the same retained source. Compare ordered Form/MSA/InflType and conditional guessed text; require every distinct approved morphology. Keep unsupported, missing and unmatched evidence separate from completed search. Store comparison evidence for later reports rather than consulting changed live data.
5. Test multiple approved readings, extra parser readings, wrong same-owner allomorph, wrong MSA/InflType/order, duplicate surfaces, no expectation, unavailable projection, partial matches under limits, mixed later completed cases, malformed artifacts and persisted read-back.
6. Use independent FieldWorks/C# evidence for identity expectations, never PanGloss output as its own oracle. Run managed PanGloss check/targeted tests/full affected gates and Motif `./test.ps1`, inspect all delegated changes, and record exact results and remaining gaps here.

No migration readers or guessed identity reconstruction will be introduced. Stored schema changes refuse obsolete stores with the existing delete/recreate instruction.

## Verification evidence

The implementation preserves what was actually found and says where comparison remains unavailable. Passing the supported fixtures does not establish complete parser compatibility.

All commands ran in their task worktree. PanGloss used `./rust/tools/pg.ps1`; Motif used `./test.ps1` with the newly built PanGloss executable.

| Gate | Executed / passed | Skipped or ignored | Result |
| --- | ---: | ---: | --- |
| PanGloss workspace `-Mode check` | All targets | — | Passed |
| PanGloss `-Mode test -Package pg-grammar -NoNextest` | 93 | 10 | Passed |
| PanGloss `-Mode test -Package pg-memo -NoNextest` | 8 | 0 | Passed |
| PanGloss `-Mode test -Package pg-rules -NoNextest` | 162 | 8 | Passed |
| PanGloss `-Mode test -Package pg-parse -NoNextest` | 178 | 43 | Passed |
| PanGloss `-Mode test -Package pg-cli -NoNextest` | 136 | 12 | Passed |
| Motif full build/comment/test gate against that executable | 1,651 | 19 | Passed |

PanGloss ignored cases include optional local corpora and diagnostic/oracle tests; this is not a claim that those corpora were run. Motif skips cover retired parser routes and permission-dependent filesystem cases. The supported real-parser cases executed. Both comment gates are clean, both diffs pass whitespace checks, and the documented JSON example validates against the schema.

The tested executable is `G:\cargo-build-cache\pangloss-shared-parse-morph\pg-test-opt\pangloss.exe`, SHA-256 `30dc1f85c6c5755ef3b684f1a2ebd0f06177a6fc88825715aa3a244b33dcb022`. Set `MOTIF_PANGLOSS_EXE` to that path to exercise this uncommitted implementation. PanGloss is based on `19554f63ed1fdbe9e7243f473ef430f7d053473a`, branch `feat/shared-parse-morph`; Motif is on `feat/ai-handoff`.

Verified identity safeguards include same-owner root and affix allomorph distinction; exact ordered Form/MSA trails; variant and circumfix compiler metadata; ordinary, infix, circumfix, null-affix, and guessed-source projection rules; and source-row alignment after dropping an earlier unreachable affix. The two-stage homophonous fixture compares exact structured results with memo enabled and disabled. Positive memo replay includes source morphology; procedural passed-over indices stay out of the key. Independent xhigh Sol source review found no remaining blockers for this supported scope.

Motif tests cover every approved reading, extra parser readings, wrong allomorph/MSA/order/inflection type/guessed text, malformed artifacts, retained bytes and hashes, frozen expectations, persisted partial findings, both limits with all expectations matched, regression of one reading in an already partly unmatched word, and readiness refusal after incomplete parsing. A real LibLCM-to-PanGloss test reproduces two approved same-owner allomorph readings and preserves duplicate cases. A second real fixture independently authors an inflectional variant and verifies its selected variant Form, base MSA, and InflType GUIDs across the process boundary. Both populated and empty correctness measurements explicitly refuse the unsupported legacy analyses aggregate.

## Explicit coverage limits

Unavailable evidence stays visible so a person can distinguish an unsupported comparison from a word that the parser proved it could not reproduce. These limits do not turn partial search into completion.

- Source Form and MSA are required nonempty GUIDs even when guessed text is present. Runtime fabricated roots without authored identities are explicitly unavailable; guessed text cannot replace source references.
- Direct XML IDs never establish FieldWorks provenance, including GUID-shaped IDs. Trusted source metadata comes from `.fwdata` compilation.
- Batch cases select literal text in the source's default vernacular writing system. Every approved morphology of every wordform with that exact text is an obligation; frozen expectations retain the originating wordform GUID and writing system. Repeated cases remain separate rows.
- The richer shape follows FieldWorks `ParseAnalysis` / `ParseMorph`. Machine's older TSV harness remains unchanged and lossy; it has not adopted the new JSONL profile. Exact conformance and positive approved-reading matching remain different comparisons.
- `motif analyses --assessment` has no aggregate projection for this evidence yet. It returns `assessment.aggregate-unavailable` and directs the caller to the correctness report; raw evidence remains retained. No legacy analysis digests are invented.
- Circumfix and infix source projection have compiler/projection fixtures; this ledger does not claim a native process-boundary fixture for every grammar construct or complete real-language parser parity.
