# ADR 0083 — Grammar Design Records live in the FieldWorks project

**In plain terms:** the short pages that explain why a language's grammar is built the way it is are kept inside the
language project itself, as a list FieldWorks already knows how to store. People can read and edit them in FLEx, they move
with the project, and Motif changes them only the way it changes everything else: through a Proposal, applied by a person.

**Status:** accepted, 2026-11-02. Design: `docs/superpowers/specs/2026-11-02-grammar-design-records-design.md`. Adds
**Grammar Design Record** to `CONTEXT.md`. Follows [ADR 0056](0056-human-judgments-belong-to-the-fieldworks-project.md), which already keeps human judgments in
the FieldWorks project, and [ADR 0079](0079-linguistic-decisions-follow-black-and-judgement-calls-are-the-linguists.md):
a GDR records a judgement call and Motif does not second-guess it. Does not touch [ADR 0076](0076-grammar-data-never-belongs-in-motif.md).

## Context

A grammar's reasons are scattered across Proposals, chat and memory. Proposals say what changed; they are poor at saying
what the grammar currently is and why. Agents need a current page to read before they change something.

## Decision

1. A Grammar Design Record (GDR) is a living page per topic, with one permanent ID and title. It is stored in the
   FieldWorks project as a custom possibility list named "Grammar Design Records": Name is the title, Abbreviation the ID,
   Description the abstract, Discussion the body, sub-items the outline. FieldWorks needs no change.
2. The project is the store. Motif's own database holds none of the text. GDR content is therefore project data and,
   per ADR 0076, never enters the Motif repository, including as examples or fixtures.
3. A person edits in FieldWorks directly. In Motif a person's edit joins the one Draft and is applied by Apply. An agent
   writes only through a Proposal's operations. Every route that writes the project is the same Apply the rest of Motif uses.
4. A GDR edit remembers the text it began from. If the page changed since, that page is stopped and its item no longer
   fits, handled like any other (see the design, section 6).
5. History is the Proposals that touched the page. No separate history is kept.
6. Agents are shown GDRs in levels (ID and title, abstract, full body) at the moment they start a Proposal and when they
   touch linked objects, and reads are logged. Finalize warns about a missing read and never blocks.

## Consequences

GDRs survive project copies and need no new store. Direct FLEx edits are not tracked in the page's Proposal list. Storing
rich text depends on the shared editor and the converter, which are specified separately.
