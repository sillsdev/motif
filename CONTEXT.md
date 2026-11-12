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

**Composer**:
The catalogued command that turns one Intent into Motif operations, together with the linguistic policy that keeps
them consistent, such as reusing an existing analysis rather than adding a duplicate. It is named for the intent a
linguist would recognise (`compose-author-stem-name`), never for a field or a language, and it is the same command
whether a person runs it from the command line or an agent calls it as a tool
([ADR 0069](docs/adr/0069-the-agent-api.md)).
_Avoid_: setter, endpoint, builder, wizard

**Phenomenon**:
A cross-linguistic pattern a grammar must express, such as noun class, infixation, reduplication or stem
alternation. Languages are many and phenomena are few, so the Phenomena are the grammar Subjects an agent finds
Composers by; no Composer is written for one language.
_Avoid_: feature (a FieldWorks term), construction, rule

**Archiphoneme**:
A phoneme that an underlying form carries and a rule changes, so it may never appear in the written language, as a
nasal that is `m`, `n` or `ng` by the sound after it. LibLCM has no flag for it and Motif adds none: it is an
ordinary phoneme, found by the same Composers, and a phoneme that occurs nowhere draws an information finding.
_Avoid_: abstract segment, placeholder phoneme

**Underlying form**:
The represented phonological sequence in a parser form, which may distinguish segments, quantity or stress that
running spelling omits. Ordinary FieldWorks morphology and ordered rules retain those distinctions until final
written realization; a parser form stays nonabstract and its phonemes have nonempty representations.
Citation Form may keep the selected source headword
([ADR 0090](docs/adr/0090-underlying-distinctions-survive-until-the-grammar-realizes-the-written-form.md)).
_Avoid_: hidden phoneme flag, empty grapheme, pre-parser folding

**Subject**:
One entry in the closed list an agent searches to find a Composer: what part of a language project the change is
about. The grammar Subjects are the Phenomena; the others are lexicography and description, such as senses and
definitions, example sentences, a grammar explanation, or texts and their analyses. Each Composer declares the
Subjects it serves, and a new Subject enters the list with its first Composer.
_Avoid_: topic, category, area, tag

**Lowering**:
Turning one Motif operation into the concrete changes that realize it against a particular store.
_Avoid_: compilation, translation, expansion

**Proposal**:
A stored, named set of Motif operations that is reviewable as one unit. It owns its attached
Assessments, has a lifecycle, and can be applied to a language project or discarded. A Proposal does
not combine unrelated changes, and belongs to exactly one project: the same change in two projects is
two Proposals, each Applied on its own.
_Avoid_: PR, change set, change group, patch, branch

**Request**:
Something a linguist asked an AI assistant to fix: a word, a Warning or a Matrix cell, with an optional note in
the linguist's own words. A Proposal answers a Request; the Request is answered when that Proposal is
finalized, and reopens if the Proposal is withdrawn or superseded before a decision
([ADR 0094](docs/adr/0094-requests-how-a-linguist-asks-an-assistant-for-a-proposal.md)).
_Avoid_: task, ticket, ask, job, inbox item

**Draft**:
A Proposal that is still being authored: it has an id and a name, and no committed revision yet.
Finalizing does not move it anywhere — it commits the first immutable revision and changes the
Proposal's state. A Draft is therefore a phase of a Proposal's life, never a separate thing kept
somewhere else ([ADR 0041](docs/adr/0041-the-database-is-the-only-store.md) decision 3).
_Avoid_: working copy, staging area, scratch proposal, uncommitted proposal

**Proposal status**:
Where a Proposal is in its life, in the window's words: **Draft** (still being authored), **Ready** (finalized and
open for review; stored `proposed`), **Deferred** (wanted later, still open), and the closed ones: **Applied**
(final: never reopened or deleted, its Receipts kept), **Declined** (a person said no; stored `rejected`),
**Superseded** (replaced by the Proposal it names) and **Withdrawn** (its author took it back before review).
Lists show open Proposals by default. A declined, superseded or withdrawn Proposal that never applied can be
deleted with its Trials. A reopened Declined or Withdrawn Proposal stays closed until its Draft is Finalized;
discarding the Draft leaves it as it was.
_Avoid_: state, closed-out, archived, cancelled

**Rationale**:
A Proposal's account of what it changes and why, in FieldWorks Markdown, written and revised by people and agents
while the Proposal is open. Finalize requires it, with no checker findings; once Ready, a revision with findings is
refused and the last clean one stays (ADR 0099). It records the Proposal content it was written for, so a reader can see when
later changes have left it out of date. Its earlier versions are kept until the Proposal is decided, then dropped; the
latest stays, frozen. A reply belongs in a **Comment**.
_Avoid_: description (that is Help text), summary, commit message

**Comment**:
A remark attached to a Proposal, shown under its Rationale, in FieldWorks Markdown; its findings never block. A Proposal can have many and they can grow. People can add,
edit and delete any Comment; an agent can only add its own, attributed to it, and cannot change or remove one. Comments
freeze when the Proposal is decided. The window also records, as a Comment, which choice a person made for a change
that no longer fits. A Comment is a label for readers, not an approval.
_Avoid_: note, review, sign-off, annotation

**Grammar Design Record**:
A living page about one grammar topic of a language project, such as how word-initial sounds change: what the grammar
does and why. It keeps one permanent ID and title, is stored in the FieldWorks project, and is rewritten in place;
the Proposals that touched it are its history. Agents read it before changing the objects it describes. Also GDR.
_Avoid_: design doc, wiki page, note, decision log

**FieldWorks Markdown**:
The Markdown form of FieldWorks text: a strict profile of Pandoc Markdown, marked by `fwmd: 1` in its front matter,
that people and agents read and edit and that the clipboard carries. FieldWorks still stores the text; the Markdown is
a view of it (ADR 0089, SIL.TextEditing ADR 0028).
_Avoid_: GDR Markdown (for the general format), FWMD (in prose), source (for the stored text)

**GDR Markdown**:
A **Grammar Design Record**'s text as **FieldWorks Markdown**, written to the GDR template (title, ID, abstract, body
sections). It is not a format of its own: it has no notation, links or checks beyond FieldWorks Markdown's.
_Avoid_: GDR profile, agent Markdown, extended Markdown

**Project path**:
How FieldWorks Markdown names a FieldWorks object in a link: a path in a designed tree of the project, such as
`/lexicon/palit(2)`, always written with the object's GUID. The GUID is the identity; the path is a readable locator that
is refreshed when the object is renamed or moved.
_Avoid_: link kind, object URL, ownership path

**Proposal snapshot**:
A saved copy of the project as it would be after a Proposal, opened in FLEx to look around. It is built from the
Proposal on request and deleted when the Proposal's content changes or it is decided. Edits typed into it are never
applied. Not a **Baseline**.
_Avoid_: preview project, backup, baseline

**Home** (of a project):
Where a project's lexicon and grammar really live: a FieldWorks project (.fwdata) or an FW Lite project's history (a
Harmony log). A Proposal is applied to the Home; everything else is built from it
([ADR 0101](docs/adr/0101-a-project-home-is-fwdata-or-a-harmony-log-everything-else-is-built.md)).
_Avoid_: source (that is the Paratext project), master copy, database, backend

**Build**:
Making what a tool reads, such as the parser's grammar or a FieldWorks project, from a project's Home. A Build
reports problems and never fixes them. It is internal: people see "parser updated" or a problem to fix, not a Build.
_Avoid_: compile, realize, materialize, render, Lowering (which turns one operation into store changes)

**Update baseline**:
Bringing a Proposal up to the project as it stands now, so its changes are checked against the current state. The window
says **Update to current project**; each change that cannot be carried over then "no longer fits" and offers Use mine,
Use FieldWorks or Fix with chatbot. A person can view both versions and copy from either.
_Avoid_: rebase, refresh, sync, merge

**Finalize**:
To commit a Draft's content as the Proposal's first immutable revision, handing it to a person to review.
Finalizing is not needed to measure a Proposal: a Draft can be Dry Run and Trialled as often as its author
likes.
_Avoid_: finish, submit, commit, publish

**Citation**:
The source a Motif operation or phonology example was taken from — a section of a published grammar, a
dictionary entry, a corpus passage — recorded as a `source` and an optional `locator` such as a page or
section. It is review metadata:
citing a source, or a different one, never changes what a Proposal does or its intent digest. It names where a
change came from, never who made it ([ADR 0064](docs/adr/0064-every-operation-can-cite-its-source.md)).
_Avoid_: author, attribution, reference (for a cited source), provenance (the contract field that holds it)

**Construct**:
One of the ~30 grammar things a Proposal can be about — a stratum, a natural class, an affix template,
a phonological rule. **The unit in which grammar support is staged and delivered**, and hand-authored
because the grouping is linguistic judgement. It used to double as the middle segment of an operation's
name; it no longer does ([ADR 0023](docs/adr/0023-derived-kind-names-required-descriptions.md)) — that
segment is derived from the declaring class. Construct is now purely about *what work ships together*.
_Avoid_: entity, class, feature

**Rewrite rule**:
A regular phonological rule with an Input and Output of up to eight phoneme or natural-class items each, in order,
with left and right contexts. The two sequences may have different lengths; empty Input means insertion, and empty
Output means deletion. These sequence shapes are proven through FieldWorks' HermitCrab and PanGloss. An Output
segment-list natural class does not choose a member: it applies only the features the members share, so the input segment keeps its own value for every feature the members differ on (an l stays l under {l,w}); this holds for rule Output only. A context item may be optional or repeated, and a context
may name a word or morpheme boundary. A rule can be edited in place, reordered, switched off (disabled) or deleted;
its order is meaning, because a later rule sees the output of the earlier ones. A rule has one or more authored right-hand sides, each with its own Output, contexts, required/excluded rule features
and input categories. They are ordered cases: the first applicable one wins, rather than freely alternating. A multi-member Output
segment-list class does not supply alternatives; author them as alternate forms of the stem. Complete expected-current guards protect outcome
sequence replacement for adding, removing or reordering cases; existing longer sequences read back without truncation. An aligned feature-class Output changes
only its specified features; Output `change` bundles express this explicitly. Alpha variables share a closed feature across Input, Output and contexts through positive or negative
constraints, including single or all-negative references to any declared closed phonological feature with values,
without requiring its use on phonemes. They read back with stored identities so edits preserve sharing; replacing
all Input referrers recreates the constraint when LibLCM collects the old one. A variable confined to repeated
items inside multi-item contexts is refused because FieldWorks' loader cannot discover it; another reference
outside those iterations makes it discoverable. An unrepresentable context
still prevents an editable intent
([ADR 0088](docs/adr/0088-phonological-rules-are-edited-in-place-and-hold-one-item-each-side.md)).
_Avoid_: sound change (for the stored rule), multi-item rule

**Judgement call**:
A choice the sources leave to the analyst, such as whether an affix, reduplication included, is derivational or
inflectional. Linguistic decisions follow Andy Black's paper and guidance; a judgement call belongs to the person
or agent who makes it, recorded with a rationale. Whatever LibLCM can hold, Motif allows, and it refuses only an
invalid model; Grammar Health and Parsimony advise and never forbid
([ADR 0079](docs/adr/0079-linguistic-decisions-follow-black-and-judgement-calls-are-the-linguists.md)). Changes
to ADRs, `AGENTS.md`, `CLAUDE.md` and this file go straight to `main`, which is how decisions are made visible.
_Avoid_: override, exception, waiver

## Stages of use

**Grow**:
The first stage of using Motif: simple local analysis to get the text at hand working, one chapter at a time, with
Errors only, a speed light, Proposals and GDRs. Tagline: "Find the Pattern in the Words"
([ADR 0100](docs/adr/0100-motif-has-two-stages-grow-and-polish.md)).
_Avoid_: Build, Explore, Workshop

**Polish**:
The second stage: making the grammar ready for everyone, with deployment, FST, Warnings, speed targets, coverage and
Parsimony, besides Proposals and GDRs. Tagline: "Make It Ready for Everyone". People move back and forth between
Grow and Polish.
_Avoid_: Deploy, Release, Publish, Production, Send

## Scripture in the Paratext flow

**Source** (of scripture):
A whole Paratext project, or a folder of corpus files, with one versification. Motif reads its text and never copies it
into the FieldWorks project ([ADR 0110](docs/adr/0110-scripture-stays-in-paratext-and-judgements-ride-on-wfiwordform.md)).
_Avoid_: import, corpus copy, home

**Passage**:
One chapter of a Source; for a corpus, one document.

**Context** (in context):
The chapters of a Source the work is about now, written as a range such as `GEN-ROM; 1CO 13-15`.
_Avoid_: scope, selection

**Later**:
Every chapter of the Source that is not in Context.

**Current**:
The one chapter a person is working on. It is kept on their machine and is not shared.

## Evaluating a Proposal

Two different evaluations, deliberately named apart. One asks *does the grammar parse better?*; the
other asks *what would this do to the project?*

**Assessment**:
One immutable measurement of one kind, made by one Assessor under one Assessment scope, returned in raw
form. A parse run is the common kind and PanGloss is the common Assessor, but an Assessment is tied to
neither: a C# HermitCrab or an alignment model asking whether more lexemes align each produce them too.
One invocation of one Assessor yields several — the compiled engine's size, the time to parse a subset,
per-rule timing, correctness against manual analysis, the difference between two sets of automatic
analysis, which words now complete. A Trial summary is a measurement like any other. Immutable once written, so it
has no lifecycle of its own — the Job that produced it has the states, and *current* is a pointer the
project holds rather than a state the Assessment carries. Motif stores Assessments and compares them; it
never renders a verdict from one. A Proposal review report presents this evidence at summary, failures and
full levels. Its red flags state observed regressions, not a verdict; their absence means only "not bad",
never "good". Incomplete evidence is labelled Incomplete, not Failed
([ADR 0091](docs/adr/0091-proposal-review-report-three-levels-and-red-flags-no-verdict.md)).
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
What a run was told to do: which words or explicit phonology examples, which Assessor, what to collect,
and what limits to apply — a per-word limit defaulting to about a second, or an equivalent cap on attempts.
Declared per
project and embedded in each Assessment by content, so editing the declaration cannot reinterpret a
measurement already taken. A scope is **context, not a gate**: two Assessments compare by joining on the
word, and a differing engine or corpus annotates that comparison rather than forbidding it.
_Avoid_: profile, config, settings, run options

**Assessment kind**:
What one Assessment measured — the compiled engine's size, the time to parse a set of words, per-rule
and per-morpheme timing, correctness against manual analysis, the Trial summary of two Assessments,
or which words now complete. A closed set, because a Report can only say it was never given what it
needs if there is a fixed list of things it could have been given. Two Assessments compare only when
they share a kind, alongside sharing an Assessor.
_Avoid_: type, category, measure, metric

**Parser work**:
The measure of how much the parser did for a word, a rule or a whole run: every unit of work PanGloss reserved while
parsing, shown as a percent change against the Baseline, for example "about 12% more parser work". "This grammar takes less work to parse the words" means fewer parser work. The
**parser work limit** is the per-word bound on it, 20,000,000 by default. It counts the same quantity, not analysis attempts.
PanGloss's own names (`work_steps`, `--step-cap`) stay where Motif reads or passes them.
_Avoid_: work steps, steps, step cap, step limit, attempts

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

**Passed over**:
A Candidate the parser produces for a word form whose approved occurrences all chose a different analysis. It ranks below
Unknown and above Disapproved, and is reviewed first, because it is the main sign that the grammar produces too many
readings. It is computed, never set by a person, so it is not an Opinion.
_Avoid_: rejected, outvoted, other-approved, wrong

**Opinion**:
A person's judgement on one analysis, in FieldWorks' words: **Approved**, **Disapproved** or **Unknown**.
Shown with FieldWorks' colours (cyan, red, brown) and always with a letter and glyph too, never by colour
alone. It is changed one analysis at a time, in its text. A staged change of Opinion is read "now → after
Apply", never as an Opinion of its own.
_Avoid_: rejected, grade, verdict, no opinion, status

**Set (an analysis)**:
The action that chooses what FieldWorks will hold for one analysis: **Approved**, **Unknown**,
**Disapproved** or **Remove**. Its menu says **Set analysis**; **Opinion** names the value, not the
action. **Add analysis from PanGloss** offers readings FieldWorks does not hold. Both stage changes
for Review and Apply; **Undo** takes back a staged change. The count remains **N changes**.
_Avoid_: Fix, Opinion as the action name

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
A saved comparison of one Trial's before and after parses for one Text, set against the Approved
expectations in the Baseline the Trial measured ([ADR 0067](docs/adr/0067-proposal-difference-against-human-decisions.md)).
It is a standalone result, not an Assessment kind: it cites the Trial's two Assessments by id and carries
every fact it needs by value, so it does not keep them alive. There is one per Proposal, Text and reference
(a person's decisions or a program's), so several sit side by side, and a new one replaces only the one for the same
Text and reference. A small summary lasts as long as its Proposal; the per-word detail is dropped as soon
as the Proposal is applied, rejected, withdrawn, superseded or deferred, and a deferred Proposal can make it again. The `delta.*` codes on the wire are stable
identifiers, not vocabulary.
_Avoid_: diff, delta, grammar delta, score change, improvement

**Trial summary**:
The older whole-Trial comparison, joined on the word and stored as an Assessment of kind `Difference`
(the `motif_difference` tool). It stays as it is and is not extended; a Difference is the newer result.
_Avoid_: Difference, grammar delta

**Dry Run**:
What a Proposal would do, computed by applying it to a throwaway copy of the project and reading the
effects back from the engine — never by predicting them. The live model is not mutated
([ADR 0016](docs/adr/0016-scratch-cache-copy-not-undo.md)). A Draft can be Dry Run as well as a revision;
the result is identified by the content it ran against, so editing the Draft afterwards leaves it describing
the earlier content.
_Avoid_: assessment, preview, plan, simulation

**Store budget**:
The housekeeping target for a project's Motif store, 100 MB, with a larger target for the per-user folders of
runs, sources and Baselines ([ADR 0082](docs/adr/0082-stores-and-user-data-stay-inside-a-byte-budget.md)); neither
is a guaranteed physical ceiling. A Proposal releases unneeded computed evidence at committed close or after
seven days without Proposal activity even while open, keeping authored content and written summaries with bounded
examples ([ADR 0093](docs/adr/0093-proposals-release-computed-data-on-close-and-after-seven-days-untouched.md)).
Primary Corpus files and metadata have no size cap and are never evicted; stored per-occurrence analysis follows
computed-evidence retention, while its frozen language summary tables remain durable history
([ADR 0094](docs/adr/0094-corpus-files-harvesters-and-stored-word-analysis.md)).
_Avoid_: quota, disk limit, cache size

**Proposal activity**:
An authored edit or a Dry Run, Trial or Assessment run against a Proposal; viewing or reading stored results
does not count. It determines the seven-day retention of that Proposal's computed evidence, independently of its
status ([ADR 0093](docs/adr/0093-proposals-release-computed-data-on-close-and-after-seven-days-untouched.md)).
_Avoid_: last viewed, last opened, access time

**Baseline**:
A saved, minimal, file-backed copy of a project's grammar and lexicon, from which Motif can make many independent
Dry Run scratches. It holds what a Proposal changes and what the engine needs to reproduce its behaviour, but not the
Evidence it is measured against, and no linked media bytes. A Baseline is replaced explicitly, not merely because time
passed. It may be captured from a project FieldWorks holds open, in which case it is the state as of FieldWorks' last
save ([ADR 0109](docs/adr/0109-evidence-is-separate-from-the-baseline-and-always-current.md)).
_Avoid_: snapshot (a Proposal snapshot is a different thing), backup, session cache, live model

**Evidence**:
The scripture chapters (read from Paratext) and the approved occurrences a Proposal is measured against. It is always current: it follows Paratext edits and
new approvals, and carries a version. A Trial records the Evidence version it ran on; a result from older Evidence is
shown as outdated and is rerun for the changed words only.
_Avoid_: test data, corpus (a Corpus is unapproved material), baseline, evidence set (a different thing: one named body
of measured results)

**Trial**:
One attempt at a Proposal on a throwaway copy of the project, producing both a Dry Run and one or more
Assessments — what it would do, and how well the result parses. Started whenever an author wants to
know, at no cost to the Proposal's state: a Trial freezes nothing, edits continue afterwards, and a
Proposal may have many at different points in its life. What a Trial measured is identified by the
content it ran against, so two Trials either side of an edit are distinguishable and comparable.
_Avoid_: run, attempt, test, experiment, evaluation

**Dropped grammar**:
Grammar the parser leaves out with only a warning, so a Trial parses and looks fine while the grammar is weaker
than the Proposal says: an environment it cannot read, an allomorph it cannot segment, an entry with no usable
form. Every Trial checks for it. The Trial is *clean* only when that check ran and found none in what the
Proposal adds or edits; it is not clean when it found some, and also when the check did not run, did not
finish, or could not compare with the Baseline's own check. "Clean" says nothing about the Correctness numbers,
which a Trial reports separately. Apply refuses a Proposal that is not clean and the window offers Try again for
one whose check did not finish; `--force` on the command line applies in spite of it. Check findings, which the
parser reports for a rule it keeps as partial or for data that is missing, leave nothing out and do not count. A
finding the Baseline already had is counted but does not make a Trial unclean, except one of the listed drops on an
object the Proposal edits.
_Avoid_: lost grammar, silent drop, dirty Trial

**Evidence set**:
One named body of measured evidence a page or command reads: the Baseline it was measured against, its
root ParseTime Assessment, the later re-runs that replace some of its words, its Correctness and per-rule
timing measurements, its grammar check, and where it came from, the project or a Trial. The project's
current evidence set and a Trial's have the same shape, so a page cannot tell them apart.
_Avoid_: context, source, data context, snapshot, Evidence (that is the input, not the measured result)

**Proposal mode**:
The window's state while a Proposal is open: a context bar names it and says when its Trial was run. The
bar's view switch (Today, Today → Proposal, Proposal) shows only on a page that follows it, now the Overview,
Texts, Warnings and Timing, and chooses whether that page shows today's evidence set, the Proposal's Trial evidence set or
both; every other page shows today's. Each such page runs the same query once for each set, only while it is shown
and once for each Proposal, Trial and choice of words, and says in a note what is paired and what stays today's; a
value one set lacks reads as none, never as zero or as the other set's number, and a read still running reads as
reading, never as an earlier key's number. Timing and the Texts matrix pair only when both sets cover the same
words; the matrix holds the Proposal's side as counts and an index of the values the word list reads for each word (its cell,
parser work and time), never the Trial's rows, and lets go of it when the Proposal is left or another Proposal or Trial is open. The Texts words under the
matrix are the Proposal's in the Proposal view, where a Trial word today's matrix lacks is counted but not listed, and
carry a With This Proposal column in the pair; the places under a cell follow the same choice; a Moved By This Proposal chip narrows the words to those the Proposal puts in a different cell than today, over the words both sides have, and is dropped with the Proposal;
the Lists tab reads each list's words and places the same way, and What Changed in the pair lists the words that
moved from one cell today to another with the Proposal, over the words both sides have, counting the rest and
holding only positions that go when the Proposal does; the Proposal view lists the same moves, since a move is a
word's cell today and its cell with the Proposal. Choosing a view is
not an evidence change, so no page reloads, re-reads or re-applies for it, and the choice comes back when a
following page is shown again. Texts, Warnings and Timing each keep a delta badge beside their sidebar entry (its tooltip and accessible name say what it counts) while a
Proposal is open, from numbers the page has already read: the net change in the Same column's words, the findings gained or lost
and the percentage change in parser work; a page that has not read its side, holds a side over other words or finds
no change shows none. In the Proposal view a headline stands alone, with today's breakdown left
to the Today view. Leave, or Esc when nothing else uses it, returns to today.
_Avoid_: compare mode, diff view, Proposal page, branch

**Looked at**:
A review mark a person puts on one change in a Proposal with Keep, or by stepping past it with Next.
Kept in the Motif store, never in the Proposal; it clears when the change's content or the Trial evidence
it was judged against changes. Nothing records who looked.
_Avoid_: reviewed, accepted, approved, seen

**Keep** and **Undo** (of a change in a Proposal):
Keep marks a change looked at and leaves the Proposal as it is; Undo removes the change and every change
that declared it depended on, which makes a new revision. Neither is Accept or Reject: nothing is
approved, and an undone change is gone rather than refused. Undo exists only before Apply.
_Avoid_: accept, reject, approve, discard

**Preflight**:
The final non-mutating comparison against the live model immediately before Apply. It proves that the
measured evidence still matches the project; it is not the earlier, reusable Dry Run.
It runs per change as well as per Proposal: each change carries the wordform and analysis identities it
touches and the Baseline token it was made against, and a change whose inputs are gone or different fails
and blocks Apply ([ADR 0046](docs/adr/0046-pages-not-stages.md)). The CLI verb is `preflight`; the window
never shows the word.
_Avoid_: dry run, assessment, validation pass

**Readiness**:
Whether a Proposal has enough evidence to apply. Normally a Correctness Assessment covers its current
content and project state and shows no regression; a judgment-only Proposal needs no Correctness when its
current bound Dry Run confirms that its effects are limited to the bounded Notebook judgment writes. Every
Apply, from the App or the CLI, passes this evaluation: it needs a bound Dry Run anchor and no Readiness
reasons, unless forced. Readiness is computed from evidence, never granted by a person, and `--force` applies
in spite of it.
_Avoid_: approval, sign-off, review, gate

**Drift**:
The condition where the project has moved since a Dry Run was computed, so the Dry Run no longer
describes what applying the Proposal would do. Applying one Proposal drifts every other Proposal's
evidence that was measured against the earlier state; evidence counts only for the same content and the same
project state. Finalizing unchanged content does not drift it.
A single change drifts too, when FieldWorks has saved since and something it was made against is gone or
different. The window says such a change "no longer fits".
_Avoid_: staleness, conflict, merge failure

**Apply**:
Writing a Proposal's changes into the FieldWorks project, always a person's act and always one-way: Motif
has no unapply. To test before it, a person
has Proposals, Dry Run and Trials ([ADR 0016](docs/adr/0016-scratch-cache-copy-not-undo.md),
[ADR 0005](docs/adr/0005-schema-operations-non-undoable-uow.md)).
_Avoid_: commit, save, publish, roll back

**Apply Authorization**:
The check every Apply passes on both the App and CLI paths: the Readiness evaluation (see **Readiness**).
It is not a grant or a credential, and it is not anyone's approval — nothing in Motif authorises an Apply.
_Avoid_: approval, token, permission

**Conflict**:
A loud, derived condition in which the language project's applied history and the Motif store disagree
about a Proposal. It is shown ahead of ordinary workflow states until a person resolves it, but it is not
itself a Proposal lifecycle state.
_Avoid_: drift, merge conflict, failed assessment

**Receipt**:
The record that one Proposal was applied to one project, naming the before and after state. The
durable edge in a project's history. It names whether a person applied the Proposal or an agent did
in Autonomous mode, and carries a short message. A person's message defaults to the Proposal's label, so
they can later recognise what they did; an Autonomous Apply must give one.
_Avoid_: application receipt, success result, audit log

**Parsimony review**:
An advisory Report that offers specific grammar recommendations along two named axes: parsimony and
restrictiveness. Each recommendation states its evidence tier, scope, and limitations; it does not decide
whether a grammar is good or a change may be applied ([ADR 0060](docs/adr/0060-parsimony-evidence-and-advisory-dispositions.md)).
_Avoid_: grammar fitness, grammar score, health check

**Parsimony recommendation**:
A specific Parsimony finding about one grammar object or group, tied to its exact identity, evidence, and
recipe or stated limitation. Its evidence digest is separate from its stable finding identity.
_Avoid_: lint, warning, verdict

**Measure**:
One fixed Parsimony check registered in a Motif build, such as repeated ad hoc prohibitions or a reviewed
negative that still parses. It names the axis it serves, the evidence it needs, and its Recipe; the
`parsimony measures` command lists them. Not the verb "measure" for what an Assessment or a Trial does.
_Avoid_: rule, lint, check (alone), metric

**Measure group**:
One of the two sets the registered Measures fall into, chosen by what a Measure reads: the Grammar group or the Text
group. `parsimony --group grammar|text|all` runs one or both. A group is not a score
([ADR 0080](docs/adr/0080-parsimony-runs-in-two-groups-grammar-and-text.md)).
_Avoid_: tier (alone), suite, profile

**Grammar group**:
The Measures that read only the grammar and need no Texts, no Selection and no Assessment. A Grammar-group run
captures a Baseline itself when none is current. It may still abstain on incomplete grammar facts.
_Avoid_: static group, automatic group

**Text group**:
The Measures that read Approved analyses of chosen Texts or a parser Assessment of them: the text and parser tiers.
It runs on the Parsimony Selection.
_Avoid_: manual group, evidence group

**Parsimony Selection**:
The saved Selection of Texts a person chose by hand for the Text group, reused by every Text-group run until
changed. It is separate from the Default Selection, though both use the same stored Selections. When it is not
configured, running both groups runs the Grammar group and says once how to configure it.
_Avoid_: Parsimony corpus, test set, Default Selection (a different saved choice)

**Recipe**:
The guidance attached to one Measure: what a finding means, which evidence supports it, what a
person may reasonably fix, keep, ask or defer, and the update a Draft can stage for it. A recommendation
without a Recipe states what information is missing instead.
_Avoid_: fix, autofix, script, playbook

**Kept view**:
A drill-down result a Parsimony finding points to, computed while the grammar facts were still open and stored with
its bundle. The facts are then discarded, so a view no finding points to answers "not kept" and needs the measures run
again ([ADR 0086](docs/adr/0086-parsimony-keeps-reports-and-drill-down-rows-and-discards-grammar-facts.md)).
_Avoid_: cached view, frozen facts

**Parsimony note**:
An information line in a Parsimony review that is never a recommendation: either a check that could not look
(for example, no Approved words in the Parsimony Selection) or an authored statement that has no effect on parsing.
It never reads as clean and never asks for a decision.
_Avoid_: warning, issue, skipped

**parsimony**:
The axis that favors fewer or less-duplicated grammar statements while preserving the known valid readings.
It is distinct from restrictiveness, which concerns how narrowly a statement licenses forms.
_Avoid_: simplicity score, rule count alone

**restrictiveness**:
The axis that favors the narrowest grammar statement that still accounts for known valid forms and excludes
explicitly forbidden readings. An unattested form is not a negative without a declared universe and suitable
negative evidence.
_Avoid_: over-generation score, missing-text form

**Reviewed negative**:
A human-confirmed forbidden form or reading used as negative evidence. A FieldWorks Disapproved analysis is
a negative for that reading only, not for the whole word.
_Avoid_: rejected word, parser failure, absence

**Disposition**:
A person's durable choice for one Parsimony recommendation: fix, keep, ask, or defer. It is tied to the
recommendation's exact identity and relevant evidence; new relevant evidence brings the item back for review.
Disposition data belongs to the FieldWorks project as Notebook judgment revisions
([ADR 0056](docs/adr/0056-human-judgments-belong-to-the-fieldworks-project.md)).
_Avoid_: approval, Apply authorization, dismissal

**Human judgment**:
A durable choice about a Parsimony recommendation, a human-confirmed forbidden example, or a withdrawal of
an earlier choice. Each revision stays with the FieldWorks project; its reason is optional. A changed reason
does not silently change the decision or its evidence.
_Avoid_: annotation row, suppression flag, approval

**Project initialization**:
The deliberate, confirmed preparation of a FieldWorks project for Motif Proposals. It establishes the place
for durable human judgments once, before any are recorded; opening a project or applying a Proposal does
not perform it implicitly.
_Avoid_: automatic setup, migration, field repair

**Suppressed set**:
The always-inspectable Parsimony recommendations kept or deferred under their exact relevant evidence.
Relevant new evidence returns a recommendation to the active list with its earlier reason.
_Avoid_: deleted findings, dismissed warnings, hidden items

**Report**:
A query over stored evidence that produces statistics and findings. A Report can use an Assessment, frozen
Parsimony evidence, or both; a Parsimony Report may have no Assessment and is always advisory. Configured
regression behavior for other Report kinds follows [ADR 0042](docs/adr/0042-a-job-produces-assessments-an-assessor-makes-them.md).
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
nothing has to be re-derived. Its words are stored in NFD, as FieldWorks stores them, so a Trial and an
Assess of one word agree.
_Avoid_: query, sample, filter, scope, subset, test set, corpus descriptor

**Phonology Example Set**:
A named, immutable revision of explicit underlying forms paired with expected surface forms, declared
generation context and optional Citations. Stored in the project database and embedded by exact revision
and executable content in a Phonology Check. It is distinct from a Selection of words to parse; it adds no
permanent lexical entries ([ADR 0065](docs/adr/0065-phonology-example-sets-are-advisory-evidence.md)).
_Avoid_: word list, corpus, Selection, gold analyses

**Phonology Check**:
An explicit Worker job that measures a frozen Baseline or candidate against a Phonology Example Set
revision and produces a real PhonologyExamples Assessment. Per-example results are agree, disagree or
unavailable, with completion and source binding recorded separately. A missing expectation is a
disagreement only after a supported check finishes. Stored reads never run a check automatically.
_Avoid_: Trial, grammar check, Readiness check

**PhonologyExamples Assessment**:
An immutable advisory Assessment of supplied underlying forms and authored surface expectations under
recorded generation context, grammar, parser and limits. Agreement measures expected-form membership,
not linguistic correctness or exhaustiveness. Missing context, an empty lexicon, zero examples and
incomplete generation cannot pass. It never satisfies Readiness or changes Apply.
_Avoid_: Correctness, verdict, approved analysis, Apply gate

**Default Selection**:
The Selection a project measures when nobody names another: chosen Texts plus any added words, stored in
the Motif store with the project and resolved to an exact word list and digest on every run. It also records
the per-word parser work limit and its time-limit policy. Chosen in the setup dialog once the project's first Baseline
exists, and changed from Configure. Each Assessment keeps the list and resolved limits it used, so changing
the Default Selection never rewrites what an earlier run measured.
_Avoid_: default corpus, word set, profile, preset

**Selection time-limit policy**:
Whether a Selection's per-word time limit follows Motif's estimate for its parser work limit or uses an explicit value
chosen by the person. An unbounded parser work limit has no time limit and no explicit override.
_Avoid_: inferred preference, hidden override

**Overview**:
Where a project stands, read from what the last run stored: the project's counts, Text Coverage, accuracy
against the manual analyses, timing and warnings. The window's first page and the `overview` command show the
same numbers, computed once. A projection of stored evidence, never a trigger to rerun. Its Look First rows are
advice that ranks and links; one names the entries with a stem not tagged with an inflection class, and none of
them stops an Apply, a Draft or an edit.
_Avoid_: dashboard, summary, health, status, to-do list

**Refresh**:
The first of two things a person does to bring Motif up to date with the FieldWorks project: Refresh captures a
new Baseline, then Parse all words measures the saved Default Selection against it. Both steps are started by a
person, never on their own. Motif records when each Refresh happened and when the FieldWorks project had last been
saved, so the window can say how far behind it is. Coming back to the window only re-reads what Motif has already
stored.
_Avoid_: reload, sync, auto-refresh, update

**Review Changes**:
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
Project workflow records — Proposals, Drafts, jobs, Assessments, Reports, Receipts, Harvests, the Corpus index,
stored corpus analysis and its durable summaries, and the applied index — live in the project's paired sibling
database. Primary Corpus files and their metadata sidecar live under the project folder's `corpora/` directory
([ADR 0094](docs/adr/0094-corpus-files-harvesters-and-stored-word-analysis.md)). Parsimony Report bindings are stored here,
but reviewed negatives and dispositions remain durable data in the FieldWorks project. File-backed
Baselines and disposable parser and grammar-analysis artifacts live separately under the Motif worker root, which defaults on Windows to
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

**Advanced AI mode**:
An installation-wide choice that lets AI assistants use Motif's MCP tools on a person's projects. An assistant
may read, draft, Dry Run, Trial and Finalize, but never Apply: Apply is always a person's act.
_Avoid_: AI mode, agent mode, MCP mode

**MCP server**:
The `motif mcp` process through which an AI assistant reaches Motif's typed commands as tools. It is the
Motif API presented as Model Context Protocol tools, not a second API: every tool is a catalogued command,
each tool names its project by Known project name or recorded path, and the server shows no tools and
refuses every call while Advanced AI mode is off. It is how Consultant mode
works; Autonomous mode uses the CLI instead.
_Avoid_: agent server, AI endpoint, Motif service

**Consultant mode**:
An AI consultant works with a linguist through the MCP server. It may build, Trial, Dry Run and Finalize
Proposals, and queue them Ready, but it never Applies. Nothing reaches the language project without the
linguist knowing.
_Avoid_: supervised mode, MCP mode, linguist mode

**Autonomous mode**:
An AI acting as an autonomous linguist through the CLI, usually started by a developer to take a grammar
from source to working parser. It may Apply. It never forces past Readiness, and it applies only with
measured evidence that parsing did not regress.
_Avoid_: auto mode, unsupervised, API mode, "the API" for the CLI

**Text**:
FieldWorks' term, kept for FieldWorks' meaning: an interlinearised document **in the language project**.
Never used for a Corpus or a Document, which Motif holds outside the FieldWorks model even when its files
sit in the same project folder.
_Avoid_: using this for corpus material

**Corpus**:
A body of running text collected for a project, preserving its Documents, order, repetitions, source
information and any attributed qualification. Its files are primary project assets outside the FieldWorks model,
with no size cap or automatic eviction. A particular segmentation and analysis interpret that material; neither is the Corpus
itself ([ADR 0094](docs/adr/0094-corpus-files-harvesters-and-stored-word-analysis.md)).
_Avoid_: text, texts, dataset, word list, sample

**Document**:
One source unit within a Corpus, preserving running text in order with repetitions. Its boundary scopes
contexts and counts; n-gram models must not run across it. It keeps its identity when edited; a content hash
distinguishes the version a measurement or Harvest used. It is never a FieldWorks Text.
_Avoid_: text, article, item, record

**Harvester**:
A named, reusable definition for finding material in Corpora and producing or updating a curated FieldWorks
Text, with an explanation, regex or filters, source references and a destination Text. People and agents can
edit it; the definition belongs to the FieldWorks project. It is distinct from a Harvest, which records a use
([ADR 0094](docs/adr/0094-corpus-files-harvesters-and-stored-word-analysis.md)).
_Avoid_: Recipe, corpus group, approval rule, run

**Harvest**:
One recorded run or other derived use of Corpus Documents: what was used, which definition or manual method
was used, and what it produced or updated. It includes manual extraction, linking an imported Text and uses
such as n-gram training; a Harvester and a destination Text are optional for manual or other kinds. Motif keeps
the provenance record, distinct from disposable computed output.
_Avoid_: Harvester, import (as a synonym), Trial, approval

**Wordform**:
A written word form considered independently of any particular place it occurs. Many Occurrences can share
one Wordform; its spelling alone does not decide its language. In this shared analyzer vocabulary it does
not imply a stored FieldWorks wordform object or a spelling judgment.
_Avoid_: token, Occurrence (for the form)

**Occurrence**:
One instance of a Wordform at a particular span in source material, retaining its original surface and location.
Repeated instances remain distinct. A Corpus Occurrence is bound to a Document version and segmentation;
a FieldWorks Text occurrence uses its saved FieldWorks context. Sharing a Wordform assigns no approved reading.
_Avoid_: token, Wordform (for the instance), analysis

**Word-forming character**:
A character that belongs inside a word under a writing system's segmentation rules, including Unicode defaults,
configured additions and WordMaker's numeric rule. Being word-forming does not establish a valid word.
_Avoid_: letter only, Latin word character

**Segmentation profile**:
The recorded writing-system rules used to find word boundaries, with the identity and hash of the source
`.ldml` from which they were obtained. It describes segmentation, not the fuller Assessment scope or a
language judgment.
_Avoid_: Tokenization profile, tokenizer guess, language detector, Assessment scope

**Segmentation alternative**:
Another division of the same source span into one or more parts, alongside the primary FieldWorks-rule split.
Each part retains its own analysis and Language candidates. Alternatives remain visible rather than replacing
the primary split or multiplying the count of source Occurrences. A character can be word-forming in one
interpretation and punctuation in another. If any alternative is analyzed, the Occurrence is analyzed;
misses of its other alternatives are ignored for failure, misspelling and no-analysis counts, flags and squiggles
([ADR 0094](docs/adr/0094-corpus-files-harvesters-and-stored-word-analysis.md)).
_Avoid_: replacement profile, discarded split, correction

**Dictionary pack**:
A data bundle for dictionary and word evidence: Hunspell `.dic`/`.aff` data, a word list and an optional
n-gram profile, with source, version and licence identity. It can support contact-language checking and
suggestions without a grammar. It is distinct from a Language Pack; absence from a limited word list is
not spelling rejection.
_Avoid_: Language Pack, executable plugin, guaranteed lexicon

**N-gram profile**:
A language-specific summary of short-sequence frequencies used as language-detection evidence. It is
optional data in a Dictionary pack, distinct from a Segmentation profile, a grammar or a spelling verdict.
_Avoid_: grammar analysis, exclusive language label

**Language Pack**:
A PanGloss `.pgpack` grammar analysis artifact, also called a PanGloss Language Pack. A contact language
can have one loaded alongside the vernacular grammar. The term means only this grammar artifact;
Dictionary packs supply the separate dictionary resources.
_Avoid_: Dictionary pack, umbrella language-resource bundle, Hunspell pack (as a synonym)

**Language candidate**:
A possible language for an Occurrence or a part of a Segmentation alternative, carrying supporting evidence
and, when applicable, a misspelling flag and correction suggestions. Evidence can come from Dictionary packs
or Language Packs, including another loaded grammar for a contact language. Several candidates can coexist
with the vernacular parse. A suggested correction is not valid-word evidence for the original spelling.
Candidate alone keeps its FieldWorks analysis meaning.
_Avoid_: exclusive language label, confirmed language, Candidate (alone)

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
> | **Text Coverage** | What share of the Default Selection's words in the project's vernacular writing systems, and of their occurrences in the chosen Texts, parse. Digit-only Wordforms and their Occurrences, and words in other writing systems, are counted separately; words the parser refuses as invalid shape stay in the share. Measured over the project's own Texts; the same share over a Corpus is grammar coverage | Motif |
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

## Evaluating agents

**Episode**:
One agent's work on one task, from a fresh copy of the project to its frozen final state. A comparison runs
many Episodes per task and arm. Inside an Episode the agent may run many Trials of its own Proposal.
_Avoid_: trial, attempt, sample, run

**Arm**:
One side of a comparison: a model at an effort level, an MCP profile, and a version of the shipped skills.
Two arms differ in one thing, and only that thing is being measured.
_Avoid_: variant, config, condition, branch

**Hold**:
The verdict on a change whose evidence does not settle it: it is not shipped, and the record says what
evidence would settle it. Running more Episodes until a change wins is not a way out of Hold.
_Avoid_: inconclusive pass, soft accept, neutral

**Oracle**:
A versioned recipe — these reference materials, these models at this effort, these rules — that produces
answer keys and rulings for agent tasks. Every key or ruling is cited with the Oracle version that produced
it. It is the best judgement available, not the truth, and a person checks a sample of each version's
rulings before it is used, after a devil's advocate has tried to break each key. The model under test may
sit on the Oracle; a result says when it did.
_Avoid_: oracle pass, gold model, ground truth

**Judge**:
A fixed pair of big models from different families that decide, each independently, whether a free-text
answer meets each point of its key and breaks none of its prohibitions, quoting the answer for every point
they find. The same pair judges every arm. When they agree, that is the verdict; when they disagree, the
answer is unscored until a person rules. The pair qualifies by agreeing with the Oracle's rulings, and a
result names the Oracle version it was judged against. In the big-tier release check, the other family's
model judges alone and the record marks the verdict "single judge"; a person reviews every failure and
every task whose outcome changed since the previous release check.
_Avoid_: grader, evaluator, scorer

**Agent failure**:
An Episode that ends without a correct result for a reason the agent owns: a wrong or unappliable
answer, an invented identity, or running out of its budget while still working. Scored as a failure.
_Avoid_: infrastructure failure, error

**Cloud failure**:
An Episode cut short because the model service failed or stalled. Retried; never counted against the agent.
_Avoid_: infrastructure failure, timeout, outage

**Harness defect**:
An Episode cut short because Motif, the parser, the grader or the isolation around them failed. Never
counted against the agent, and the task stays out of comparisons until the defect is fixed.
_Avoid_: infrastructure failure, flake

**Agent budget**:
The turns, tool calls and tokens an agent may spend on one task. Time is never part of it, so a slow host
cannot fail an Episode; a separate safety wall kill only stops an Episode that never ends, and hitting it is a
Harness defect.
_Avoid_: wall limit, timeout, time budget

**Price to solve**:
Everything an arm spent on its scored Episodes divided by the number it solved, so failed Episodes count
against it. The headline cost of an arm; average cost per Episode is reported beside it for comparison
with published leaderboards.
_Avoid_: cost per success, cost-of-pass

**Validity**:
The evidence that every task and grader works against the exact product, parser, profiles and task sets an
experiment will run: gold answers pass, empty and tempting wrong answers fail. It is established again
whenever any of those changes, before an agent is run.
_Avoid_: self-check, smoke test, sanity check

## The plugin and its skills

Motif publishes the `motif` plugin: five skills (a FieldWorks expert, a parsing expert, a linguistic
consultant, a Motif workflow and a Parsimony review) that an AI assistant installs beside the MCP server. That
is a bounded context of its own with its own vocabulary — plugin, marketplace, skill, corpus, index, crosswalk,
persona, job map — in [plugin/CONTEXT.md](plugin/CONTEXT.md). Inside the plugin, **corpus** means a
documentation source, not the text corpus defined above.
