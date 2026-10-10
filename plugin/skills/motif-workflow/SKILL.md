---
name: motif-workflow
description: Use Motif to inspect a Known project, draft and measure a Proposal, revise it, and Finalize it for a person to Apply. Use for any change to a FieldWorks project; never edit the project another way.
---

# Motif workflow

Use Motif for every project change. A Proposal belongs to one project. The person owns project
persistence and decides whether to Apply after reviewing a Finalized Proposal in Motif.

If the Motif tools are missing, tell the person to open Motif, turn on Advanced AI mode, and select
this assistant. Do not substitute direct FieldWorks, SQL, XML, or file edits.

## Find the project

1. Call `motif_list_projects` and show the Known project names and paths.
2. Ask which project to use when the intended project is unclear. Never guess from a workspace folder,
   a recent conversation, or a project name that is not listed.
3. Pass the chosen project's `project` value to every project tool call. There is no current project.

## Read before drafting

1. Call `motif_overview` to understand the project's grammar and saved state.
2. Read only the relevant objects with `motif_grammar`, `motif_lexicon`, or `motif_word`. Use the
   exact ids returned by Motif; do not infer identity from similar names, forms, or glosses.
3. Call `motif_proposals` if an existing Proposal may already cover the requested change. Do not add
   the same slot to a second operation in one Proposal.
4. State the intended linguistic change and the evidence it should satisfy before creating a Draft.

## Draft and measure

1. Call `motif_start_proposal` with a short draft name, a reviewer-facing label, and the reason for the
   change.
2. Use the semantic composer tool whose description matches the intended operation. For example,
   `motif_set_gloss` changes a gloss; it does not authorize a raw property write. Keep each operation
   grounded in ids and current values returned by Motif.
3. Call `motif_dry_run` on the Draft. Read the effects and any refusal before continuing.
4. Call `motif_trial` with representative positive words and, when available, attested negative words.
   Read its summary, including unfinished searches, without treating an incomplete search as rejection.
5. Use the Trial's Difference id with `motif_difference` to page through the relevant categories. Read
   every lost positive and newly parsing negative before deciding whether to revise.

## Revise and Finalize

1. If the evidence is not acceptable, revise the Draft with the appropriate semantic composer. To
   replace an operation, read its id and remove it with `motif_remove_operations` before adding the
   replacement. Then repeat Dry Run, Trial, and Difference review.
2. Call `motif_finalize_proposal` only when the Draft is ready for a person to review. Tell the person
   the Proposal label and summarize its Dry Run and Trial evidence.
3. Never Apply. The person reviews and Applies the Proposal in Motif if they choose.

## Several projects

The same requested change in two projects is two Proposals. For each Known project, make a separate
Draft, read that project's objects, run its own Dry Run and Trial, review its Difference, and Finalize
it independently. Name the related project in each Proposal's comment so the person can review them
together without combining them.
