# Three FieldWorks skills: research and recommended approach

Date: 2026-09-09. Research only; no skill was written. Three Sonnet researchers worked in parallel,
one per skill, plus one pass against the Claude Code plugin documentation. Their load-bearing claims
were re-checked directly against the repositories before being used here (see "What was verified").
The full reports are working notes, not repo documents; this file is what survives them.

## What the skills are for

| Skill | Question it answers | Kind of skill |
|---|---|---|
| `fieldworks-expert` | "How do I do X in FLEx? Where is it? Is it visible, hidden, possible, done in two or three places? What is it in the data model?" | lookup, over UI and LCModel |
| `fieldworks-parsing-expert` | "How do HermitCrab and XAmple work, what do I configure in FLEx to teach the parser my language, and why is this word parsing wrong, missing, ambiguous, or slow?" | lookup plus procedure |
| `linguistic-consultant` | "What does a linguist do, what words do they use, and does this screen or workflow meet the need of someone documenting a language, describing it, and checking it against text?" | judgment, plays the user |

Three skills, not one. They differ in kind, audience, trigger phrases and source currency. A merged
skill would blow the body budget and under-trigger for all three. The consultant is the evaluator that
consumes the other two: it frames the need and the persona, the other two supply the facts, and it
applies the checklist. Skills cannot literally call each other, so its SKILL.md tells the agent to load
the sibling before rendering a judgment and to say which facts came from where.

## Where they live and how they ship

Motif is the "AI for FieldWorks" repository, so the skills live here and the FieldWorks team installs
them once, globally. The repository moved from `johnml1135/motif` to **`sillsdev/motif`** on 2026-09-09
for exactly this reason; GitHub redirects the old name, including raw URLs. The supported mechanism is a **Claude Code plugin marketplace hosted in this
repo** ([plugin-marketplaces](https://code.claude.com/docs/en/plugin-marketplaces.md),
[plugins-reference](https://code.claude.com/docs/en/plugins-reference.md)):

```
motif/
  .claude-plugin/marketplace.json      # marketplace root = repo root; source: ./fieldworks
  fieldworks/                          # ONE plugin holding all three skills
    .claude-plugin/plugin.json
    README.md                          # install, what triggers each skill, update, contribute
    CONTEXT.md                         # the plugin's own glossary
    skills/
      fieldworks-expert/SKILL.md + references/
      fieldworks-parsing-expert/SKILL.md + references/   # ai-parser-help lands here, flat
      linguistic-consultant/SKILL.md + references/
    references/                        # the SHARED corpus (see next section)
  .claude/settings.json                # extraKnownMarketplaces + enabledPlugins, so a clone of
                                       # motif offers the plugin to whoever trusts the folder
```

Install is two commands, and updates flow from GitHub on marketplace refresh:

```
/plugin marketplace add sillsdev/motif
/plugin install fieldworks@motif
```

Facts that shape this layout:

- **A plugin cannot reference files outside its own directory.** Paths that resolve above the plugin
  root, including symlinks, are rejected with `path escapes plugin directory`. So anything a skill
  needs at runtime must sit inside `fieldworks/`. A corpus kept at `docs/ai-parser-help`
  would be invisible to the installed plugin.
- **Skills are namespaced by plugin name**: `fieldworks:fieldworks-expert`. The plugin name is
  therefore user-facing and carries trigger weight, which is why it was the first grill question.
- **Claude.ai web users are not served by this path.** They upload a zipped skill folder through the
  web UI. The same folders can be zipped for them, but that is a second, manual channel.
- Public precedent: `anthropics/skills` uses this exact shape (`.claude-plugin/` at root, `skills/`,
  Apache 2.0 for most skills).

## The corpus: distill, index, fetch. Never vendor the help.

Six corpora feed the skills. Each gets one of three treatments.

| Corpus | Size | Treatment | Why |
|---|---|---|---|
| `Docs/ai-parser-help` (currently in FieldWorks; moving to motif, not yet landed on any branch) | ~57K tokens of topics plus ~88K of Black primary text; 16 correctness gotchas, 10 speed gotchas, 8 workflow topics | **Ships inside the plugin** as the parser skill's backbone. Its raw URLs get rewritten to motif. | Already LLM-facing and current (last change 2026-08-19). Its own README's raw URLs point at `sillsdev/machine`, where the content never lived, so they 404 today. |
| FwHelps help as markdown, one file per topic (1,600 topics, FLEx 9.3) | 6.5 MB; the repo's README says ~759K tokens | **Index, never vendor.** Ship a one-line-per-topic index built from each topic's frontmatter (`breadcrumb`, `keywords`, `related`, `source_url`), grep it, then fetch the one topic needed by raw URL. | Too big to ship, and it already carries its own cross-reference graph in frontmatter. **It exists only on `johnml1135/FwHelps`, not `sillsdev/FwHelps`.** Every topic's `source_url` points at the official live HTML help at downloads.languagetechnology.org, which works as a fallback today. |
| FieldWorks source: `DistFiles/Language Explorer/Configuration/Parts/*.xml`, `*.fwlayout`, `areaConfiguration.xml`, `toolConfiguration.xml`, `DataTree.cs`, `HCLoader.cs`, `M3ToXAmpleTransformer.cs` | a few dozen files | **Distill the mechanism into authored reference files; teach lookup by raw URL** against `sillsdev/FieldWorks` at a pinned tag, with a local-clone fast path. | The "visible, invisible, possible" question is answered by the `visibility` attribute (`always`, `ifdata`, `never`) in `.fwlayout` files, read in `DataTree.cs` around line 2421, and by the fact that Show Hidden Fields skips the attribute entirely so even `never` fields appear. Nothing documents this today. |
| `MasterLCModel.xml` (193 classes, 898 fields, prose comments) | 5,368 lines | **Teach lookup.** Ship the path and a grep recipe, not the file. | Lives in the `SIL.LCModel` NuGet package and the liblcm repo; version-pinned. |
| motif's own docs (user-facing names, what a proper analysis is, hc-grammar-map, hc-surface-scope, ADRs 0027/0033/0038) | evidence-dense | **Distill into the skills' reference files**, with citations. | They are the distillation layer already; they map LCModel names to labels, engine constructs and linguist judgments. |
| Literature: Black 2025 and workshop L02, Moe *Introduction to Lexicography* (local copy revised 2025-08-22), Leipzig Glossing Rules, DDP/RWC, Payne, Bowern, Chelliah & de Reuse, Coward & Grimes | mixed | Black travels with ai-parser-help. Leipzig rules and abbreviations may be reproduced. Books get **summaries and URLs only**. | Copyright. SIL-authored help and training text: summarize and link, do not bulk-copy, pending a licensing call. |

**Retrieval, in the form that works in Claude Code.** Tier 1 is grep over shipped index files plus
on-demand reads, which is what the existing FieldWorks skills already do. Tier 2 is fetching one raw
URL per topic. Tier 3, a vector store behind an MCP server, is deferred until the skill-creator eval
loop shows the index approach missing real questions. Build the evals first: five realistic prompts
per skill, run with and without the skill, and compare.

**Estimated on-disk size** of the plugin without the vendored ai-parser-help: about 15K tokens for the
FLEx expert, 20K for the consultant, and the parser skill's authored files on top. All of it is
loaded on demand; only the three descriptions are always in context.

## What each skill contains

**fieldworks-expert.** SKILL.md carries the five areas, the three question shapes (location,
visibility, model mapping), a decision tree to its references, and a fifteen-line inline summary of
the visibility mechanism. References: field visibility (the three unrelated meanings of `visibility`
and the Show Hidden Fields caveat), areas-tools-views (why one field is editable from Lexicon Edit,
its browse grid, Browse, and Bulk Edit: browse columns are generated from the same Parts/Layouts),
data model lookup, custom fields, and the help-corpus query manual.

**fieldworks-parsing-expert.** SKILL.md carries the two-parsers overview, Black's build order
(phonemes, one paradigm, text, lexicon, template, choose parser, parse, read the colors, iterate),
and a diagnosis tree for wrong, missing, ambiguous, and slow. References: the vendored HermitCrab
gotchas and workflow, a new end-to-end concept map (FLEx label to LCModel class to HermitCrab
construct to XAmple analogue, which no source has today), a FLEx setup workflow drawn from the
Grammar-area help topics, Parser menu and debugging, and an honestly smaller XAmple/PC-PATR appendix.
Hard ceilings stated plainly: realizational rules and multiple strata are engine features no FLEx
project can reach.

**linguistic-consultant.** SKILL.md carries the plain-language answering contract already written in
ai-parser-help's README, a thirteen-task job map (setup and orthography, elicitation, word collection,
interlinearization, morphology, phonology, cross-checking against text, discourse, notebook, sketch,
publication, collaboration, archiving) with the FLEx area for each, five personas from mother-tongue
translator to computational evaluator, and sixteen evaluation heuristics. References: the job map
with the literature's five disagreements held as tension (texts-first vs paradigm-first; lexicon- vs
grammar-driven; depth vs breadth; two definitions of "the same analysis"; who judges "better"),
terminology crosswalk, personas and scenarios, evaluation checklist, annotated bibliography, and a
Black methodology digest that points at the parser skill for mechanics.

**One shared asset** all three cite instead of copying: a terminology crosswalk of linguist term,
FLEx label, LCModel class, and HermitCrab construct. It lives in the plugin's shared `references/`.

## Alignment with the AI handoff

The [AI handoff design](../superpowers/specs/2026-09-04-ai-handoff-design.md) (settled 2026-09-04) is
the other live track, and the two meet at three points.

- **Digest versus corpus.** The Handoff folder carries `reference/hc-mechanics.md`, a 137-line digest a
  chat model reads whole, kept at `docs/handoff/`. The parser skill carries the full ai-parser-help tree,
  about 145K tokens across 35 files, that a Claude Code agent greps. These are two artifacts with one
  source: the tree inside the plugin is the source of truth, and the digest is derived from it by hand
  and says so in its header. The design's sentence "the HC mechanics reference moved from FieldWorks'
  `Docs/ai-parser-help`" now reads: the digest is in `docs/handoff/`, the tree is in the plugin.
- **The parser skill reads Handoff folders.** Its "deep debugging" input is a Motif Handoff
  (`grammar.json`, `texts/*.flextext.json`, `read_handoff.py`, `recipes.md`), with GenerateHCConfig's XML
  as the older alternative. A Claude Code user with the plugin and a Handoff folder gets the same
  answers a ChatGPT user gets from the folder alone, plus the gotcha corpus.
- **One answering contract.** The Handoff's `instructions.md` section on how to answer for a linguist
  and the consultant skill's plain-language contract are the same prose, lifted from ai-parser-help's
  README. Keep one source and copy from it; do not let the two drift.

The Handoff's `instructions.md` raw URLs still name `johnml1135/motif`; the redirect covers them, and
the handoff track should switch them to `sillsdev/motif` at its next touch.

## Boundaries between the three

- The FLEx expert owns *what FLEx does, where, and how it maps to the model*. It names
  `MoMorphSynAnalysis` and says where an MSA is edited; it does not explain how the parser uses it.
- The parser expert owns engine behaviour, Grammar-area authoring, and diagnosis. It cedes the
  Lexicon and Texts areas beyond what feeds the parser, and the FLExText corpus material.
- The consultant owns the need, the persona, the terminology and the verdict. It may name a FLEx
  area or help topic but must not keep its own map of the UI. Where a question mixes "how do I" with
  "should I", the expert answers the how and defers the should.

## Prerequisites and risks

1. **ai-parser-help must land in motif inside the plugin directory**, not under `docs/`, or the
   installed plugin cannot see it. Its raw URLs get rewritten at the same time.
2. **The help markdown export has no official home.** Until `markdown-export` is on
   `sillsdev/FwHelps`, canonical raw URLs are impossible and the index falls back to the live HTML
   help URLs it already carries.
3. **Licensing posture is undecided**: quote short attributed excerpts of SIL help and training
   text, or summarize and link only. Black's texts are already public in the FieldWorks repo.
4. **Version drift**: the help is FLEx 9.3; the FieldWorks source describes the WinForms UI while an
   Avalonia UI is being built; `MasterLCModel.xml` is pinned per package version. Each reference
   file should state the version it describes, and the exporter's `markdown-export/<tag>` convention
   gives a re-index cadence.
5. **Personas are inferred, not researched.** No user research exists in either repo. The usage
   statistics behind "linguists barely approve analyses" rest on two sampled projects.
6. **No real project data.** The machine repo root holds real project backups and word lists; the
   `samples/` grammars are real languages. Worked examples come from the conformance suite's
   synthetic edge cases or are invented.

## Decisions taken in the grill (2026-09-09)

- **Plugin `fieldworks`, marketplace `motif`.** Skills appear as `fieldworks:fieldworks-expert`,
  `fieldworks:fieldworks-parsing-expert`, `fieldworks:linguistic-consultant`. "Motif" keeps meaning the
  CLI and the repo only.
- **The help index points at `sillsdev/FwHelps`, branch `markdown-export`.** The export is merged
  upstream before the FLEx expert depends on it; the personal fork is never a canonical home. Until the
  merge lands, the FLEx expert is blocked on this and no other decision.
- **Summarize and link only.** No verbatim SIL help or training prose inside the plugin. Black's two
  texts travel with ai-parser-help as they already do; every other source is paraphrased with its URL.
  The Leipzig rules and abbreviation list may be reproduced. Books get summaries only.
- **The consultant ships all five personas and serves both callers**: the FieldWorks UI review (the
  translator, graduate student and lexicography consultant lead) and motif's own reports and handoff
  prompts (the morphologist and computational evaluator lead). Scenarios are seeded for both.
- **The FLEx expert describes the shipped UI only, version-stamped.** Every reference file names the
  FieldWorks version it describes (9.3, WinForms today). The Avalonia port is owned by the migration
  skills in the FieldWorks repo until a release ships it; then the reference files are revised.

- **The parser skill names PanGloss and Motif in one bounded section** on measuring and experimenting
  at scale, including PanGloss's three modes and the rule that only FST pruned by HermitCrab answers
  whether rules apply properly. Everything else in the skill serves a user with only FLEx installed.
- **XAmple is a small, honest appendix**: choosing between the parsers, the pipeline shape, PC-PATR word
  grammar basics, no phonological rules, and a plain statement that no XAmple gotcha corpus exists.
- **Claude.ai users get zips per release.** A CI step zips each skill folder and attaches it to the
  GitHub release; the README documents the upload. Each skill therefore stays self-contained enough
  to work as a zip, which constrains how the shared `references/` folder is used (see below).
- **The plugin has its own glossary**, `fieldworks/CONTEXT.md`, a bounded context of its own.
  The root `CONTEXT.md` carries one pointer line. Proposal and Assessment vocabulary stays out of it.
- **Motif moves to `sillsdev/motif`** (done 2026-09-09) so the marketplace, the Handoff's raw URLs and
  the ai-parser-help URLs all have a canonical organisation home.
- **The full ai-parser-help tree lives inside the plugin and is the source of truth**; the Handoff's
  `docs/handoff/hc-mechanics.md` is a digest derived from it. No mirrored copy, no CI diff guard.

## Consequences the decisions force

- **Shared references versus zippable skills.** A zipped skill folder cannot see
  `fieldworks/references/`. So the shared terminology crosswalk is either duplicated into each
  skill's own `references/` at build time by the same CI step that zips, or each skill carries a short
  local copy and the shared file is the source of truth. Decide at authoring time; do not hand-maintain
  three copies.
- **ai-parser-help lands at `fieldworks/skills/fieldworks-parsing-expert/references/`**, flat (`broken/`,
  `speed/`, `workflow/`), rather than in the shared folder or under a `hermitcrab/` level: the parser
  skill is the only consumer, the zip must carry it, and the path is short enough to type.
- **Version stamps everywhere.** Each reference file opens with the FieldWorks version, the help
  branch tag, and the `SIL.LCModel` package version it describes.

## ADR candidates

Decisions here that are hard to reverse and surprising without the reasoning. Not written as ADRs
yet; the repo's convention is `docs/adr/`.

1. Motif publishes FieldWorks skills as a Claude Code plugin marketplace; the plugin is `fieldworks`,
   the marketplace `motif`. Hard to rename once installed anywhere.
2. The help corpus is indexed and fetched, never vendored, and its canonical home is
   `sillsdev/FwHelps` `markdown-export`. This makes an upstream branch a dependency of a shipped skill.
3. Summarize-and-link is the licensing rule for SIL prose inside the plugin.

## Next steps

1. Merge `markdown-export` (and its exporter tooling) from `johnml1135/FwHelps` to `sillsdev/FwHelps`.
2. Move `Docs/ai-parser-help` from FieldWorks into the parser skill's references, rewrite its raw
   URLs to `sillsdev/motif`, add a derived-from header to `docs/handoff/hc-mechanics.md`, and leave a
   pointer in FieldWorks.
3. Scaffold `fieldworks/` with `plugin.json`, `README.md`, `CONTEXT.md`, and the root
   `.claude-plugin/marketplace.json`; validate with `claude plugin validate`.
4. Author the three skills against this note, one at a time, each followed by the skill-creator eval
   loop (five realistic prompts, with and without the skill) before the next begins.
5. Add the release CI step that zips each skill folder.
6. On the handoff track's next touch, switch `instructions.md` raw URLs to `sillsdev/motif`.

## What was verified directly, and what was not

Verified against the repos in this session: `sillsdev/FwHelps` has no `markdown-export` branch and
`johnml1135/FwHelps` does; `DataTree.cs` reads `visibility` at lines 2421 to 2427 and skips it when
Show Hidden Fields is on; `MasterLCModel.xml` is in the local NuGet cache for `sil.lcmodel`
11.0.0-beta0135 through beta0150; `HCLoader.cs` and `M3ToXAmpleTransformer.cs` exist in
`Src/LexText/ParserCore`; ai-parser-help's last commit is 2026-08-19 and both Black sources are in
`workflow/sources`; its README has the "How to answer: plain language, for a linguist" section; Moe's
lexicography introduction is attributed and revised 2025; ai-parser-help is on no motif branch; the
plugin-marketplace and plugins-reference docs say what this file says they say; `docs/handoff/` on
`feat/ai-handoff` holds three reference files of 84, 137 and 138 lines; the repository transfer to
`sillsdev/motif` succeeded and the old name redirects.

Not verified: token counts (the 759K figure is the FwHelps README's own); every individual line
citation inside the three researcher reports; the online book URLs beyond the researchers' fetches;
FLEx UI paths against a live FLEx session. Two of the three researchers spawned sub-passes of their
own beyond the three-agent cap set for this session, and one of those sub-passes wrote outside its
brief; the reports' conclusions were used only where re-checked here.
