# Grammar Design Records

**In plain terms:** each language project gets a set of short pages, one per grammar topic, that say why the grammar is
built the way it is. People and agents read them before changing the grammar, and a Proposal can update them as part of
its changes. The pages live inside the FieldWorks project, so FLEx users see them too. Around that, Proposals gain a
discussion (a Rationale that can be revised until decision, and Comments), pictures, a one-line change list, and a link
that opens the project as it would be after the Proposal. When the project has moved on, a person gets a plain choice
for each change that no longer fits.

**Status:** approved by the owner 2026-11-02. Decisions only; nothing is built. Records in
[ADR 0083](../../adr/0083-grammar-design-records-live-in-the-fieldworks-project.md),
[ADR 0084](../../adr/0084-proposal-discussion-rationale-comments-and-pictures.md) and
[ADR 0085](../../adr/0085-proposal-snapshots-are-disposable-derived-files.md). Terms are in `CONTEXT.md`. The Markdown
form of GDR text and its converter are specified separately, in
[FieldWorks Markdown, and the GDR template](2026-11-03-gdr-markdown-design.md), and are only referenced here.

## 1. Grammar Design Records

A Grammar Design Record (GDR) is a living page about one grammar topic. It is rewritten in place and keeps one stable
title and ID. The history of why it changed stays in the Proposals that touched it; the page lists them, and there is no
separate history. Direct FLEx edits do not appear in that list.

- **Home.** A custom list in the FieldWorks project named "Grammar Design Records". Name is the title, Abbreviation is
  `GDR-n`, Description is the abstract, Discussion is the body, and sub-items are its outline. No FLEx change is needed;
  collapsing the outline is Motif only. The project is the store, so GDRs travel with it ([ADR 0083](../../adr/0083-grammar-design-records-live-in-the-fieldworks-project.md)).
- **ID.** Permanent, never reused, independent of position. Order is stored separately. A deleted GDR keeps its ID and
  last title.
- **Status.** Each outline topic shows Unreviewed, Described, Not applicable (with a reason) or Deferred.
- **Three ways to edit.** In FieldWorks a person edits directly. Through a Proposal an edit is a Motif operation that takes
  effect at Apply; this is the only route for agents. In Motif a person's edit joins the one Draft with the person's other
  pending changes and is applied with them by one Apply, after review on Review changes. No second list and no separate
  small Proposal.
- **Base text and conflicts.** An edit to a GDR remembers the text it started from. If the GDR changed since, that GDR
  alone is stopped and the item is handled as in section 6.
- **No comments on GDRs.** Comments belong to Proposals.
- **Seeding.** People seed the pages; agents then draft through Proposals in batches, citing the objects they describe,
  and a linguist reviews. A Proposal does not have to update a GDR.
- **No grammar data in the repository.** Examples and words in GDRs stay in the project ([ADR 0076](../../adr/0076-grammar-data-never-belongs-in-motif.md)).

## 2. How agents get GDRs

Agents are shown what exists without being flooded. The index is delivered when it is useful, not pasted every time.

**Levels.** L0 is the ID, title and status of every GDR, always available. L1 is the abstract, delivered automatically for
GDRs linked to an object the agent touches. L2 is the full body, fetched by ID or by search. Every read is logged and
shown on the Proposal page. Finalize only warns about a missing read; it never blocks.

**Moments.**

| Moment | What the agent receives |
|---|---|
| Session start | One line of about 30 tokens saying GDRs exist and how to fetch them |
| A Proposal is started | The L0 index |
| The agent touches an object a GDR links to | That GDR's L1 abstract, with the touched object's result |
| The agent needs detail | The L2 body, by ID or search, on request |
| Finalize | A warning, never a block, if linked GDRs were not read |

**Links.** A GDR is linked to objects by the references in its body. Finalize can name touched objects that no GDR
mentions, so a gap in the records shows up where it is made.

**Skills and the assistant.** The assistant injects the index at the right moment through the MCP instructions and
the open-project result; the authoring tools refuse until the index has been fetched for the Proposal. Skills explain how
to read and write GDRs but do not paste the index.

## 3. Proposal page

Today Rationale and the person's note are columns on the Proposal row, and MCP tells an agent when the note changed.
This section replaces the note with Comments and keeps that notice.

The Proposals page grows to hold the discussion ([ADR 0084](../../adr/0084-proposal-discussion-rationale-comments-and-pictures.md)).

- **Rationale** keeps its name and is the main description. People and agents edit it while the Proposal is open; its
  history is kept until the decision, then dropped. The latest text stays.
- **Comments** replace the single note. They can grow. Agents add comments, attributed to them, and never edit or delete
  one. A person can add, edit and delete any comment. Attribution reads "Claude (Opus 5.5) for John".
- **Freezing.** Rationale and Comments are editable while the Proposal is open and frozen at decision.
- **Retention.** The latest Rationale, Comments and pictures stay as long as the Proposal exists; manual delete of a
  never-applied Proposal stays, and takes them with it.
- **Pictures** are stored in `Project.motif.db`, scaled down to about 1 MB each. They count toward the 100 MB store budget (ADR 0082), are authored data that a prune
  never removes, and a store full of pictures reports `OverBudget` like any other.
- **Reads.** The Proposal page shows which GDRs the agent read, at which level.

## 4. One-line change list

Review changes shows one line per changed grammar object, in simple text, with a hard line limit: for example "update
lexical entry" or "add sense" followed by the new text. It is enough to know what changed; the full change is behind the
link. It shows only what is easy to display. It extends Review changes; there is no second list.

## 5. Snapshot

A Proposal snapshot is a copy of the project as it would be after the Proposal, opened in FLEx through a link
([ADR 0085](../../adr/0085-proposal-snapshots-are-disposable-derived-files.md)).

- Named "<project name> - Proposal 12" (no `#`, which FLEx forbids in project names).
- Built on demand from the pinned Baseline, by saving the scratch a Dry Run already produces. Kept until the Proposal's
  content changes or it is decided, then deleted.
- Kept in a Motif-owned folder outside the Projects folder, so the project picker does not list it, and not part of the
  Motif store. They count toward the 5 GB per-user budget (ADR 0082) and, being derived, are the first thing a sweep may
  remove; a removed snapshot is rebuilt on the next click. Repository data (`.hg` and LIFT) is stripped so a snapshot cannot act like a Send/Receive project.
- No base copy is kept. Staleness is shown for the Proposal as a whole, with an offer to update and refresh.
- Edits typed into a snapshot are not harvested by Apply. Apply uses the Proposal and checks the live project.

## 6. Update to current project, and items that no longer fit

When FieldWorks has changed since a Proposal's Dry Run, the window offers **Update to current project**. This is the
user-facing word for rebasing; the glossary term is **Update baseline**, and "rebase" is not used with non-coders. The
engineering rules are in `docs/conflicts-and-rebase.md`, which this section does not change. No Update to current project command exists yet; it is build step 7.

An item that no longer fits is shown as one line, with a banner on the Proposal. The three choices apply to every item
in the Draft and in Proposals, including approvals and misspellings. Each such item offers:

- **Use mine**: keep the Proposal's version. Unavailable, with the reason given, when it is impossible.
- **Use FieldWorks**: take the current project's version.
- **Fix with chatbot**: the agent is given the before, the change and the current state through MCP. It amends the item
  in a new revision and adds an attributed standard Comment saying what it did. The person applies it; Undo stays.

A person can view both versions and copy from either to fix by hand, for example by choosing FieldWorks and retyping.
This applies to the Draft and to Proposals. On a Proposal a Comment records which choice was made. There is no three-way
merge screen and no "Fit decision" term; **Conflict** and **Drift** keep their present meanings.

## 7. Text and editor

GDR text is stored in FieldWorks as StText, never as Markdown. Its Markdown form, FieldWorks Markdown written to the
GDR template, is a view that agents and people can both read and edit, through an importer, an exporter and a checker, and
the editor has a source mode that shows it. Both are specified in
[FieldWorks Markdown, and the GDR template](2026-11-03-gdr-markdown-design.md). This replaces the earlier rule that
Markdown was for agents only and that there was no source mode
([ADR 0089](../../adr/0089-gdr-text-is-human-editable-fieldworks-markdown.md), owner decision of 2026-11-04).

Per the owner's decision of 2026-11-02, FieldWorks needs a new editor anyway, so one editor serves both FieldWorks and
Motif's GDRs. It targets Avalonia 11 and 12 and is built from FieldWorks code plus a small Markdown importer, exporter and
checker. Tiptap is rejected. The part with no FieldWorks code to take is the text surface itself, which is new, built on
Avalonia's text layout.

People edit with this editor, and may also edit the Markdown in its source mode; agents use Markdown. Text carries its
writing system on every run, and the editor switches the default vernacular and default analysis systems, with others
from a list. Tables are paragraphs of cells separated by tabs, in a table-row paragraph style.

## 8. The outline

Twenty-three topics in seven groups, all shown on every GDR set. The one-line prompts are Motif's own wording.

**Scope**
1. Language, variety and scope: which language or variety this grammar covers, and what it leaves out.
2. Word origins and borrowing: how words of other origins are treated.

**Writing**
3. Writing systems and spelling: which scripts and spellings the data uses.
4. Word boundaries, clitics, punctuation: what counts as a word, and what to do with attached small words and marks.
5. Spelling variants and old/new conventions: which variants are the same word, and which convention wins.

**Sounds**
6. Phonemes, features, natural classes: the sound inventory and how it is grouped.
7. Syllables, stress and tone: how sounds are organised above the segment.
8. Sound processes (vowel harmony, assimilation, deletion): the changes sounds undergo in context.
9. Rule ordering and exceptions: how processes interact, and where they do not apply.

**Morphemes and word classes**
10. Morpheme types and segmentation: what the parts of a word are called and where to cut.
11. Word classes: the categories words belong to.
12. Inflection classes and noun classes: how words of one category pattern differently.
13. Irregular, suppletive and zero forms: forms that do not follow the pattern or show no mark.

**Inflection**
14. Noun inflection: what nouns inflect for.
15. Verb inflection: what verbs inflect for.
16. Agreement: what must match what.
17. Affix positions and co-occurrence: where affixes sit and which can combine.

**Word building**
18. Allomorph selection: when a morpheme takes one shape or another, stated as a rule or a list.
19. Reduplication, infixation and other non-linear forms: forms built by copying or insertion.
20. Derivation, voice and valency: how new words and argument patterns are made.
21. Compounding: how words join.
22. Exceptions and false parses: known cases the grammar must not get wrong.

**Beyond the word**
23. Syntax and discourse, as far as they affect words: only the parts of clause and text structure that change a word.

## 9. Build order and ownership

gui-lead builds, in this order. Cebuano goes first: its GDRs are drafted from its recorded decisions through Proposals,
and any gap found is a Motif defect to fix.

1. Proposal page: Rationale history, Comments, attribution, pictures.
2. Read-only GDRs: list, seed, MCP levels, read logging.
3. One-line change list in Review changes.
4. Snapshot link.
5. FieldWorks Markdown, converter and GDR writes: the separate Markdown spec, then the importer, exporter and checker.
6. The shared editor for people (FieldWorks and GDRs), after its first proof: a text surface on Avalonia's text layout
   behind the existing preview gate, which must pass typing in the active writing system, mixed-direction text with a
   correct caret, Keyman, and paragraph split, and compile against Avalonia 11 and 12.
7. Update to current project.

GDRs and the snapshot link are not "FieldWorks work, done last". The editor in step 6 is FieldWorks-facing work and is
sequenced by the owner's decision above.

## 10. What is open

- The editor prototype, and any package, licence or code-reuse choice, are the owner's call. In particular FieldWorks code
  is LGPL and Motif is MIT; whether copied code needs relicensing, or a separate library, is undecided.
- Native AOT for Motif with that editor is unproven.
- Screen-reader support, mixed-direction behaviour on Avalonia 11 and Keyman on a new text surface are unproven.
- Whether the FLEx tab alignment for GDRs matches what FLEx shows is unchecked.
- Snapshot disk and time costs are unmeasured on real projects.
