# ADR 0046 — Pages, not stages

People can check results, inspect words and review pending analysis changes without moving through formal stages. This decision describes those tasks as pages and keeps each action explicit.

**Status:** accepted, 2026-09-24. Adds **Overview**, **Default Selection**, **Text Coverage** and **Review
changes** to `CONTEXT.md` and widens **Drift** and **Preflight** to single changes. Leaves the 2026-08-31
amendment to `docs/proposal-lifecycle.md` standing. Reruns stay the reader's decision, as
[ADR 0035](0035-reports-are-advisory-queries-over-stored-assessments.md) says. Storage follows
[ADR 0041](0041-the-database-is-the-only-store.md). The implementation plan is
`docs/superpowers/plans/2026-09-24-app-shell-redesign-plan.md`.

**In plain terms:** Motif's window stops being a five-step wizard and becomes a project workspace. Opening
a project shows its stored numbers at once, and seven pages sit in a sidebar: Overview, Texts, Try a Word,
Timing, Warnings, Review changes and AI Handoff. A linguist changes analyses on any page, and one page,
Review changes, gathers what those changes would do before anything is written. **The window and the CLI
now use different words for the same things on purpose**: the window speaks to a linguist, the CLI to an
AI agent or a script. Nothing is rerun unless the person presses Refresh, and a change the project no longer
fits cannot be applied.

## Context

The stage stepper (Project, Grammar, Texts, Results, Handoff) made every visit replay the same sequence,
even when the person only wanted today's numbers or one word's parse. Most of what the pages need is
already built — the Compare matrix, What changed, Try a Word, Statistics, grammar warnings, the Handoff, and
an in-memory list of changes. The redesign mostly moves them. Designing the new shell raised five
questions, and the owner answered them.

## Decisions

### 1. Reruns are always started by the person

Nothing reruns when a project opens or when FieldWorks saves. The top bar says the stored numbers are
stale, and Refresh is a button. This restates ADR 0035; nothing supersedes it.

### 2. Review changes is a page, not a step in the lifecycle

**Review changes** is where a person reads, together, what applying their changes would write (the Dry
Run), what it would do to the numbers (an Assessment of the words it touches), and whether each change still
fits the project (Preflight, run per change). It is a view over those three, not a record of its own.

Reading it is something a person does. It is **not recorded as a Decision, it grants nothing, and it gates
nothing**. The plan first proposed binding a person's review to the revision as a Decision; the owner
reversed that, because the CLI's users — an AI agent, Claude Code, a script — have no equivalent act, and a
gate only one front end could pass would split the lifecycle in two. The 2026-08-31 amendment stands:
Readiness, computed from evidence, is what Apply requires.

### 3. The window and the CLI name things for their readers

The window's reader is a linguist, not a programmer; the CLI's reader is an agent. They name the same
machinery differently, and the glossary maps one to the other:

| In the window | In the CLI and in code | Notes |
| --- | --- | --- |
| Review changes (the page) | `preflight`, `show`, `trial` | No `review` command. The page composes their output |
| Apply to FieldWorks project | `apply` | Never "Save" on screen |
| N changes not applied yet | a Draft Proposal | Changes are kept in `Project.motif.db` and survive closing the window, but the words Proposal, Draft and Preflight never appear on screen |
| No longer fits | a drifted change; a failed per-change Preflight | |
| AI Handoff | Handoff | |

Where the two front ends share a command (ADR 0043), they share its numbers; only the words differ.

### 4. A change that no longer fits blocks Apply

Every collected change carries a fingerprint of what it was made against: the wordform and analysis
identities it touches and the Baseline token. If FieldWorks has saved since and any of that is gone or
different — a word we analysed was deleted — the change has **drifted**. Apply is refused while any change
has drifted, until it is removed or the project is refreshed and the change checked again. This is Drift
and Preflight applied per change, not a new concept.

**`--force` does not reach it.** `--force` exists for evidence that is incomplete — a parse that did not
finish, an Assessment that is missing or measured another state — the way a person may merge while CI has
not finished. A change that no longer fits is the other kind of problem, the way a merge conflict is: the
change cannot be applied as written, and forcing it would write something nobody chose. It must be brought
up to date against the project, never forced.

### 5. The default Selection and the stored summaries live in the Motif store

The first time a project opens, a setup dialog asks what to measure every time. That choice is the
project's **Default Selection**, stored in `Project.motif.db` beside the `.fwdata` with the per-run
summaries the Overview reads (ADR 0041). Each run still resolves it to an exact word list and keeps that
list. Baseline bundles, scratch copies and PanGloss workspaces stay files; `.motif.toml` stays
configuration only.

### 6. Text Coverage is named, and Corpus keeps its name

**Text Coverage** is the share of the Default Selection's words, and of their occurrences in the chosen
Texts, that parse. It is measured over the project's Texts. The same share over a Corpus is grammar
coverage, which already has its name. "Corpus" keeps its meaning — outside running text Motif holds,
never part of the project (ADR 0036) — and is not renamed.

## Consequences

- The plan's first ruling that bound a review to the revision as a Decision is withdrawn; its correction is
  dated in the plan itself.
- The App's pending changes become a Draft Proposal in the store as soon as the analysis operations exist,
  so leaving the window loses nothing.
- `preflight` becomes a CLI command that returns each change's fit.
- A later redesign that wants a recorded human sign-off would need its own ADR, and would have to say what
  the CLI's version of it is.
