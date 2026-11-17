# ADR 0024 — Sources are read in place; scripture stays in Paratext

People work from their existing texts without turning scripture into a second copy inside FieldWorks.
Their judgements travel with the language project; computed annotations can be rebuilt.

**Status:** Accepted, 2026-11-14; D2 to D4 amended, D3 completed and D14 (Approve at an Occurrence) added by the owner, 2026-11-16. Occurrence-first progress and evidence policy in Proposed remains unaccepted.

## Context

Running text must preserve order, repetitions, provenance and location. Scripture, editable corpus files
and curated FieldWorks Texts have different owners; storing an analysis must not silently approve it.

## Decisions

### D1. Sources are read in place through declared input formats

Inputs are Paratext/USFM through SIL.Machine, FieldWorks Texts, plain text and JSONL. For .txt, one file is
one Document and each line a segment; its identity is its path relative to the Source folder. JSONL has
one Document per line with required id/text and optional title/source; its identity is its `id` within the
Source. Duplicate JSONL ids make the Source refuse to load, naming the duplicated id. A .txt rename/move
or a changed JSONL id keeps approvals, marked **changed since**, rather than silently discarding them.
No ELAN, Toolbox or PDF ingestion is added: conversion happens outside Motif.

Scripture stays in Paratext in the Motif/Paratext 10 flow, never copied into `.fwdata` as StText or as a
Motif corpus. FieldWorks' legacy USFM import remains available. Use SIL.Machine's ParatextTextCorpus,
ScriptureRangeParser and project Settings.xml versification; do not duplicate USFM, scripture
range or versification parsing. This dependency outranks the Native AOT goal; a working read-path AOT
spike does not prove whole-product AOT safety. Ordinary corpus segmentation follows D9, not an independent
Latin-only tokenizer. FW Lite plugin scope is [ADR 0025](0025-motif-bible-is-an-expanded-interlinearizer-on-fw-lite.md).

### D2. Source, Passage, Context, Later and Current have distinct grains

A Source is a kind of Corpus: a whole Paratext project with one versification, a corpus folder or another
supported source. Its chapters are its Documents, and it is worked with like any other Corpus; features are
written over Corpus Documents where that is cheap, with Paratext Sources first. A Passage is one scripture
chapter, which is that Source's Document, otherwise one Document. Context is a range string in Machine's grammar
with standard three-letter book ids for scripture; its expansion can be cached. Later is the complement of
Context and is not stored. Current is one chapter/Passage per person, local to that machine and never synced.
Non-scripture Context selection remains Open; Document identity is defined in D1.

### D3. Judgements are one sub-item per Passage and installation, under the Source's item, with one custom field

For a .fwdata Home, store Sources in a custom list with Context and versification. The Sources list is Sorted: the
Apply that makes it makes it sorted, and the first Apply that adds an approvals sub-item sorts a list made before
then. FLEx's Lists area then shows the Sources in alphabetical order rather than the order they were added.

Approvals are stored under each Source's own item, one CmPossibility sub-item for each (Source, Passage,
installation) that has approvals. An installation is one user's Motif on one computer. Its identity is a GUID in
`installation-id`, kept in Motif's per-user data folder beside `scripture-local.json` (`<user data>/SIL/Motif/`).
It is made the first time it is needed and is never synced. The file also holds a fingerprint of the computer: a hash
of the host name, the operating-system user and the system's machine id, which is the Windows
`HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`, the macOS `IOPlatformUUID` or the Linux `/etc/machine-id`.
The operating system sets the machine id when it is installed, so two computers with the same host and user names
still differ. A file whose fingerprint is not this computer's, as when a user profile is copied whole to another
computer, or that has none, is set aside and a new identity is made. Sub-items written under the old identity stay
readable and are never written again. Where no machine id can be read, the fingerprint is the host and user names
only and the file's third line says it is weak; there, two computers with the same names and a copied profile keep
one identity.

The sub-item's GUID is a hash of the Source item's GUID, the Passage (such as `MAT 5`) and the installation. Its
Abbreviation is the Passage, its Name is the Source, the Passage and the start of the installation id, and its
Description says that Motif manages it and can overwrite edits there. FLEx's Lists area shows these sub-items under
each Source; every Motif reader of Sources reads only the list's top-level items. Sub-items are made with
`ICmPossibilityFactory.Create(Guid, ICmPossibility)`. One custom String field on CmPossibility holds that
installation's approvals in that Passage as text, one line per Occurrence. The field is added through LibLCM
`IFwMetaDataCacheManaged.AddCustomField` (a path ADR 0003 allows). Each line gives:

- the location, such as `MAT 5:3#2`;
- the WfiAnalysis GUID, or nothing for a clear;
- what the approval was made against: the verse fingerprint, the word in Unicode NFD (the form FieldWorks keeps a
  wordform in and a saved project gives back; the current word is compared in NFD too) and the morphology signature;
- a stamp, which is new each time the installation writes the line;
- a settle record: the installation and stamp of each other line it overrules.

The WfiWordform field is not used. There is no "rejected" group: the only way to say an analysis is wrong is D4's
word-wide Disapproved Opinion. Per-chapter Progress summaries are recomputable caches.

An installation only ever writes its own sub-items, and two installations never share one, except on a system
without a readable machine id as above. So Send/Receive, which merges objects by GUID, only adds or keeps
whole sub-items and never sees one edited on both sides. The sub-items travel in the Sources list's file, which
every shared copy has once a Source was added and synced, so two people approving do not each create a file. Apply
reads each Passage's sub-items once and writes each changed sub-item once, so a bulk approval costs time in
proportion to the Occurrences and Passages it touches. Approvals at different Occurrences of one Source do not
depend on each other in the Draft, and each effect is linked to the operation at its Occurrence. A test counts the
blobs read and written per chapter; the time for 10,000 approvals is measured in the scheduled slow run
(`docs/testing-slow-run.md`).

Renaming, removing and restoring a Source change only the Source item, so its approvals sub-items stay with it; a
Removed Source keeps them, its progress is not published and new approvals there are refused. Deleting the Source
item outright, as FLEx's Lists area can, deletes its sub-items with it.

Reading unions every installation's sub-items for a Passage. At each Occurrence:

1. A line stands unless some line at that Occurrence names its installation and stamp in a settle record. A name
   counts even when the line holding it is itself overruled, because a stamp is never written twice.
2. If the standing lines give one analysis, the Occurrence is approved with it, counted once however many
   installations gave it. Every standing line is evidence: the Occurrence is Approved when any of them still holds
   against the current text and analysis, and Stale only when none does. Which installation wrote a line never
   decides this.
3. If they give two or more analyses, the Occurrence is a conflict: all are shown and none is counted as approved.
4. If only clears stand, or nothing does, the Occurrence is not approved.

A sub-item whose field cannot be read, or that arrived without the field's definition, may hold the settle records
that decide its Passage. Its Passage is reported as unreadable and nothing in it is counted as approved or as a
conflict; other Passages are read as usual. A sub-item whose Passage cannot be told makes every Passage of its Source
unreadable. Apply starts an installation's own unreadable sub-item again empty and writes every later approval of
the same batch into it.

Approving or clearing an Occurrence writes the installation's own line. Its settle record names every other
installation's line standing there at that moment, plus every name in its previous line. A name is kept even when
this copy of the project does not have the line it names, because that line may still arrive in a later
Send/Receive; dropping the name would let it stand again although nobody approved it after the Clear. Approving
again therefore settles a conflict for every reader alike. A later approval by another installation is a new line
with a new stamp that no settle record names, so if it gives a different analysis the conflict is open again. A
clear that would overrule nothing and carries no names just removes the installation's line.

The custom field is created by the first Apply that needs it, once per project; the Apply confirmation says so in
one sentence ("This Apply also adds the Motif Approvals field to your project"). There is no separate setup step.

FLEx Bridge evidence (flexbridge c51e79b): every unowned CmPossibilityList is written, with all the objects it
owns at any depth, to one file `General/UserDefinedLists/UserList-<list guid>.list`
(`src/LibFLExBridge-ChorusPlugin/Contexts/General/UserDefinedLists/UserDefinedListsBoundedContextService.cs:22-40`).
Objects in it are merged by GUID (`DomainServices/FieldWorksMergeServices.cs:113-120`), and the order of a list's
Possibilities and of an item's SubPossibilities is significant only when the list is not Sorted
(`DomainServices/PossibilityListOrderPolicy.cs:16-29`, used at `FieldWorksMergeServices.cs:230`). Run outside the
test suite, the real Chorus and FLEx Bridge merge kept distinct items added by both sides of a sorted list with no
conflict note, and a list file that both sides created separately lost one side's items. Hosting the sub-items in
the Sources list's file keeps them out of a file created by two people; two people who each add the first Source
before syncing still create the Sources list file twice, which is a separate open issue.

Persistence proof before release is a test that merges two installations' copies object by object against their
common ancestor, as Chorus does. It covers adding sub-items, settling and reopening a conflict, and a line that
arrives after the Clear that overruled it. The real Chorus and FLEx Bridge packages cannot be loaded in the tests
(their icu.net, L10NSharp and SIL.Core versions differ from Motif's pins), so the test is a simulation and not
Send/Receive. Still unverified: a real Send/Receive run and opening the merged project in FLEx. FW Lite uses the
same judgement meaning and anchors through the plugin's log records, without scripture bytes (ADR 0025).

### D4. Word-level judgements are separate from occurrence choice

SpellingStatus records a misspelling without a verse anchor. A human Disapproved WfiAnalysis says the
analysis is wrong wherever it occurs; it is the only way to say so, because an Occurrence has no
per-Occurrence Disapprove. Paratext's spelling
list takes precedence when its API is available. Never invent a text of misspellings. MCP cannot Apply;
agent edits must retain their permitted attribution and Proposal path.

### D5. Anchors and hashes expose source changes

A scripture anchor names book chapter:verse in the Source versification and the one-based word index in
the verse. A word is a run the project's vernacular writing system calls word-forming (Motif's
`WritingSystemWordTokeniser`, FieldWorks' rule), so the index holds the words the corpus denominator counts. Digits,
apostrophes and hyphens count only where the writing system declares them word-forming. The index records the
tokeniser as `fieldworks-word-forming-<rule version>`; an index built with another value, such as the earlier SIL.Machine
`machine-3.9.7-latin-1`, is rebuilt automatically in the background and refuses reads as stale until then.
Verse bridges anchor under their first verse. Existing corpus anchors use c:, the first eight hex digits
of the SHA-256 of the file relative path, then line and word index. The index rule is permanent and golden
tested; tokeniser changes must preserve it or require migration. The switch to the FieldWorks-rule tokeniser is a one-time exception, allowed because no stored word numbers existed yet. D1 defines Document identity; source-change
handling must retain approvals with their **changed since** mark across a renamed path or changed JSONL id.
Agree one anchor format with the interlinearizer team.

Chapter SHA-256 covers Machine's verse references and whitespace-normalized text, excluding notes.
Whitespace-only changes or another chapter's edits do not change it; footnotes are not Evidence here.
On hash change, same verse and surface keep a judgement, moving to the nearest matching index if needed;
otherwise retain it only as suggested or stale. Nothing becomes approved automatically. The every-occurrence
index and suggested/stale/explained statuses and Errors are recomputed locally, never synced. Cross-
versification behavior remains unverified.

A Trial checks approved in-context occurrences; a broken one is an Error. An incomplete search still
cannot prove reading loss, as ADR 0021 requires.

### D6. External acquisition hands Motif a documented bundle

Source-specific fetching/cleaning belongs to an external fetcher, not Motif. No URL polling, source-watch
or automatic upstream replacement is added. A Corpus bundle is small JSON naming Documents, not an archive
containing their bytes. Relative paths resolve against the bundle directory, and missing files fail by
name rather than silently reducing intake. A simple file/URL intake uses replaceable IContentFetcher with
no retry, cache or resumption; resumable acquisition belongs outside this seam. Local hash checks detect
deliberate edits, not upstream news.

Origin records description, location, retrieval information and licence. Per-document Licence capabilities
record may-redistribute, may-derive, may-use-commercially, requires-attribution and their attributed basis,
separately from the licence's verbatim name. A document override replaces the corpus capability set
wholesale, never fills missing fields from a permissive parent. Capabilities are yes/no/unknown; unknown
derivation permission blocks derivation with a different explanation from no. These are recorded claims,
not invented legal conclusions. A corpus qualification is an optional, usually absent, named person's
dated claim that the text is clean and in scope, with that person's reason. Record tokenisation method,
version and handling of punctuation, numerals, mixed script and case with the corpus.

### D7. Corpus files and metadata are primary project assets

Keep corpora under the project directory's corpora subfolders as ordinary editable files plus metadata
sidecar. It records stable Corpus/Document ids, relative paths/order, titles, origin/locators, retrieval,
format/language/writing system, licence/capabilities/basis and supplied extraction attribution; preserve
unknown supplied attributes. Keep separate source-byte and extracted-text hashes where extraction changes
material. A declared format is not a promise of a custom reader. Running text preserves repetition and
Document boundaries; n-grams cannot cross them. Deduplication belongs in a derived Selection.

The paired database indexes identities, paths, hashes, metadata, counts, use links and results, not the
authoritative text. Rebuild its source index without a duplicate of every Document. Stable identity can
acquire a new content hash. Source readers use D1's relative-path or JSONL-id identity; a renamed path
or changed id keeps its prior approvals with the **changed since** mark. Invalidate only changed/dependent
evidence and never relabel history. Missing files or conflicting sidecars are visible. Reads cannot combine
one version's bytes with another's annotations;
a hash is no backup if externally edited historical bytes are gone.

No corpus size cap, automatic eviction or storage-admission quota applies to originals/primary metadata.
An I/O error never authorizes dropping another Document. Disposable analysis copies are distinct. Back up
project, corpus files/sidecar and paired database together; Send/Receive does not carry adjacent files.
Moving current database-held Documents into files must preserve ids/provenance without loss and recover
interrupted file/index publication before old payloads are retired.

### D8. FieldWorks owns Harvester definitions; Motif records Harvests

A Harvester is a permanent-id item in a native Text Harvesters custom list, with title, FieldWorks Markdown
explanation, editable regex/filters, external source references and destination Text identity. Output can
be words, sentences or passages. It is not a Parsimony Recipe or permanent linguistic classification.
Settle exact fields, deliberate initialization and external-link resolver/Markdown round trip before writes.
Corpus links are project-scoped external references, not FieldWorks Project paths or native ownership;
copied projects retain readable citations even when source files are unavailable.

People can edit the native list in FieldWorks; Motif/agent edits use semantic Intents in Drafts and atomic
Apply with expected-current guards. Opening a project creates no schema; there is no direct agent writer.
Execution prepares a reviewable Draft for Text creation/update and never writes live data itself.

A Harvest records source Documents/hashes, definition as used if any, regex/filters/transformations,
analysis/resource identities, time/outcome and outputs. Manual extraction, manual Text linking, name
collection and model training share the record without inventing a Harvester/destination when absent.
Keep provenance durable separately from disposable output. Training provenance neither implements training
nor grants licence permission. Attach actual Text/Proposal/Receipt identities after confirmed Apply;
recover a missed link from Receipt without duplicating a Text. Verify deliberate direct imports before
manual linking. Later source/Text edits do not synchronize one into the other.

### D9. PanGloss supplies writing-system segmentation and language evidence

LibLCM WordMaker is the parity authority for writing-system-configured word boundaries, including numerics.
PanGloss accepts LDML or a JSON Segmentation profile retaining LDML identity/hash; Motif does not maintain
another segmentation implementation. Saved FieldWorks Text segments remain LibLCM's. SIL.Machine is a
segmentation comparison, not the corpus boundary authority. Preserve original spelling/offsets and map
separately to normalized NFD parser keys with declared offset units.

Ingestion must refuse a corpus's declared tokenisation binding when it does not match the tokeniser
used to measure it, rather than silently stamping it with a different identity.

Retain primary split and alternatives for word-forming apostrophes/hyphens, punctuation ambiguity and
partial pack matches, with part relationships and per-part Language candidates. Send all alternatives
to the analyzer. Display primary first, then alternatives all of whose parts analyze, then others; ordering
never changes the occurrence denominator. One character may be both word-forming and punctuation.

An Occurrence is analyzed if any complete alternative accounts for all its parts with valid grammar or
spelling acceptance. Never combine incompatible alternatives. Once one succeeds, other alternatives'
misses produce no failure, misspelling, no-analysis count, badge or regression flag. Keep raw alternatives
inspectable and valid-language/ambiguity observations attributed and deduplicated. Completed relevant checks
with no successful alternative are needed to establish no-analysis; missing work proves no absence.

Dictionary packs hold Hunspell data, word lists and optional short-sequence N-gram profiles. Language Pack
means only a PanGloss pgpack grammar artifact. Several dictionary/grammar resources may supply candidates
for contact languages; vernacular parsing remains visible and takes presentation precedence with also-
language evidence. Dictionary acceptance, detection, correction suggestions and grammar analyses remain
distinct. A correction does not make the original spelling valid; a miss proves neither misspelling nor
non-word. Automatic evidence proposes and people/explicitly chosen filters decide. Packs are installation
resources bound by exact version/provenance/licence per measurement, not per-project copies. Contact-
language checking is standard and requires no gigabyte detector. Catalogue, detector, dictionary container,
Hunspell implementation and distribution/deployment are not selected here.

### D10. Motif stores dependency-layered occurrence analysis; PanGloss is stateless

Save returned source spans, primary/alternative relationships, original/normalized forms, per-part evidence,
suggestions, completion/missing-resource facts and sentence locators. Bind project/Document/hash, profile,
packs, each grammar/parser/Language Pack, analyzer/schema and limits. PanGloss owns no corpus database or
retention policy. Reuse safe form-level work without copying full parse trees per repeated Occurrence.

Source/segmentation depends on Document hash, profile and segmenter plus resources that change alternatives.
Dictionary evidence depends on segmented forms, packs and detector settings. Grammar evidence depends on
forms/parts, grammars/parser and limits. Query projections depend on exact consumed layers and resolved
source membership. Grammar-only changes reuse segmentation/dictionary work; dictionary-only changes rerun
those layers and affected alternatives; source/profile changes invalidate dependent spans. A single coarse
combined key cannot replace dependency-aware reuse.

Keep per-Document/Text and per-Corpus/exact-Selection summary tables by language for Occurrences and distinct
Wordforms, distinguishing grammar analysis, pack acceptance, misspelled-language, ambiguous and no-analysis
counts with limits/unmeasured input. Alternative paths and analysis counts inflate neither denominator.
Language totals can overlap and are not an exclusive partition. Sentence/paragraph computations are on
request. Apply the same any-alternative success rule to Wordform summaries and freeze it with history.

Page stored ambiguous/English-only Wordforms and occurrence contexts, and typed filters over sentence
counts. Queries name which valid/misspelled candidates, resources and completeness rules count. English-only
requires no other valid-language evidence within declared completed checks; unavailable checks prove no
exclusivity. Reads never analyze missing rows implicitly.

### D11. One read-only annotated viewer client shares Motif behavior

CodeMirror 6 in Avalonia NativeWebView and a VS Code read-only custom editor share the client. One Motif
service behind the typed command catalog owns identity/hash validation, range reads, boundaries, annotations,
hover/focus evidence and occurrence queries, with CLI/JSON access and host-specific bridges. It is not a
second API, RPC protocol or runner daemon. Optional LSP is not the sole full-file data path.

Pair annotated source with frequency-ranked Wordforms and contexts. Hover/focus reads saved results;
explicit cancellable analysis creates new ones. No corpus approval state is added. FieldWorks owns Opinions;
SIL.TextEditing remains the sealed rich-text field, not this long-file viewer. Successful alternatives
suppress other misses' squiggles/badges. Use bounded ranges/annotation windows; viewport rendering alone
proves neither file-backed storage nor acceptable 100 MB behavior. Validate opening/memory/scrolling,
global offsets, bidirectionality, keyboard/screen-reader and native/web focus before product completion.
This selects no installer, runtime provisioning or browser/office integration.

### D12. Detail expires, durable summaries and provenance remain

Occurrence detail follows [ADR 0023](0023-one-store-beside-each-project-inside-a-byte-budget.md): committed
close, seven-day Proposal inactivity, current/shared-use protection and recoverable pending cleanup.
Apply releases analysis superseded by its grammar without letting historical citations retain it forever.
Reads/exports are not activity; no last-viewed clock is introduced. Freeze language/Wordform/Occurrence
counts, scope/denominators, identities and limitations before release and preserve those summaries through
upgrades. Archive failure keeps detail for retry. Missing detail says unavailable; totals cannot invent
rows. Explicit recomputation records its actual versions. Primary assets and Harvest provenance are durable.

### D13. Reach and correctness remain different claims

Selections remain explicit resolved NFD word sets, never saved regexes. Measurements additionally freeze
Documents/hashes, occurrence membership/weights/exclusions and segmentation/analysis versions: identical
word digests do not imply equal occurrence-weighted evidence. Contact-language detection does not silently
shrink scope. Preserve digit-only/other-writing-system subtotals, invalid shapes and incomplete states.
Grammar coverage is reach over Corpora; Text Coverage is over chosen FieldWorks Texts.

An unqualified corpus cannot produce an accuracy figure, even with a footnote: state why correctness is
unavailable and reach remains meaningful. Qualification is an attributed claim, not a new approval checkbox
or lifecycle. Filters and dictionary hits establish no curated human expectation. Corpus Statement and a
broader narrative family remain undesigned; neither is introduced. Reports keep ADR 0021's three levels,
factual flags and no verdict. Occurrence-based flags use any-alternative outcomes; confirmed reading loss
requires its own frozen identity/expectation, not merely one failed split.

### D14. Approve at an Occurrence follows FieldWorks

To Approve an Occurrence is to choose the analysis it has, as FieldWorks does in a Text, which also approves
that analysis; it works the same in a Text and in every Corpus, Paratext Sources first. An Occurrence nobody
has approved shows a **guess**. The only actions at an Occurrence are Approve and Clear. Intake and machine
analysis approve nothing; only a person's Approve, saved by Apply, does.

1. **Opinion.** Approving when the analysis's Opinion is Unknown also stages Opinion Approved. A Disapproved
   Opinion is left alone and the conflict is shown.
2. **One approved analysis.** An Occurrence holds at most one. The newest Approve replaces the old one and
   Clear returns the Occurrence to a guess. Everything is staged in a Draft and saved only by Apply; MCP never
   applies.
3. **Guess-only Add + Approve.** When FieldWorks holds no analysis (a PanGloss guess only), Approve stages "Add
   analysis from PanGloss" plus the approval in one Draft. Apply writes both, or neither.
4. **Stale.** An approval carries over only when the verse text, the word number and the analysis are all
   unchanged. Otherwise it is stale: shown, not counted as approved, and never re-approved automatically.
5. **Enter approves one place.** Enter approves this Occurrence and moves on. Approving every place is a separate,
   explicit action that shows the count first, such as "Approve 14 Places".
6. **Bulk Approve.** It covers the same Wordform whose guess is the same analysis, across Documents within one
   Corpus. It skips Occurrences already approved to another analysis, and stale ones, and shows the count
   before Apply, such as "approve 37 Occurrences in 12 chapters".

## Proposed

### D15. Occurrence approval would be the ground truth

Occurrence approvals would drive derived per-analysis FieldWorks approvals on Build; legacy export would
turn each human-approved AnalysesRS slot into an approved occurrence. Paratext approvals would use the
chapter/Passage records of D3, not an authoritative WfiWordform String. Passed over would be computed for a parser Candidate when all approved occurrences
chose another reading: below Unknown and above Disapproved, first for review, built as no opinion.

Progress for the book would show approval and grammar explanation by Occurrence. The internal rungs would
be word gloss, morphemes, lexical links, allomorph/MSA links and parser-explained; translators would not see
that ladder. Word-gloss agreement would be weak evidence. Proposals would be ranked by how many Occurrences
they lift to explained; this proposed prioritization would not constitute a linguistic quality verdict. Re-anchor on
opening a book and before Build; same verse/surface keeps approval, changed surface becomes suggested from
approved-elsewhere or explicitly marked parser guess, unplaceable becomes stale. Suggested/stale counts are
not approved; no automatic approval. Live Paratext events would follow later. Always-current versioned
Evidence is separately proposed in ADR 0020.

## Open

- Non-scripture Context selection and how text edits, line changes and segmentation affect anchors.
  D1 settles relative-path/JSONL-id identity, rename/id-change approval retention and duplicate-id refusal;
  it does not select a rename-detection mechanism or a new segmentation/anchor contract.
- The interlinearizer team's common analysis/anchor shape and the remaining merge/versification proof.
  Large-team occurrence sync needs its own scaling proof; no 700,000-object link log is authorized here.

## Consequences

Source originals, applied judgements, provenance, summaries and disposable annotations have distinct
lifetimes. Source edits can make historical replay unavailable without invalidating the recorded old
measurement. Ordinary corpus files and scripture remain outside the FieldWorks Text model until a person
deliberately promotes a curated non-scripture subset.
