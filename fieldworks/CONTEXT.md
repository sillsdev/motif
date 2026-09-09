# The `fieldworks` plugin

The bounded context for the FieldWorks skills Motif publishes. Proposal, Assessment and the rest of
Motif's change vocabulary live in the root [CONTEXT.md](../CONTEXT.md) and are not repeated here.
Decisions and their reasoning: [docs/research/2026-09-09-fieldworks-skills-research.md](../docs/research/2026-09-09-fieldworks-skills-research.md).

## The package

**Plugin**:
The unit Claude Code installs: this directory, `fieldworks/`, holding all three skills and
their shared references. Named `fieldworks` because it is about the subject, not the source; every
skill is invoked as `fieldworks:<skill>`. A plugin cannot read files outside its own directory.
_Avoid_: extension, package, bundle, "the motif plugin"

**Marketplace**:
The catalog Claude Code adds with one command, `/plugin marketplace add sillsdev/motif`. It is this
repository, named `motif`, declared in `.claude-plugin/marketplace.json` at the repo root. One
marketplace, one plugin, three skills.
_Avoid_: registry, store, feed

**Skill**:
One folder with a `SKILL.md` and, optionally, `references/`. The `description` in its frontmatter is the
trigger: it is always in context, the body loads when it fires, references load on demand. A skill
must stay self-contained enough to be zipped for Claude.ai on its own.
_Avoid_: prompt, persona file, agent

## The three skills

**FieldWorks expert** (`fieldworks-expert`):
Answers what FLEx does, where, and how it maps to the data model: location, visibility, and the
LCModel class behind a screen. Lookup, not judgment. Describes the shipped UI only, stamped with its
FieldWorks version.
_Avoid_: FLEx guide, help bot, UI skill

**Parsing expert** (`fieldworks-parsing-expert`):
Answers how HermitCrab and XAmple behave, what to configure in FLEx's Grammar area to teach the parser
a language, and why a word parses wrong, not at all, too many ways, or slowly. Owns the HermitCrab
gotcha corpus. XAmple is an honest appendix. Names PanGloss and Motif in one bounded section only.
_Avoid_: HermitCrab skill, grammar skill

**Linguistic consultant** (`linguistic-consultant`):
Plays the user. Frames the need and the persona, takes facts from the other two, and applies the
evaluation checklist. Owns the job map, the terminology, the personas and the verdict; keeps no map of
the UI of its own.
_Avoid_: linguist bot, usability skill, reviewer

## Words the skills share

**Corpus**:
A body of source text a skill draws on: the FLEx help, ai-parser-help, the FieldWorks configuration
XML, `MasterLCModel.xml`, Motif's research notes, the literature. Each corpus gets one treatment:
shipped, indexed, or taught-as-lookup. Not the same word as Motif's text corpus in the root glossary;
inside this plugin it always means a documentation source.
_Avoid_: knowledge base, data, docs

**Index**:
A shipped file with one line per topic of a corpus, built from the topics' own frontmatter, that the
agent greps to find the one topic to fetch. The help corpus is indexed, never vendored.
_Avoid_: table of contents, catalog, embeddings, RAG

**Reference**:
A file under a skill's `references/`, or the plugin's shared `references/`, loaded only when the
SKILL.md points at it. Authored reference files open with the version of FieldWorks, help branch and
`SIL.LCModel` package they describe.
_Avoid_: appendix, attachment, doc

**Digest**:
The short HermitCrab reference copied into every Motif Handoff folder, `docs/handoff/hc-mechanics.md`,
derived by hand from the parsing expert's full reference tree and saying so in its header. The tree is
the source of truth; the digest is what a chat model reads whole.
_Avoid_: summary, abstract, the reference

**Crosswalk**:
The one shared reference all three skills cite instead of copying: linguist term, FLEx label, LCModel
class, HermitCrab construct, one row each.
_Avoid_: glossary, mapping table, dictionary

**Persona**:
One of five inferred FLEx users the consultant speaks for, from mother-tongue translator to
computational evaluator. Inferred from the literature and two sampled projects, not from user research;
every persona card says so.
_Avoid_: user type, role, archetype

**Job map**:
The consultant's ordered list of the thirteen things a documenting linguist does, each with the FLEx
area that serves it and the places the literature disagrees about order.
_Avoid_: workflow, process, pipeline

**Summarize and link**:
The licensing rule for SIL-authored prose inside the plugin: paraphrase, cite the URL, never copy the
text. Black's two texts already ship with ai-parser-help and are the one exception. The Leipzig rules
may be reproduced.
_Avoid_: quote, excerpt, vendor

## Invariants

- No real project data anywhere in the plugin: no backups, no real-language grammars, no real word
  lists. Worked examples are synthetic or invented.
- The canonical home of the help markdown is `sillsdev/FwHelps`, branch `markdown-export`, and of this
  plugin `sillsdev/motif`. A personal fork is never a canonical home.
- Skill boundaries are stated in each SKILL.md as "do not answer X here, load Y", and the three are
  revised against each other's actual text, not assumed.
