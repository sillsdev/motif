# Website home page, Learn track and sample languages — architecture and plan

**In plain terms:** the website gets a proper front page that says what Motif is and shows it working, a
"Learn" track that teaches how to read Motif's analysis and fix a grammar in FieldWorks, and three small
downloadable sample languages to practise on. Each sample comes in a broken and a fixed version: the broken
one has a few realistic grammar mistakes planted in it, and each Learn lesson finds and fixes one. Motif does
not replace FieldWorks; it measures the grammar, explains why words fail, and the fix is made in FieldWorks.
The same samples later become the data every screenshot and clip shows, so the whole site tells one story.

Companion to ADR 0047 and `2026-09-26-docs-content-plan.md`. Owner decisions of 2026-09-26: the home page
below; "works with FieldWorks and FLExTrans" with links; three samples simplified from real languages and
labelled as teaching grammars; planted bugs; the conformance grammars kept for the developer section only;
architecture first, then parallel worktrees.

## 1. How the pieces fit

```
samples/<id>/sample.json ─┐                         ┌─> bin/<Config>/samples/<id>-fixed.fwbackup ──┐
samples/<id>/bugs.json ───┼─> sample builder ───────┤                                              ├─> site: Samples page + downloads
                          │   (LibLCM, net10)        └─> bin/<Config>/samples/<id>-broken.fwbackup ─┘
                          │                                      │
                          │                 real PanGloss via `motif assess` (gated test)
                          │                                      │
                          └──────────────── samples/<id>/expected.json <── pinned by the test
                                                                 │
help/en/guide/learn/*.md  <── lessons quote bugs.json and expected.json
site home page ───────────── links Guide, Learn, Samples; clip slots filled later by Walkthroughs
Walkthroughs (later) ─────── run the window over the samples' projects
```

## 2. Decisions

**D1. Samples are built by a Motif-owned builder from a checked-in spec, not hand-made in FieldWorks and not
through PanGloss's `xample-projector author`.** `author` refuses phonological rules, infixes and
reduplication — exactly what the samples teach — and needs an installed FieldWorks. A hand-made `.fwdata` is a
binary no reviewer can read. Motif already pins LibLCM and builds real projects in its tests
(`NewLangProjFixture`, `RealParserProject`), and PanGloss's own importer (`pg-fwdata`) reads phonological rules,
infixes and `.fwbackup` files, so a LibLCM-built project is what the parser and FieldWorks both consume.

**D2. Generated projects are never committed.** The spec, the bug list and the pinned expectations are; the
build produces the `.fwdata`. No `.gitignore` carve-out is needed.

**D3. Identity is deterministic.** Every object's GUID is a name-based UUID (version 5) of
`<sampleId>/<objectKey>`, formatted textually, never through `Guid.ToByteArray()` (design rule 9). Two builds
of one spec produce the same GUIDs, so expectations, lessons and later screenshots stay stable.

**D4. The broken variant is the fixed spec plus the bugs' patches.** One source of truth; a bug is a named,
reviewable change, never a second copy of the grammar.

**D5. Correctness is pinned by the real parser, and the pin is checked in.** A test gated like
`RealParserFactAttribute` builds both variants, runs `motif assess` with the real PanGloss over each sample's
Texts, and requires: the fixed variant parses every word; the broken variant fails exactly the words
`bugs.json` declares, for the reasons it declares. `expected.json` records the numbers (word counts, Text
Coverage, failing words per bug); `MOTIF_SAMPLES_UPDATE_EXPECTED=1` rewrites it. Without a parser the test
skips and a non-gated test still proves each spec builds, each patch applies, and the project reopens.

**D6. Downloads are `.fwbackup` files**, the format FieldWorks restores and PanGloss reads.

**D7. The home page is site-owned** (`site/src/content/docs/index.mdx` with Starlight's splash layout and site
components), because it is layout as much as prose. Its strings live where Starlight localizes them, so it
translates like the rest. Every other page's words stay in `help/<lang>/`.

**D8. Walkthroughs will use the samples.** When Package B resumes, its fixture is `sample-turkish` (broken, then
fixed), and CI renders clips from recorded Assessment evidence captured from real runs, so clips need no parser
in CI. Recorded here so nothing built now forecloses it.

## 3. Interfaces (binding)

### Sample metadata — the part the site reads

`samples/<id>/sample.json` top level, whatever else the builder's schema adds below it:

```json
{ "id": "sample-turkish", "title": "Turkish (teaching sample)",
  "language": { "name": "Turkish", "tag": "<BCP 47 tag the builder can resolve offline>" },
  "teaches": ["suffix slots in order", "vowel harmony"],
  "summary": "One or two sentences for the Samples page.",
  "disclaimer": "A simplified teaching grammar for learning Motif; not a description of Turkish." }
```

`samples/sample.schema.json` validates the whole file; the builder owns everything below `disclaimer`.

### Bugs and expectations

`samples/<id>/bugs.json`: an array of
`{ "id": "<kebab-case>", "title": "...", "symptom": { "words": ["..."], "reason": "what Try a Word says" },
"fix": ["FieldWorks step", "..."], "patch": [ builder operations ] }`.

`samples/<id>/expected.json`: `{ "fixed": { "words": N, "parsed": N, "textCoverage": 1.0 },
"broken": { "words": N, "parsed": N, "textCoverage": 0.x, "failing": { "<word>": "<bug id>" } } }`.

### Build command and output

`tools/Build-Samples.ps1 [-Configuration Debug] [-Sample <id>]` builds after `./build.ps1` and writes

```
bin/<Config>/samples/<id>/fixed/<ProjectName>/<ProjectName>.fwdata (+ WritingSystemStore/)
bin/<Config>/samples/<id>/broken/<ProjectName>Broken/...
bin/<Config>/samples/<id>-fixed.fwbackup
bin/<Config>/samples/<id>-broken.fwbackup
```

### Site

`site/scripts/sync.mjs` gains `--samples-root` (default `../samples`) and `--samples-out` (default
`../bin/Debug/samples`, falling back to `site/fixtures/samples`). It generates the Samples page from every
`sample.json`, copies any built `.fwbackup` into the site's public downloads, and shows "available in release
builds" for one that is missing. Learn lessons are Guide pages under `help/<lang>/guide/learn/<slug>.md`.

### Lessons

One per bug: slug `learn/<sample short name>-<bug id>`, e.g. `learn/turkish-plural-harmony`. Each opens the
broken sample, reads the Overview and Text Coverage, uses Try a Word on a failing word from `bugs.json`, makes
the fix in FieldWorks step by step, then Refresh. Numbers quoted come from `expected.json`.

## 4. Samples

| Id | Teaches | Planted bugs, for example |
| --- | --- | --- |
| `sample-turkish` | suffix slots in order; vowel harmony | a harmony environment missing on one allomorph; two slots swapped; a stem missing from the lexicon |
| `sample-swahili` | noun-class prefixes; verb agreement slots in a template | a class prefix in the wrong slot; a missing class allomorph; an agreement prefix restricted to the wrong class |
| `sample-tagalog` | infixation and reduplication | an infix with the wrong environment; a reduplication pattern copying the wrong segments |

A few dozen genuine, common words each; two or three short constructed Texts; every page and each project's
own description carries the disclaimer. Never use a real person's FieldWorks project as a sample.

## 5. Risks, proven before anything depends on them

Package S1's first commits are spikes that must pass before it writes a whole sample:

1. **The parser accepts a builder-made project**: a ten-word spec parses under the real PanGloss through
   `motif assess`.
2. **FieldWorks opens it**: the `.fwdata` model version equals what current FieldWorks writes (`7000072`, as in
   the conformance fixture), and the `.fwbackup` restores. The owner confirms the restore by hand once.
3. **Writing systems resolve offline**: the chosen tags need no network at build time.
4. **Tagalog's constructs parse** (infix, reduplication) — spiked at the start of S3; if one does not, S3
   swaps it for another feature and says so rather than shipping a sample that fails for the wrong reason.

## 6. Packages, order and ownership

Each package works in its own worktree off `gh/integrate` and edits only the paths it owns, so merges are
mechanical.

| Phase | Package | Branch | Owns |
| --- | --- | --- | --- |
| 1 | S1 builder + `sample-turkish` | `gh/samples` | `samples/**`, `tools/SampleProjects/**`, `tools/Build-Samples.ps1`, sample tests in `tests/SIL.Motif.Tests.LibLcm/Samples/`, `Motif.sln` entry |
| 1 | H home page, Samples and Learn wiring, Developers correctness page | `gh/home` | `site/**` |
| 1 | F code fixes | `gh/fixes` | the files each fix touches, their tests, the matching `docs/cli-api.md` rows |
| 2 | S2 `sample-swahili` | `gh/sample-swahili` | `samples/sample-swahili/**` (builder changes only if S1's format cannot express it, reported first) |
| 2 | S3 `sample-tagalog` | `gh/sample-tagalog` | `samples/sample-tagalog/**`, same rule |
| 2 | L Learn lessons for `sample-turkish` | `gh/learn` | `help/en/guide/learn/**` |
| 3 | L continued for the other two samples; merge; CI job for samples and site | | |

**Package F** — five small fixes, each test-first:
1. `motif help` exits 1, not 2, on a malformed call (unknown names stay 2).
2. The `handoff` usage line names `--texts`, accepted with `--no-assess`.
3. The `dry-run --wait` and `trial --wait` usage lines name `--wait-timeout-ms`.
4. The window's AI Handoff for selected words sends a request the command refuses for lacking an invocation;
   make that path work or remove it, whichever the code shows was intended.
5. The setup dialog says unlimited steps let parsing run until it finishes, but a per-word time limit still
   applies; say so.

**Gates for every package:** `./build.ps1` and `./test.ps1` green (in a sandbox, CLI failures that say "could
not be recorded" are the sandbox, proven by rerunning the CLI project with a worktree-local
`MOTIF_WORKER_ROOT`); `cd site; npm test; npm run build` for H; for S packages, the gated real-parser test run
locally with `MOTIF_PANGLOSS_EXE` naming a real `pangloss.exe`
(on the owner's machine, `C:\Users\johnm\Documents\repos\pangloss-releases\v0.3.0\pangloss.exe`).
