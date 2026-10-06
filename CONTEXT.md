# Motif

Motif is the semantic vocabulary and tooling for proposing, evaluating, and applying changes to a
FieldWorks language project — its lexicon and, above all, its grammar. It sits between **LibLCM** (which
owns the data and is the only authority on it) and **PanGloss** (which parses with the result).

## The unit of change

**Motif operation**:
The smallest durable unit a Proposal is made of, named for its field and its verb —
`grammar/fsFeatDefn/setAbbreviation`. Named for the musical sense: the smallest recognizable unit that
recurs and is developed across a work. This is the hashed vocabulary a digest is computed over, so it
does not churn ([ADR 0021](docs/adr/0021-cli-is-the-full-surface-layer-1-churns.md)).
_Avoid_: command, CRUD+ operation, mutation, edit

**Intent**:
A unit of linguistic purpose an author states, which Lowering turns into the several Motif operations
that realize it — `AuthorLexemeForm`, `AuthorFeatureStructure`. What an agent addresses; the verb
surface may churn beneath a stable operation vocabulary
([ADR 0029](docs/adr/0029-agents-address-layer-1-only.md)).
_Avoid_: macro, template, high-level operation, composite

**Lowering**:
Turning one Motif operation into the concrete changes that realize it against a particular store.
_Avoid_: compilation, translation, expansion

**Proposal**:
A stored, named set of Motif operations that is reviewable as one unit. It owns its attached
Assessments, has a lifecycle, and can be applied to a language project or discarded. A Proposal does
not combine unrelated changes.
_Avoid_: PR, change set, change group, patch, branch

**Draft**:
A Proposal that is still being authored: it has an id and a name, and no committed revision yet.
Finalizing does not move it anywhere — it commits the first immutable revision and changes the
Proposal's state. A Draft is therefore a phase of a Proposal's life, never a separate thing kept
somewhere else ([ADR 0041](docs/adr/0041-the-database-is-the-only-store.md) decision 3).
_Avoid_: working copy, staging area, scratch proposal, uncommitted proposal

**Construct**:
One of the ~30 grammar things a Proposal can be about — a stratum, a natural class, an affix template,
a phonological rule. **The unit in which grammar support is staged and delivered**, and hand-authored
because the grouping is linguistic judgement. It used to double as the middle segment of an operation's
name; it no longer does ([ADR 0023](docs/adr/0023-derived-kind-names-required-descriptions.md)) — that
segment is derived from the declaring class. Construct is now purely about *what work ships together*.
_Avoid_: entity, class, feature

## Evaluating a Proposal

Two different evaluations, deliberately named apart. One asks *does the grammar parse better?*; the
other asks *what would this do to the project?*

**Assessment**:
One immutable measurement of one kind, made by one Assessor under one Assessment scope, returned in raw
form. A parse run is the common kind and PanGloss is the common Assessor, but an Assessment is tied to
neither: a C# HermitCrab or an alignment model asking whether more lexemes align each produce them too.
One invocation of one Assessor yields several — the compiled engine's size, the time to parse a subset,
per-rule timing, correctness against manual analysis, the difference between two sets of automatic
analysis, which words now complete. A difference is a measurement like any other. Immutable once written, so it
has no lifecycle of its own — the Job that produced it has the states, and *current* is a pointer the
project holds rather than a state the Assessment carries. Motif stores Assessments and compares them; it
never renders a verdict from one.
_Avoid_: parse report, evaluation, score, verdict, PanGloss run

**Assessor**:
What produces an Assessment — PanGloss, a C# HermitCrab, an alignment model. Two Assessments may only be
compared when they share an Assessor, which is what stops an alignment score being subtracted from parse
coverage.
_Avoid_: backend, engine, provider, plugin

**PanGloss invocation**:
One cancellable execution of a supported PanGloss command, answered with an outcome describing what came
back or why nothing did. What an Assessor uses, not what it is.
_Avoid_: process, subprocess, launch, parser run, shell-out

**Assessment scope**:
What a run was told to do: which words, which Assessor, what to collect, and what limits to
apply — a per-word limit defaulting to about a second, or an equivalent cap on attempts. Declared per
project and embedded in each Assessment by content, so editing the declaration cannot reinterpret a
measurement already taken. A scope is **context, not a gate**: two Assessments compare by joining on the
word, and a differing engine or corpus annotates that comparison rather than forbidding it.
_Avoid_: profile, config, settings, run options

**Assessment kind**:
What one Assessment measured — the compiled engine's size, the time to parse a set of words, per-rule
and per-morpheme timing, correctness against manual analysis, the difference between two Assessments,
or which words now complete. A closed set, because a Report can only say it was never given what it
needs if there is a fixed list of things it could have been given. Two Assessments compare only when
they share a kind, alongside sharing an Assessor.
_Avoid_: type, category, measure, metric

**Parse completion**:
Whether the parser finished its declared search for a word, independently of how many analyses or
approved matches it found. An interrupted search remains incomplete even when it found every approved
analysis, independently of completion for the other words in the same Assessment.
_Avoid_: first match, success, correctness

**Approved expectation**:
The set of morphologies represented by all human-approved analyses on a FieldWorks word form.
Every distinct morphology in that set is expected; finding one reading does not cover the others.
_Avoid_: the approved analysis, any parse

**Candidate**:
An analysis a FieldWorks project holds for a word form that no person has approved or disapproved, whoever
produced it: FieldWorks' parser, its guesser, or nobody. FieldWorks' own name for these is "Analysis
Candidates", and it offers them in texts as guesses, coloured tan. The window shows a candidate as
**Unknown**, the Opinion FieldWorks' User Opinion menu gives it
([ADR 0049](docs/adr/0049-fieldworks-opinions-and-now-after-apply.md)). One of the five things a project
can hold for a word that Motif compares an Assessment against: not present, candidate, approved,
disapproved, or an incorrect spelling. FieldWorks' parser re-judges candidates whenever it runs, and deletes
one it no longer produces unless a text uses it.
_Avoid_: not approved, present, unapproved analysis, no-opinion analysis

**Opinion**:
A person's judgement on one analysis, in FieldWorks' words: **Approved**, **Disapproved** or **Unknown**.
Shown with FieldWorks' colours (cyan, red, brown) and always with a letter and glyph too, never by colour
alone. It is changed one analysis at a time, in its text. A staged change of Opinion is read "now → after
Apply", never as an Opinion of its own.
_Avoid_: rejected, grade, verdict, no opinion, status

**Read**:
A word the linguist has looked at in Motif. Its opposite is **Unread**. It is kept in the Motif store and never sent to FieldWorks. It clears on its own, by the same fingerprint rules as a pending change: a new PanGloss result for the word, a change to its FieldWorks analyses or opinion, or a change to the sentence it was read in. The window colours Unread words and can show only those.
_Avoid_: reviewed, checked, seen, approved, needs a look

**Parser agreement**:
Whether the current Assessment still produces what FieldWorks holds for a word, judged by the same-analysis
rule of [ADR 0027](docs/adr/0027-what-counts-as-the-same-word-analysis.md): agreeing (an approved analysis
the parser still builds, or a disapproved one it no longer builds), a conflict, or a suggestion (the parser
builds an analysis FieldWorks lacks). A word whose search did not complete, was skipped or was not assessed
has no agreement either way — an unfinished search never shows that the parser does not produce something.
_Avoid_: match, parse success, coverage, correctness

**Uncertain**:
A staged Opinion change whose sentence changed underneath it: after a Refresh, some word in the same
sentence line of the Text is different. It stays staged but cannot be applied until the linguist looks again
and reconfirms it, or undoes it. Distinct from a change that no longer fits, where the analysis itself moved.
_Avoid_: stale, drifted, invalid, conflict

**Remove analysis**:
Deleting one stored analysis from the FieldWorks project, staged like any other pending change and shown
"now → removed". One analysis may be used by many places in the Texts, so Motif shows them before Apply.
Cancelling any staged change is **Undo**, never Remove.
_Avoid_: undo, discard, reject, clear

**Correctness**:
An Assessment kind measuring whether the parser reproduces the morphology of a word form's approved
analyses, without claiming agreement about sense or word-level category. Its findings are separate from
whether the word's search completed.
_Avoid_: parse success, grammar coverage, first match

**Difference**:
An Assessment kind: what changed between two other Assessments, joined on the word. Stored and citable
like any other measurement rather than computed afresh each time it is read. Replaces the earlier term
*Grammar Delta*, which named the same thing separately and so gave one concept two names.
_Avoid_: diff, delta, grammar delta, score change, improvement

**Dry Run**:
What a Proposal would do, computed by applying it to a throwaway copy of the project and reading the
effects back from the engine — never by predicting them. The live model is not mutated
([ADR 0016](docs/adr/0016-scratch-cache-copy-not-undo.md)).
_Avoid_: assessment, preview, plan, simulation

**Baseline**:
A saved, minimal, file-backed copy of the project state from which Motif can make many independent
Dry Run scratches. It contains the LibLCM and writing-system data needed to reproduce engine behaviour,
but no linked media bytes. A Baseline is replaced explicitly, not merely because time passed. It may be
captured from a project FieldWorks holds open, in which case it is the state as of FieldWorks' last save.
_Avoid_: snapshot, backup, session cache, live model

**Trial**:
One attempt at a Proposal on a throwaway copy of the project, producing both a Dry Run and one or more
Assessments — what it would do, and how well the result parses. Started whenever an author wants to
know, at no cost to the Proposal's state: a Trial freezes nothing, edits continue afterwards, and a
Proposal may have many at different points in its life. What a Trial measured is identified by the
content it ran against, so two Trials either side of an edit are distinguishable and comparable.
_Avoid_: run, attempt, test, experiment, evaluation

**Preflight**:
The final non-mutating comparison against the live model immediately before Apply. It proves that the
measured evidence still matches the project; it is not the earlier, reusable Dry Run.
It runs per change as well as per Proposal: each change carries the wordform and analysis identities it
touches and the Baseline token it was made against, and a change whose inputs are gone or different fails
and blocks Apply ([ADR 0046](docs/adr/0046-pages-not-stages.md)). The CLI verb is `preflight`; the window
never shows the word.
_Avoid_: dry run, assessment, validation pass

**Readiness**:
Whether a Proposal has been measured well enough to apply: an Assessment covers its current content, that
Assessment measured the project state the Apply would land on, and it shows no regression. Readiness is
computed from evidence, never granted by a person, and `--force` applies in spite of it.
_Avoid_: approval, sign-off, review, gate

**Drift**:
The condition where the project has moved since a Dry Run was computed, so the Dry Run no longer
describes what applying the Proposal would do.
A single change drifts too, when FieldWorks has saved since and something it was made against is gone or
different. The window says such a change "no longer fits".
_Avoid_: staleness, conflict, merge failure

**Apply Authorization**:
An opaque, one-use, short-lived grant from the Motif worker for exactly one Apply attempt. It binds the
project, Proposal intent, Dry Run, Baseline, and Assessment disposition; it is not a general security
credential, and it is not anyone's approval — nothing in Motif authorises an Apply.
_Avoid_: approval, token, permission

**Conflict**:
A loud, derived condition in which the language project's applied history and the Motif store disagree
about a Proposal. It is shown ahead of ordinary workflow states until a person resolves it, but it is not
itself a Proposal lifecycle state.
_Avoid_: drift, merge conflict, failed assessment

**Receipt**:
The record that one Proposal was applied to one project, naming the before and after state. The
durable edge in a project's history. It carries the short message the person gave when applying, which
is pre-filled with a summary of the changes, so they can later recognise what they did.
_Avoid_: application receipt, success result, audit log

**Report**:
A query over an Assessment and the project's own data, producing statistics and findings. **Advisory
always** — a Report never gates anything.
_Avoid_: score, verdict, metric, dashboard, health check

**Check Run**:
A Report cited as evidence on a Proposal, which freezes its inputs and binds it to the state it was
computed against.
_Avoid_: check, test, gate, validation, CI run

**Selection**:
A named set of word forms to be parsed, listed out in full, with a note of where they came from. Not a query
and not a sample — a person may pick fourteen words with nothing in common, and why they matter is theirs.
A word may be typed rather than found: it need not occur anywhere in the language project, and it is parsed
and measured like any other. A list can be exported from an Assessment, but what is kept is the words, so
nothing has to be re-derived.
_Avoid_: query, sample, filter, scope, subset, test set, corpus descriptor

**Default Selection**:
The Selection a project measures when nobody names another: chosen Texts plus any added words, stored in
the Motif store with the project and resolved to an exact word list and digest on every run. It also records
the per-word step cap and its time-limit policy. Chosen in the setup dialog once the project's first Baseline
exists, and changed from Configure. Each Assessment keeps the list and resolved limits it used, so changing
the Default Selection never rewrites what an earlier run measured.
_Avoid_: default corpus, word set, profile, preset

**Selection time-limit policy**:
Whether a Selection's per-word time limit follows Motif's estimate for its step cap or uses an explicit value
chosen by the person. An unbounded step cap has no time limit and no explicit override.
_Avoid_: inferred preference, hidden override

**Overview**:
Where a project stands, read from what the last run stored: the project's counts, Text Coverage, accuracy
against the manual analyses, timing and warnings. The window's first page and the `overview` command show the
same numbers, computed once. A projection of stored evidence, never a trigger to rerun.
_Avoid_: dashboard, summary, health, status, to-do list

**Refresh**:
The first of two things a person does to bring Motif up to date with the FieldWorks project: Refresh captures a
new Baseline, then Parse all words measures the saved Default Selection against it. Both steps are started by a
person, never on their own. Motif records when each Refresh happened and when the FieldWorks project had last been
saved, so the window can say how far behind it is. Coming back to the window only re-reads what Motif has already
stored.
_Avoid_: reload, sync, auto-refresh, update

**Review changes**:
The window's page where a person reads, together, what applying their pending changes would write (the Dry
Run), what it would do to the numbers (an Assessment of the touched words) and whether each change still fits
(Preflight). A page, not a lifecycle step: reading it is not recorded, grants nothing and gates nothing, and
the CLI has no `review` verb ([ADR 0046](docs/adr/0046-pages-not-stages.md)). Its button is "Apply to
FieldWorks project", which is Apply. The pending changes are a Draft Proposal in the Motif store, shown to
the linguist only as "N changes not applied yet".
_Avoid_: approval, sign-off, review decision, save

**Trace**:
The parser's own account of what it tried on one word: each step's rule, stratum or template, which subrule
or allomorph fired, the word forms in and out, and where a path died. The only thing in the system that
answers *why* a word did not parse, where statistics answer *whether* and *how expensively*. Costlier than
the parse it describes, because the parser stops collapsing equivalent analyses while tracing — so which
words get one is a decision, never a default for all of them. A Trace that ran out of budget is kept and
marked, not discarded.
_Avoid_: log, debug output, explanation, derivation dump

**Hole**:
A combination the grammar licenses that no analysis exercises. Undecided by construction: it means an
over-broad rule, a missing word, or an unreachable combination, and Motif does not guess which.
_Avoid_: gap, miss, failure, uncovered case

## Where state lives

**Canonical data**:
The FieldWorks language project itself — `.fwdata` on disk, or the live LibLCM model loaded from it.
The authority on model invariants, ownership, and validity.
_Avoid_: source of truth, database, backing store

**Live model**:
A loaded, in-memory LibLCM model representing the project as it is right now, against which Dry Runs
may be prepared and Proposals are applied. The Runner is always handed an already-loaded model; the
FieldWorks adapter or the `net10.0` Host owns loading, saving, locking, and disposal.
_Avoid_: cache, session, connection

**Motif store**:
Project workflow records — Proposals, Drafts, jobs, Assessments, Reports, Receipts, Corpora, and the
applied index — live in the project's paired sibling database. File-backed Baselines and parser
artifacts live separately under the Motif worker root, which defaults on Windows to
`%LOCALAPPDATA%\SIL\Motif` and can be changed with `MOTIF_WORKER_ROOT`. Content digests identify
immutable intent and evidence, but neither storage location is content-addressed. There is no merge
engine and no replication. The Machine store holds what belongs to the installation rather than to a
project.
_Avoid_: proposal store, change store, database, repository, queue

**Machine store**:
The single database for one logged-in user, holding what belongs to the installation rather than to any
project: the Known projects, and the usage log of the Motif API's own calls. It is deliberately small.
Machine-wide exclusion — PanGloss capacity and the single-runner guarantee — is not kept here but in
named operating-system mutexes, because the kernel releases those when a process dies.
_Avoid_: global store, config, registry, settings, central database

**Known project**:
A language project this installation has been pointed at, recorded in the Machine store when a command
names it. The list is what lets the Motif job runner find work in a project it was not launched with. A
Known project whose file has gone is forgotten rather than reported.
_Avoid_: registered project, workspace, recent project, project list

**Queue order**:
The position a job holds in the single ordered run of work across every Known project. It is stored,
not derived from when a row last changed, so moving a job changes what runs next rather than only what a
list displays ([ADR 0041](docs/adr/0041-the-database-is-the-only-store.md) decision 6).
_Avoid_: priority, rank, position, sort order

**Motif job runner**:
The one on-demand process for a logged-in Windows user that takes work which must outlive a command —
durable jobs, project queues, Baseline refreshes, and PanGloss orchestration. It claims work from the
paired database; nothing sends it requests, and it answers none. It never owns a FieldWorks user's live
`LcmCache`. Called *the worker* in documents written before 2026-08-26, when it also served a named-pipe
protocol that [ADR 0040](docs/adr/0040-one-api-the-cli.md) withdrew.
_Avoid_: server, service, daemon, project host, worker

**Motif API**:
The typed command catalog every front end calls, and the `motif` executable's `--json` rendering of it. The
CLI is how everything outside Motif's own process reaches the catalog — AI agents, scripts, tests, and a
separate FieldWorks alike — and it stays the complete front end: every catalogued command has a verb. Motif's
own application calls the same catalog in-process, which is Motif calling Motif, not a foreign host loading
Motif's internals; a separate FieldWorks still only ever runs a `motif` verb and reads its JSON. There is no
wire protocol.
_Avoid_: wire protocol, worker protocol, endpoint, RPC

**Text**:
FieldWorks' term, kept for FieldWorks' meaning: an interlinearised document **in the language project**.
Never used for a Corpus or a Document, which Motif holds and FieldWorks does not.
_Avoid_: using this for corpus material

**Corpus**:
A body of running text Motif holds, with a record of where it came from, how it was tokenised, and what
anyone attests about it. Never part of the language project.
_Avoid_: text, texts, dataset, word list, sample

**Document**:
One unit within a Corpus — an article, a file. The boundary counts and n-gram models must not run across.
_Avoid_: text, article, item, record

**Corpus bundle**:
The handoff an outside tool writes when it has fetched text for Motif: a small file describing one Corpus and
naming its Documents, with each one's origin and licence. It names files; it does not contain them.
_Avoid_: import, package, archive, manifest

**Handoff**:
The folder Motif writes for a person to give to a chat model, plus the separate block of text they paste
ahead of their question. It contains the grammar in PanGloss's JSON, every chosen Text in `texts.json`,
one helper script, and a short orientation; it adds `parse-results.json` for an Assessment and may add a
trace diagnostic. The longer format and parser explanations are linked online. Outbound, where a Corpus
bundle is inbound. Motif sends nothing anywhere; the person drags the files. Also *AI handoff* where the
audience needs the qualifier.
_Avoid_: export, bundle, package, dump, advice folder

**Licence capabilities**:
What a licence permits — redistribute, derive, use commercially — as distinct from what it is called. Three
states: yes, no, and nobody established it. The last blocks derived work exactly as "no" does.
_Avoid_: licence (that is the name), permissions, rights, flags

**HC interpretation**:
The rules by which grammar in a language project becomes a HermitCrab grammar — the semantics
FieldWorks' `HCLoader` implements and PanGloss ports. An authority on meaning, not a stored format.
_Avoid_: HC XML, export, projection

## Coverage and generation

> **"Coverage" is ambiguous in this project and must always be qualified.** An unqualified "coverage" is
> never acceptable in prose, a report, or an API name. There are six senses:
>
> | Term | What it measures | Whose |
> | --- | --- | --- |
> | **parse coverage** | What share of a word list parsed. The raw figure | PanGloss |
> | **grammar coverage** | How much of a language the grammar reaches — parse coverage reported over a named Corpus, with provenance | Motif |
> | **Text Coverage** | What share of the Default Selection's words, and of their occurrences in the chosen Texts, parse. Measured over the project's own Texts; the same share over a Corpus is grammar coverage | Motif |
> | **feature coverage** | Which declared grammar features, and which combinations of them, the analysed words actually exercise. What a Hole is an absence in | Motif |
> | **test coverage** | What the manual analyses assert. Always qualified in prose and API names — this repo has literal unit tests | Motif |
> | **model coverage** | Whether the generator accounts for every field in LibLCM's model. The terms in this section concern this one | Motif |
>
> *Renamed 2026-08-09.* **Feature coverage** was called *grammar coverage* until the two were being used in
> one sentence. The name moved to the measure people reach for it to mean — *how much of the language does
> this grammar handle* — and the feature-combination measure took the name that says what it counts.
> Documents written before that date use the old sense; `docs/grammar-coverage-design.md` is about **feature
> coverage** despite its filename, which is left alone because it is cited by name elsewhere.
>
> **Grammar coverage and feature coverage answer opposite questions** and a grammar can score well on one and
> badly on the other: *how much text can the grammar touch* versus *how much of the grammar does the text
> reach*. That is why both exist.

**Manifest**:
One row per field in LibLCM's model, recording what is in scope and which Construct it belongs to. Those
two are human judgement that exists nowhere else. Verbs and comparison behaviour are **derived** from
LibLCM's own structural declarations and checked against the manifest, not taken from it
([ADR 0022](docs/adr/0022-structure-is-derived-policy-is-five-rows.md)).
_Avoid_: inventory, schema, spec

**Group**:
The first segment of an operation kind — **`lexical`**`/lexSense/setGloss`. **Derived** from the declaring
class's LibLCM prefix family ([ADR 0024](docs/adr/0024-group-is-derived-domain-is-editorial.md)). Its job is
namespacing and versioning granularity: what changes together when LibLCM changes. **Not** a statement about
who should review something.
_Avoid_: domain, namespace, area

**Domain**:
Which linguistic area a field belongs to for **review purposes** — who should look at a change. Hand-authored,
never hashed, and deliberately allowed to disagree with the kind's `group`: `MoForm.Form` is named
`grammar/moForm/setForm` and reviewed as `lexical`, because a lexeme form is lexicon even though its class is
morphology.
_Avoid_: group, category, class

**Class segment**:
The middle segment of an operation kind — `lexical/`**`lexSense`**`/setGloss`. **Derived**, as the LibLCM
class where the field is declared with its first letter lowercased
([ADR 0023](docs/adr/0023-derived-kind-names-required-descriptions.md)). Deliberately ugly and never
curated: what a human needs in order to understand an operation lives in its **description**, which nothing
hashes. **Not the same thing as a Construct** — that word means a staging unit and nothing else now.
_Avoid_: construct, name map, mapping table, alias list

**Description**:
The required, never-hashed one-to-three sentences explaining a documented thing — an operation, a command,
a window control, a glossary term. For an operation it is seeded from the labels FieldWorks already shows
linguists. Free to improve at any time, because no digest depends on it. It must not restate its Title and
must record its source ([ADR 0047](docs/adr/0047-generated-help-walkthroughs-and-docs-site.md)).
_Avoid_: comment, doc, label, summary

**Ordered grammar**:
The grammar whose meaning depends on sequence — phonological rule order encoding feeding and
bleeding, and alpha variables using position as identity. The part that cannot ride on a
last-writer-wins order value.
_Avoid_: sequences, lists, sorted fields

## Help and documentation

**Help text**:
What Motif says about a documented thing, in four lengths written once and shown by the window, the CLI
and the website alike: its code, its Title, its Description and its Help page.
_Avoid_: docs strings, tooltips, localization strings

**Title**:
The short name of a documented thing, at most 30 characters — what a button, a menu or a CLI help list
shows. Translated; the code it belongs to never is. Unrelated to a Proposal's `label` field.
_Avoid_: label, caption, display name, summary

**Help page**:
The full explanation of a documented thing, a paragraph to half a page of plain Markdown, one file per
language. Shipped inside Motif so the CLI and the window show it offline; the website shows it with its
screenshots and clips.
_Avoid_: article, manual page, docs page

**Walkthrough**:
A declarative script of steps against the real window on fixed data. One Walkthrough is at once a test,
the screenshots of its marked steps, and a short silent clip with a caption per step.
_Avoid_: demo, tour, scenario, recording, tutorial

## The FieldWorks skills

Motif also publishes three Claude Code skills about FieldWorks itself (a FLEx expert, a parsing expert,
a linguistic consultant) as the `fieldworks` plugin. That is a bounded context of its own with its own
vocabulary — plugin, marketplace, skill, corpus, index, crosswalk, persona, job map — in
[fieldworks/CONTEXT.md](fieldworks/CONTEXT.md). Inside that plugin, **corpus** means a
documentation source, not the text corpus defined above.
