# 0005. The Grammar Guide

**Status:** Accepted, 2026-11-14.

**Amended 2026-11-17:** Hand-analysis annotation and its full-fidelity Home boundary.

## Context

A readable current account of a grammar must travel with its FieldWorks project. Proposals explain changes;
the Grammar Guide explains how the grammar works, its evidence and its judgement calls.

## Decisions

### D1. The Guide is one tree in the FieldWorks project

The custom possibility list named **Grammar Guide** stores Guide parts and Guide sections, each
with a permanent ID and title. Name is the title, Abbreviation the number/ID, Description the summary and
Discussion the body; sub-items form the outline. A **Guide part** is not a chapter: chapter means a scripture
Passage. Motif's database holds none of the Guide text. It is project data under
[ADR 0002](0002-grammar-data-never-belongs-in-motif.md).

### D2. A Guide section has a fixed template

The body's parts, in order, are **How it works**, **Examples**, **Grouping commands**, **Exceptions**, **Decisions**,
**Open questions** and **In the grammar**. Judgement calls and their reasons go in **Decisions**, not standalone records.
A part is `#`, a section `##`, and a template part `###`; headings stop at `###`. The template adds no
operation kinds and no Markdown profile beyond the two fenced block kinds under Grouping commands below. Generic field operations and the shared checker/importer in
[ADR 0006](0006-rich-text-is-fieldworks-markdown-in-the-shared-textediting-field.md) carry the edits. Tables
use **Table Header**, **Table Row** and **Table Cell Separator**.

#### Grouping commands

**Grouping commands** holds queries that gather the corpus evidence behind the section's claims, such as the words
that share an ending or the stems that take one affix. It stores definitions and display directives only. A query
result is never stored: it is computed each time the section is rendered or compared, so
there is no saved table and nothing that can go out of date. Both blocks below are the only fenced block kinds the
template admits; the importer reads them as authored text, and every other fenced block stays ordinary Markdown.
A section or Proposal may hold many queries, and one query may be shown several times.

A query is a fenced block tagged `motif-query`. Each line is `key: value`; the keys are `id` (unique in the section,
lowercase letters, digits and hyphens), `title` (one plain-language line saying what the query groups), `run`
(exactly one read-only `motif` command, without `--project`, which the renderer supplies), `columns` (optional,
comma-separated result columns to show) and `sort` (optional, a column, then `asc` or `desc`).

````text
```motif-query
id: dropped-letters
title: Alternate forms that lose a letter, grouped by the letter lost
run: motif query forms --where kind=alternate --group letters.dropped
columns: letters.dropped, count
sort: count desc
```
````

A display directive is a fenced block tagged `motif-show`. The keys are `query` (the `id` of a defined query),
`show` (optional: `table`, `counts` or `list`; default `table`), `limit` (optional, a positive number of rows)
and `columns` (optional, overriding the query's own).

````text
```motif-show
query: dropped-letters
show: counts
limit: 10
```
````

A Proposal's before and after run each query with `--as-of <base revision>` and with `--as-of <result revision>`, so a
verb used in a query must accept `--as-of`. `--as-of` names a project revision and is distinct from `query stems --at`. Reading the Guide never executes a query; the renderer and
`guide check` do. Export carries the definitions and directives only: it never runs a query and never writes a result.

`guide check` (a later slice) reports a query that does not parse, uses a verb that is not read-only, or does not
execute, and a directive whose `query` names no defined id. As the rest of the check does (D9), these findings
are advisory and do not block Finalize, Apply or Trial success.

### D3. One outline and a human-editable export

Guide parts follow Payne's *Describing Morphosyntax* order; syntax sections follow PAWS section order.
Empty parts are hidden from readers. Claude renames and re-sorts CEB's existing list through a Proposal,
with no migration script, preserving permanent IDs and links. Export writes one `*.fw.md` per Guide part
and `index.fw.md`, using unchanged FieldWorks Markdown.
A person may edit the Markdown and return it through the same checker, Dry Run and Proposal Apply as an
agent. FLEx's own Grammar Sketch is left alone; the Guide is Motif's grammar sketch.

### D4. Generated material is a view, never authored storage

**In the grammar** and live examples are generated from grammar data and approved analyses at view/export
time. Generated content sits in marked blocks that the importer ignores, so importing an edited export
never stores it. This is decided; the concrete marker spelling, filenames and index contents still need
specification. The objects and approved analyses supplying each section remain Open below.

### D5. All Motif Guide edits use the Proposal path and remember their starting text

A person may edit directly in FieldWorks. In Motif, a person's edit joins the pending Draft Proposal; an
agent writes only through a Proposal's operations. Project writes use the same Apply as other changes.
Edits retain the text they began from. Drift identifies the field that no longer fits and holds the whole
Apply until resolved, under ADR 0006. History is the Proposals touching the section, with no separate history;
direct FLEx edits do not appear in that list.

Agents receive Guide content at increasing depth (ID/title, summary, body) when starting a Proposal and
when touching linked objects. Reads are logged; a missing-read warning at Finalize never blocks. Each plugin
grammar update names the section it carries out, or edits one, with evidence verses. Claude's plugin
playbook requires that link before Finalize; this is distinct from a Guide-check finding becoming a gate.

Guide examples may be derived from approved analyses. Recording a hand analysis never writes the Guide;
agent grammar explanations and every Guide edit retain the Proposal path.

### D6. An original starter interview seeds sections and grammar together

Claude asks about twenty questions, requests two or three examples and allows every question to be skipped.
Topics cover writing, word classes, noun number/possession/case, verb person/TAM/negation, agreement,
derivation, compounding, reduplication, clitics, affix order/position, allomorphy, inflection classes,
morphophonology and tone/stress/length. Use original wording from the approved morphology-interview research;
the questions live in
`plugin/skills/linguistic-consultant/references/starter-morphology-interview.md`, linked from the consultant
skill and README and named by the motif-workflow skill.

Answers are **Known**, **Working guess** or **Not sure**. One Proposal drafts the sections and grammar objects;
working guesses go to **Open questions**. Skip writing questions if a writing system already exists.
Uncertainty is never silently promoted to a settled decision.

### D7. The Guide check is a read of the captured project

The CLI uses `guide …` throughout, with no `gdr` aliases. `guide check --project <fwdata> [--json]` is a
Developer read on the current Baseline's project copy and paired store: no unit of work and no parse. Only
link runs cite a grammar object; writing a rule's name in prose
does not. Links identify objects by GUID. A missing local object is reported with its section and heading;
links to another project are not looked up.

The counted kinds are phonological rules, compound rules, ad hoc rules, exception features, affix templates,
stem names, inflection classes and affix processes. A template covers its slots. Phonemes, natural classes,
environments, features, categories, strata, morph types and entries with ordinary allomorphs are inventory,
not one-required-section-per-object. An absent kind costs nothing. A section covers every object it links
or whose owner it links: an entry covers its affix processes, a category its templates, stem names and
inflection classes. Several sections may cover one object. Report per kind, with at most ten example names.

### D8. Stored Trial rows check explicit example claims without a parse

Under a heading starting with **Examples**, vernacular-WS tokens separated by tabs/spaces, trimmed of
surrounding punctuation, are positive examples. Ignore tokens starting or ending with hyphen or equals
sign, and analysis-language glosses. Under **False parses** or **Does not parse**, they claim no analysis.
A positive example needs an analysis; entries, forms and MSAs linked in its paragraph must appear in some
analysis by their own or their entry's GUID. A negative example needs none. Links to rules, categories or
templates do not make claims that morpheme rows can test.

Use each word's newest conclusive `analysed` or `no-analysis` outcome among the newest 25 ParseTime,
Correctness or ObjectTiming word Assessments. Timed-out, capped and skipped rows are inconclusive, so an
older conclusive row may answer. With none, report **unverified** and ask the next Trial to include it.
This check's token convention does not invent a new stored example format; aligning it with generated live
examples and the current section template remains an Open source-selection question.

### D9. Hypothesis links and Trial review share the advisory check

A passed hypothesis in [ADR 0011](0011-the-hypothesis-ledger-lives-in-the-motif-store-and-the-cli-writes-it.md)
that names a Guide section must find its stable `GDR-n` ID and named heading, title or summary (`Abstract`
in the stored checker contract). Missing targets are reported.

A Trial runs the check once on its post-Proposal scratch project, on the lane just after Dry Run, and keeps
the response in its review ledger. Review summary field `GdrSync` and the text report carry advisory findings.
A check unable to run leaves it absent. Its example evidence predates this Trial because this Trial's own
rows do not yet exist; newly changed examples are checked at the next Trial. Finalize cannot substitute a
Baseline check for the missing post-Proposal project.

Every finding is advisory under [ADR 0004](0004-linguistic-decisions-follow-black-and-judgement-calls-are-the-linguists.md).
`InSync` means no findings; a warning never blocks a Proposal, Finalize, Apply or Trial success.
The check adds at most one second, O(rules + sections + examples): one pass over objects, one over
paragraphs/runs and one batched `(AssessmentId, Word)` lookup capped at 25 Assessments. It never scans the
ledger text or corpus. Report Baseline-copy open time separately.

## Open

- Which Payne and PAWS editions fix the outline, crosswalk and numbering, and how are existing IDs and
  links preserved during re-sorting?
- Which grammar objects and approved analyses supply each section, and what appears when evidence is
  absent? Align the stored-row check with the template and generated live examples.
- How do skipped and **Not sure** answers map to Draft content and grammar objects?

## Consequences

People and Claude maintain one current story alongside the grammar, with uncertainty and reasons visible.
Authored content travels with FieldWorks; generated explanations follow current grammar/evidence without a
second stored copy. Template and checker changes must preserve stable links and the advisory boundary.
