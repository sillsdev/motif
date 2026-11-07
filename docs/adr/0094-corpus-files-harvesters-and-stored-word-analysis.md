# ADR 0094 — Corpus files, FieldWorks Harvesters and stored word analysis

**Status: Accepted.**

A linguist needs abundant source material to find the frequent words a grammar cannot yet analyze,
and a deliberate route from that material into curated FieldWorks Texts. Corpus files are primary
project assets. PanGloss analyzes their words; Motif stores and queries the results, shows the source
with annotations, and records how it was used. Neither source intake nor machine analysis approves a
spelling, an analysis or a Proposal.

## Context

The existing corpus implementation stores whole Documents in the paired SQLite database. Earlier
designs treated imports as immutable snapshots, kept acquisition external, and used the project
writing system for word boundaries. The owner has chosen editable real files, FieldWorks-owned
Harvester definitions, and a wider PanGloss analyzer. These choices replace the earlier suggestions
to cap corpus intake, keep definitions only in Motif, port SIL.Machine's Latin tokenizer, or keep
PanGloss parser-only. They do not move bulk corpus content into the FieldWorks model.

## Decision

### 1. Corpus files and their sidecar are primary data; the database indexes them

Keep each Corpus under `<project>/corpora/<corpus>/`, where `<project>` is the directory containing
the FieldWorks project. Its Documents remain ordinary files, openable in other editors. A sidecar
records stable Corpus and Document identities, relative file paths and ordering, titles, origin and
source locators, retrieval information, declared format and language/writing system, licence and
Licence capabilities with their basis, and supplied metadata or extraction attribution. Preserve
unknown supplied attributes. Record source-byte and extracted-text hashes separately when extraction
changes the material. A format declaration does not promise a format-specific reader; the first useful
input is prepared running text, without a custom WYSIWYG reader.

The paired Motif database indexes file identities, paths, hashes, metadata, counts, derived-use links
and analysis results. Files and sidecar remain the authority for source content and its supplied
metadata; rebuilding their index must not require a stored duplicate of every Document's text.
Harvest history and durable measurement summaries have their own records and are not reconstructible
from files alone. Back up the corpus directory and sidecar, the paired database, and the FieldWorks
project together. Being beside `.fwdata` does not mean FieldWorks Send/Receive carries these files.

Detect local edits by content hash. A stable Document identity can acquire a new content hash;
the old hash still identifies what an earlier measurement or Harvest used. Invalidate results for
the changed inputs, never relabel old results as new. A rename alone must not invent a new Document.
Missing files or sidecar conflicts are visible, not silent empty Documents. A read must not combine
annotations for one hash with bytes from another. Exact historical replay is unavailable when its
source bytes were externally changed and no longer retained; a hash is not a backup.

Corpus text and its primary metadata are **never evicted**, with **no corpus size cap or storage-based
admission quota**. Show disk use separately for primary assets, computed detail and temporary work.
An ordinary I/O failure remains an error, not permission to drop another Document. This protection
does not require keeping a computed snapshot of every earlier edit forever, or prevent a person's
deliberate source edit or removal. Retention must distinguish the corpus originals from disposable
analysis inputs, even when both are called source files elsewhere.

### 2. Harvesters live in FieldWorks; Harvests live in Motif

A **Harvester** is a durable, human- and AI-editable definition for finding material in Corpora and
producing or updating a curated FieldWorks Text. Store definitions in a FieldWorks custom possibility
list, **Text Harvesters**, following ADR 0083's native-list pattern. Give each definition permanent
identity and a title, a rich-text explanation, editable regex and filters, external references to
source Corpora/Documents, and a destination Text reference. Output can be words, sentences or other
selected passages; it is not restricted to one word per line. For example, a Harvester can gather
capitalized forms outside sentence-initial position, unresolved forms, or examples of a grammatical
analysis. A filter's observation is not a permanent linguistic classification.

Reuse FieldWorks Markdown for the explanation without redefining GDR Markdown or broadening a GDR
beyond one grammar topic. Use existing LibLCM custom-list/field facilities; this decision requests no
FieldWorks data-model change. Specify exact field contracts and deliberate initialization before
implementing authoring. People can edit the list in FieldWorks. Motif human edits and agent edits
use semantic Intent in a Draft Proposal and the existing atomic Apply path, with expected-current
guards for direct FieldWorks edits. There is no direct agent list writer or automatic schema creation
on opening a project.

Corpus links are external, project-scoped references with stable identities and readable source
locators, **not** FieldWorks Project paths or native ownership of corpus content. Destination Texts
use FieldWorks identities. A copied FieldWorks project retains the definition and readable citations
even when its Corpus files are absent; those links show source unavailable. Stored link syntax and
its FieldWorks Markdown round trip require an explicit resolver contract before implementation.

A **Harvest** records one run or other derived use in Motif: which source Documents and hashes were
used, the Harvester definition as used when applicable, regex/filters and transformations, relevant
analysis/resource identities, time and outcome, and output references. This is one record type for
automated and manual uses, including manual passage extraction, a manually linked Text, proper-name
collection and n-gram training. A manual kind need not invent a Harvester or a destination Text.
Keep the durable provenance record separate from disposable output artifacts; recording a training
use does not implement a trainer or permit an otherwise disallowed derivation.

Executing a Harvester prepares a reviewable Draft for Text creation or update; it does not write the
live project. Attach the Harvest's source-to-Text relationship to the produced Text's actual identity
after confirmed Apply, including Proposal/Receipt identity. Recover a missed link from the Receipt
without duplicating the Text on retry. An intentional direct FieldWorks import can be linked manually
after verifying its identity. Subsequent source or Text edits do not synchronize one into the other;
the link describes historical derivation. Apply rules and MCP's prohibition on Apply remain unchanged.

### 3. PanGloss supplies segmentation and multi-language word evidence

Use the shared terms **Wordform**, **Occurrence**, **Word-forming character**, **Segmentation profile**,
**Segmentation alternative**, **Dictionary pack**, **N-gram profile**, **Language Pack** and
**Language candidate** in `CONTEXT.md`.
Many Occurrences share one Wordform; neither is called a token in new domain prose. Existing API
identifiers need not be renamed by this documentation decision.

PanGloss ports LibLCM WordMaker's writing-system-configured rule and accepts `.ldml` or a JSON
Segmentation profile retaining the source `.ldml` identity and hash. LibLCM WordMaker is the parity
reference, including numeric behavior; SIL.Machine is a comparison in the parity suite, not a
replacement authority. Existing FieldWorks Texts retain their saved LibLCM segments. Motif must not
maintain another independent implementation for corpus segmentation or change FieldWorks WordMaker
as a side effect. Source spelling and offsets survive parser normalization; NFD parser keys are a
separate projection with an explicit source mapping and declared offset units.

Retain the primary FieldWorks-rule split and alternatives for word-forming apostrophes/hyphens and
partial pack matches. Send **all alternatives** to the parser. Preserve part relationships and
per-part Language candidates, including hyphens joining parts from different languages. Present the
primary split first, then alternatives with every part validly analyzed, then the rest. Ordering does
not delete alternatives or silently change the measured occurrence denominator.

A character can be both word-forming and punctuation without contradicting the writing-system
settings. Preserve interpretations with it inside the word and with it treated as punctuation,
using Segmentation alternatives rather than dropping the character from the source. An Occurrence
counts as **analyzed if any of its alternatives is analyzed**, including its primary interpretation.
A complete alternative accounts for its word parts with valid analysis or spelling acceptance;
parts from incompatible alternatives are not combined into a fictitious successful split. Once an
alternative succeeds, misses of the other alternatives are ignored for occurrence outcomes: no
failure, misspelling, no-analysis count or flag is produced from those misses.

PanGloss's remit includes multi-language spell checking, prediction and word analysis, including automatic
language detection, contact-language Hunspell checking and correction suggestions. A **Dictionary pack**
contains Hunspell `.dic`/`.aff` data, a word list and an optional N-gram profile of short-sequence
frequencies for language detection. **Language Pack** means
only a PanGloss `.pgpack` grammar analysis artifact; it is not an umbrella term. A contact language can
also be analyzed by another Language Pack, such as a Tagalog/Filipino grammar loaded alongside the
vernacular grammar. Both kinds can supply Language candidates, with several resources loaded together.
A vernacular parse remains visible and takes presentation precedence when another language also
recognizes the form, shown as `also <language>`. Retain
grammar analyses, dictionary acceptance, word-list/detection evidence and correction suggestions as
distinct attributed observations. A misspelled-English candidate with a suggested correction is not
a valid-English analysis of the original spelling; a failed parse or dictionary miss alone establishes
neither misspelling nor a non-word. Automatic evidence proposes; people or their explicitly chosen
filters decide what to do with it. No detector model, Dictionary pack container or Rust Hunspell crate is selected
by this Motif ADR.

Dictionary packs are installation-level resources rather than copied into every project; each measurement
still binds their exact versions. Contact-language checking is a standard feature, with small detection
resources rather than a required gigabyte model. The exact resource catalogue and detector remain to be
selected. Dictionary and grammar resources retain their own provenance and licence identities.

### 4. Motif stores layered per-occurrence results; PanGloss is stateless

Store the core returned analysis so readers, filters and MCP queries operate on saved evidence
without rerunning Hunspell or the parser. PanGloss has no corpus database, storage lifecycle or
knowledge of Motif's retention policy; another host can store the same returned evidence.

The complete evidence binding includes project/Document identity and content hash, resolved
Segmentation profile hash, Dictionary pack identities and versions, each loaded grammar's version/content
and Language Pack identity where packaged, analyzer/schema versions and requested analysis limits.
Store primary and alternative source spans,
original surfaces and normalized lookup keys, per-part candidates and their evidence, suggestions,
completion and missing-resource states, and source references sufficient to locate occurrence
sentences. Never turn an unattempted or interrupted check into a completed no-analysis result.
Preserve raw per-alternative outcomes separately from the occurrence outcome derived with the
any-alternative rule. Failed alternatives can be inspected without becoming additional failures.

Layer storage by its actual dependencies:

| Layer | Inputs that identify reusable work | Stored output |
| --- | --- | --- |
| Source/segmentation | Document hash, resolved profile and segmenter version; pack inputs if they affect alternatives | Source spans, primary segmentation, alternatives and part relationships |
| Dictionary evidence | Segmented spans/forms, Dictionary pack versions and detector/resource settings | Candidates, dictionary acceptance, misspelling flags and suggestions |
| Grammar analysis | Forms/parts sent to the parser, each grammar/Language Pack and parser identity, limits | Analyses, language candidates, outcomes and completion |
| Query projections | Exact bindings of the layers consumed and resolved source membership | Counts and indexes for documents, words, occurrences and contexts |

A grammar-only change reuses unchanged segmentation and Hunspell evidence. A change confined to a
Dictionary pack reruns affected dictionary evidence and projections, plus segmentation/grammar work
only where changed alternatives require it. A source or profile change invalidates dependent spans and their results.
Dependencies, not merely a single combined cache key, determine which layer must run again. Reuse
identical form-level work where safe, while keeping each Occurrence's own source and candidate
relationships; do not copy whole parse trees onto every repeated occurrence.

Default statistics are per Document (or FieldWorks Text when that is the measured source) and per
Corpus or exact Selection. For each language code, keep counts of Occurrences and distinct Wordforms
with valid analysis, distinguishing grammar analysis from pack spelling acceptance. Keep separate
`misspelled-<language>`, ambiguous and no-analysis counts, with completion and unmeasured limitations.
Counts do not multiply by the number of analyses or alternative paths. Language counts can overlap;
they are not an exclusive partition whose sum must equal the occurrence total. Freeze the counted
source spans and the distinct-form rule so alternatives cannot inflate either denominator. Sentence
and paragraph statistics or further computations are on request, not mandatory summary tables.

Aggregate alternatives before deriving occurrence statistics. Any analyzed alternative makes the
Occurrence analyzed; failed alternatives then add no failure, misspelled-language or no-analysis
count, even if one contains a correction suggestion. Valid-language and ambiguity evidence remains
attributed and deduplicated, rather than being erased by that success. A no-analysis outcome requires
completed relevant checks with no analyzed alternative; missing or incomplete alternatives alone do
not establish it. Apply the same rule to Wordform summaries and freeze it with historical counts.

Provide paged stored-data queries for ambiguous Wordforms, English-only Wordforms and the occurrence
sentences of a Wordform, as well as filters such as sentences with at least five words and at most
two English words. Query definitions name whether valid, misspelled or other candidates count, which
packs and grammar were checked, and how ambiguity and incomplete checks are treated. "English-only"
means valid English evidence and no other valid-language evidence within the declared completed
checks; unavailable checks do not prove exclusivity. These reads never silently analyze missing data.

### 5. One annotated viewer client uses one Motif service

Use **CodeMirror 6**, read-only with annotations, in Avalonia **NativeWebView**, and reuse the same
client in a VS Code custom read-only editor. One Motif corpus/analysis service owns file identity,
hash validation, range reads, boundaries, annotations, hover evidence and occurrence queries. This
is shared domain behavior behind the typed Motif command catalog, with the same CLI/JSON surface;
it is not a replacement job-runner daemon, a second agent API or a new public RPC protocol. Each
host's bridge adapts to that behavior. An optional LSP adapter may later serve VS Code's ordinary
editor, but full-file LSP synchronization is not the sole corpus data path.

The viewer shows harvested/scraped source with boundaries, markers and inspectable saved findings,
paired with a frequency-ranked Wordform list and occurrence contexts. Hover and keyboard focus read
saved evidence; explicit, cancellable analysis produces new evidence. It has no corpus approval
state. Keep linguistic Opinions and spelling judgments in FieldWorks. SIL.TextEditing remains the
sealed FieldWorks rich-text control, not the base of this long-file viewer (ADR 0092).

Do not draw a failure or misspelling squiggle when any alternative analyzes the Occurrence. Other
alternatives' misses are neither flagged nor used to add warning badges. A person may inspect the
raw alternative results, but those misses do not become the source's displayed outcome.

Range queries and bounded annotation windows avoid shipping all results for every hover. CodeMirror
viewport rendering is not proof of a file-backed document model or acceptable 100 MB behavior;
benchmark memory, opening and scrolling before choosing a full-document or windowed client model.
Validate global offset mapping, mixed-direction text, keyboard navigation, screen-reader access and
native/web focus on supported desktops. This viewer decision does not select product deployment,
hosting, installer/runtime provisioning or browser/office integration.

### 6. Per-occurrence detail expires; summary tables remain durable

Per-occurrence corpus analysis is computed evidence, **not exempt** from ADR 0093. Apply its committed
close and seven-day Proposal-inactivity rules, actual-current-use protection, queued/running work and
reader/reconciliation leases, and recoverable pending cleanup. When Apply commits, release analysis
superseded by the applied grammar as well as the closing Proposal's computed ownership; history must
not keep it alive merely by citation. A promoted result that is actually current, or shared bytes
still needed by another entitled consumer, remain under that consumer. Corpus reads, queries and
exports do not constitute Proposal activity. Standalone/current project evidence follows its own
actual-use and existing housekeeping rules; do not invent a last-viewed retention clock.

Before releasing detail, freeze durable summary tables with counts per language (for example `ceb`
and `en`), misspelled-language, ambiguous and no-analysis counts, both occurrence and Wordform units,
scope/denominators, complete input/version identities and limitations. Keep these summaries as
history through store upgrades, alongside ADR 0093's written review summaries and bounded examples.
A failed summary write retains the source evidence for retry. Historical detail pages and queries
say unavailable after eviction; they do not infer rows from totals, substitute current analysis, or
rerun on a read. Explicit recomputation creates evidence under its actual inputs and versions.
Primary corpus files/metadata and durable Harvest provenance are not disposable occurrence analysis.

### 7. Keep the existing terms and undecided narrative boundaries

**Selection** remains an explicit, fully resolved NFD word set, not a saved regex or query. A
Harvester can help produce one, but each measurement also records exact Documents/hashes, occurrence
membership, weights, exclusions and segmentation/analysis identities. Equal Selection word digests
do not establish equal occurrence-weighted measurements. Use **grammar coverage** for named Corpora
and **Text Coverage** for chosen FieldWorks Texts. Contact-language detection does not silently shrink
either denominator. Preserve explicit digit-only/other-writing-system subtotals, invalid-shape and
incomplete results, and freeze any deliberately filtered scope.

Existing attributed corpus qualifications retain their historical meaning; this workflow adds no
qualification checkbox or corpus approval lifecycle. Curated expectations and human judgments remain
FieldWorks data rather than being inferred from a Harvester filter or a Dictionary pack hit.

**Recipe** remains Parsimony Measure guidance, not a Harvester synonym. **Candidate** alone remains
FieldWorks' undecided analysis; use **Language candidate** for language evidence. **Store budget**
remains a computed-data housekeeping target, not a cap on primary Corpora or durable summaries.
**Proposal activity** retains ADR 0093's authored-edit/evaluation definition. Reports retain ADR 0091's
three disclosure levels, factual red flags and absence of a verdict; language statistics add no
readiness test, acceptance percentage or Apply permission.

Occurrence-based red flags use the same any-alternative outcome. A missed split is not a new word
failure while another alternative still analyzes the Occurrence, and misses of the other alternatives
are not flagged as regressions or lost readings. A reading-specific comparison still requires its
own frozen expectation and evidence; a failed alternative alone cannot establish a confirmed reading
loss. This clarifies how corpus evidence feeds ADR 0091 rather than adding a red-flag category.

The broader project narrative and **Corpus Statement** were proposals in the background design,
not settled owner decisions. This ADR adopts neither a new narrative family nor a Corpus Statement
list/schema, template, lifecycle or renamed GDR. Harvester explanations cover their own purpose;
the first CEB slice does not depend on a wider narrative facility. Decide that facility separately
if needed. Source-specific fetching/extraction remains outside Motif under ADR 0037, including its
existing simple URL-fetch seam; hash-checking local files is not upstream polling. Deployment of
PanGloss spelling/prediction or the host applications is explicitly outside this ADR.

## Relationship to existing decisions

- [ADR 0036](0036-motif-has-its-own-data-store.md) and
  [ADR 0041](0041-the-database-is-the-only-store.md): amend corpus content storage to files plus
  sidecar and a paired database index; bulk text still stays outside the FieldWorks model. Primary
  imports cannot be treated as a disposable cache. Other workflow coordination stays database-owned.
- [ADR 0037](0037-fetching-lives-outside-motif.md): preserve external acquisition, Corpus bundles
  and document-level Licence capabilities; amend the immutable-import/new-Corpus-only restriction
  for deliberate local edits identified by hash. No automatic upstream refetch is introduced.
- [ADR 0082](0082-stores-and-user-data-stay-inside-a-byte-budget.md) and
  [ADR 0093](0093-proposals-release-computed-data-on-close-and-after-seven-days-untouched.md): primary
  corpus assets have no cap and cannot be evicted; per-occurrence detail follows computed retention,
  while frozen language summary tables are durable. No reservation/admission subsystem is added.
- [ADR 0083](0083-grammar-design-records-live-in-the-fieldworks-project.md) and
  [ADR 0089](0089-gdr-text-is-human-editable-fieldworks-markdown.md): reuse native list storage and
  rich-text editing for Harvesters without redefining GDRs or making corpus links Project paths.
- [ADR 0091](0091-proposal-review-report-three-levels-and-red-flags-no-verdict.md) and
  [ADR 0092](0092-textediting-is-a-sealed-folder-with-its-own-build.md): preserve honest stored review
  and the sealed rich-text library boundary.

PanGloss ADRs **0010, Multi-language spelling, prediction and analysis**, **0011, FieldWorks-compatible
segmentation**, and **0012, Return multi-language word evidence alongside the vernacular parse**
record the analyzer side. Motif consumes their stateless evidence contract and owns persistence;
it does not select PanGloss pack distribution or deployment.

## Consequences and alternatives

Users can open their sources with ordinary tools and keep them with a project, while reproducible
measurements bind hashes rather than assuming the files never change. Implementation must export
existing database-held Documents without loss before retiring their payload rows, preserve IDs and
provenance, and recover interrupted file/index publication. This is a design decision, not a claim
that the migration or viewer already exists. External edits can make historical replay impossible;
durable summaries must say what was measured and what detail is now unavailable.

Keeping all source text in SQLite would simplify a transaction but defeat ordinary file editing.
Capping or evicting corpus originals would destroy primary data. Keeping every occurrence result
forever would defeat ADR 0093; keeping only totals would prevent the requested stored queries.
Motif-only Harvester definitions would lose the human-editable definition on a FieldWorks project
copy. Separate editor implementations would duplicate annotation behavior. A native Avalonia editor
or VS Code-only extension gives less client reuse; the chosen shared viewer instead requires real
large-file, accessibility and native-host validation before product completion.
