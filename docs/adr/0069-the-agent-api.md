# ADR 0069 — The agent API: found by goal, defined once, judged by tasks

An AI assistant building a grammar for a new language should find the command it needs by saying what it wants,
get the same command and the same answer from the window's command line and from its AI tools, and be told what
to do next whenever Motif refuses. This decision sets how Motif's agent-facing commands are discovered, named,
shaped, versioned and measured, so that the next two thousand languages mostly reuse what the first few built.

**Status:** proposed, being decided with the owner. Builds on [ADR 0009](0009-layered-api-primitives-and-composers.md),
[ADR 0029](0029-agents-address-layer-1-only.md), [ADR 0043](0043-one-command-catalog-two-front-ends.md),
[ADR 0047](0047-generated-help-walkthroughs-and-docs-site.md), [ADR 0058](0058-the-agent-edit-loop.md) and
[ADR 0059](0059-measuring-agents-fairly.md), and on the one-composer-definition design (a composer is declared once
and every front end is built from that declaration).

## Context

The first real grammars built through Motif (Swahili and Tamil, about 200 applied Proposals) logged about 95
friction rows. Most came from the command line, not the AI tools. The largest group was refusals and empty results
that did not say what was missing; about a third of the requests for "missing" commands named something that
already existed but could not be found; and about half of the genuinely new commands needed linguistic policy that
a generic field setter would have let an agent get wrong. Each new composer cost hours of lead time and a release.

## Decision

_Sections are filled as the owner decides; see the decision log._

### 1. One API, two renderings

The agent API is the catalog's Released and AdvancedAi commands, nothing else. A command's CLI verb and its MCP tool
are two renderings of one definition, so:

- no capability exists on one rendering only (registering a project, a Proposal argument to try a word, and a
  refusal's wording have each existed on one side and not the other);
- a response has one shape on both: a refusal carries the same code, reason, message, detail and next step whether
  it reaches standard error as the FailureEnvelope or an agent as an MCP error result;
- every skill and guide that names a command gives both routes, because most real authoring so far ran the CLI.

### 2. The unit an agent addresses: a Composer per Phenomenon

An agent authors through Composers (see CONTEXT.md). A Composer is named for the intent a linguist would recognise
and declares the Phenomena it serves; the Phenomenon is the key an agent searches and a skill groups by. No Composer
is written for one language: a second language that needs the same pattern reuses it, and a pattern that differs
gets its own intent, not a language flag.

### 3. No primitive route (ADR 0029 stands)

Agents do not stage Layer 0 primitives, even into a Draft behind Dry Run and Trial. About half of the new surface the
first grammars needed was linguistic policy (a zero realization restricted to a class, a stem name attached to an
allomorph) that a generic setter would let an agent author wrongly, and the most harmful failures were silent
linguistic side effects that a setter makes easier, not harder. The long tail is unblocked by making a Composer cheap
to add: one definition, its lowering, and its Help. This is revisited only with evidence: if, after one-definition
composers land, a Phenomenon with no Composer holds up a language for more than a day, the task evaluations in
section 9 record it and a later ADR weighs a gated route against that record.

### 4. Discovery: stage tools and a bank of Composers

Both major agent hosts now hide MCP tools behind a search step by default, and at least one host's search misses
tools by their exact name or is never called. So Motif does not depend on the host finding a Composer.

- **Stage tools.** The default profile offers about fifteen tools, one per stage or verb of the edit loop, marked to
  load always, and the server instructions open with a map of them short enough for every host to read.
- **The Composer bank.** Two of the stage tools reach every Composer. `motif_find_composer` takes a goal in a
  linguist's words or a Phenomenon and returns the matching Composers, each with when to reach for it, its schema and
  a worked example, together with the Phenomenon's help page: what that issue needs, in which order, and the
  pitfalls. `motif_compose` takes one or more steps, each a Composer's verb and its intent, and is the one way to
  author; a single step and a batch are the same call.
- **Found by goal.** A Composer's definition declares its Phenomena from a closed list kept in the catalog, and its
  Help gives a few example goals in a linguist's words, which translators can translate like the rest of Help. The
  search reads those goals with the Title and Description. A Composer without a Phenomenon or an example goal fails
  the build.
- **The expanded profile.** One tool per Composer is still generated from each definition, in an expanded profile,
  so the two shapes can be compared by the task evaluations rather than argued.

### 5. Naming and shape

- Tool names are `motif_<verb>_<noun>`; arguments are camelCase.
- One idea has one name on every tool: `proposal` for a Draft or a Proposal, `project` for the project.
- An id is passed back exactly as Motif returned it, prefix included.
- Every tool takes `detail` (`concise` by default, or `detailed`); a response that would exceed about four thousand
  tokens pages instead of inlining.

### 6. Refusals and next steps

A refusal is the API teaching its caller. Every refusal:

- carries a code from one closed registry, which maps the code to its reason, its hint and its next step for the CLI,
  the MCP server and Help alike;
- names the precondition that is missing (no Baseline, no worker owns the job, the id is an Assessment id without its
  prefix) and the call that satisfies it, written for the rendering the caller used.

No read returns plausible zeros or an empty list when it could not look; it says it could not look and why, as a
Parsimony note already does. A success whose next step is obvious says it in `next`.

### 7. Skills are generated from the catalog

What a skill says about commands is generated: each command's route on both renderings side by side, the
Phenomenon index of Composers, and the crosswalk between CLI verbs and MCP tools. What is written by hand is written
once: the slice playbook (pick a Phenomenon and cite the source; compose; Dry Run; Trial; compare the Difference and
human decisions; Parsimony; Finalize; record the evidence), including how ids returned by one step feed the next.
The build fails when a skill names a tool, a CLI verb or a Help link that does not resolve.

### 8. Versioning and stability

Before 1.0 a rename is a rename, as for stored shapes (AGENTS.md rule 18): no tool aliases and no deprecated argument
names. Each release's notes carry the generated difference of the MCP tool listings, so an agent author sees what
moved. A Composer can be retired or merged freely, because a Proposal stores Motif operations and names the Composer
only as provenance; nothing recorded depends on the Composer still existing. The stability promise after 1.0 (a
surface version, deprecation windows) is a later decision.

### 9. Evaluation by task

Whether the API serves agents is measured, not argued. Each Phenomenon gets a small task in the public test grammars,
across typologically different languages, run by the existing harness (ADR 0059). Besides its current measures
(calls by tool, tool errors, turns, tokens, wall time), each run records refusals by code and whether the agent found
the expected Composer at all, and keeps these per run so later questions ("which refusal costs agents most turns?")
can be asked of stored runs. Grading stays on the resulting grammar, never on the sequence of calls.

Real sessions are recorded the same way, on the person's machine only: the activity log records each call's stage,
Composer and refusal code and whether it followed the previous response's next step, and `motif activity` answers
the same questions of it. Nothing is sent anywhere.


## Consequences

## Rejected
