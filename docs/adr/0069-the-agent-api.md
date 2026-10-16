# ADR 0069 — The agent API: seven tools, a bank of Composers, and a Trial that tells everything

An AI assistant building a grammar should need only a handful of commands: look at the project, find the right way
to say what it wants, write it into a Proposal, run a Trial, and read everything the Trial found. Motif now offers
agents seven tools instead of thirty-eight, gives the same commands and answers on the command line, finds the
right Composer from a goal in a linguist's words, and always says what to do next when it refuses. The aim is that
the next two thousand languages mostly reuse what the first few built.

**Status:** proposed, decided with the owner on 2026-10-15. Amends [ADR 0058](0058-the-agent-edit-loop.md)'s list of
which commands become agent tools and its separate Dry Run and Trial tools; the meaning of a Dry Run and a Trial is
unchanged. [ADR 0029](0029-agents-address-layer-1-only.md) stands, with no gated primitive route. Builds on
[ADR 0009](0009-layered-api-primitives-and-composers.md), [ADR 0043](0043-one-command-catalog-two-front-ends.md),
[ADR 0047](0047-generated-help-walkthroughs-and-docs-site.md), [ADR 0059](0059-measuring-agents-fairly.md),
[ADR 0064](0064-every-operation-can-cite-its-source.md) and [ADR 0067](0067-proposal-difference-against-human-decisions.md),
and on the one-composer-definition design, in which a Composer is declared once and every front end is built from
that declaration.

## Context

The first real grammars built through Motif (Swahili and Tamil, about two hundred applied Proposals) logged about
ninety-five friction rows. Almost all came from the command line rather than the AI tools. The largest group was
refusals and empty results that did not say what was missing, such as a job reported as queued when no worker
owned it, or zero findings because no Baseline existed. About a third of the requests for missing commands named
something that already existed but could not be found, and agents read C# source to learn what a Composer
accepted. About half of the genuinely new commands needed linguistic policy that a generic field setter would have
let an agent get wrong. Each new Composer cost hours of lead time and a release.

Meanwhile both major agent hosts began hiding MCP tools behind a search step by default. At least one host's
search misses tools by exact name or is never called, and both vendors advise keeping only a few tools always
loaded (under ten per group, three to five always present).

## Decision

### 1. One API, two renderings

The agent API is the catalog's Released and AdvancedAi commands, nothing else. A command's CLI verb and its MCP tool
are two renderings of one definition, so:

- no capability exists on one rendering only (registering a project, a word result for a Proposal, a Difference
  category and a refusal's wording have each existed on one side and not the other);
- a response has one shape on both: a refusal carries the same code, reason, message, detail and next step whether
  it reaches standard error as the FailureEnvelope or an agent as an MCP error result;
- every skill and guide that names a command gives both routes.

### 2. Seven tools

The default profile is seven tools, marked to load always, and the server instructions open with a map of them
short enough for every host to read in full.

| Tool | Reads or writes | What it does |
|---|---|---|
| `motif_projects` | reads | The Known projects |
| `motif_read` | reads | One `view` of a project: `overview` (including anything missing before work can start: no Baseline, no worker, Advanced AI mode off), `grammar`, `lexicon`, `word`, `texts`, `proposals` |
| `motif_find_composer` | reads | The Composers for a goal in a linguist's words or a Subject, each with when to reach for it, its schema and a worked example, and that Subject's help page |
| `motif_try_word` | reads | One word against the project as it stands |
| `motif_results` | reads | Everything a Proposal's Trial collected, by `aspect`, paged |
| `motif_proposal` | writes | A Proposal's simple state changes, by `action`: `start`, `rationale`, `trial`, `finalize`, `withdraw` |
| `motif_compose` | writes | Authoring: one or more steps, each a Composer's verb and its intent, and operations to remove |

A tool that reads never writes, so a host that asks a person before every write keeps working. Each `action` and
`view` takes only its own arguments, and the server refuses an argument that does not belong to it. A single
Composer step and a batch are the same `motif_compose` call.

The full profile offers one tool per Composer and the older separate tools. It exists so the task evaluations
(section 9) can show the seven do at least as well; no real session needs it. No other profile is kept.

### 3. A Trial tells everything

A Trial is everything a person or an agent could want to know about a Proposal, collected once: the Dry Run, the
grammar check's warnings with Parsimony among them, the parse of every measured word with its timing and its
standing against human decisions, what changed against the Baseline, the comparison with human decisions
(ADR 0067), and the parser's explanation for every word whose result changed and every word that still fails.
Running a Trial again makes a new Trial; Apply reads the newest, as now.

After a Trial, an agent reads what was collected and computes nothing live against the Proposal. `motif_results`
offers the aspects `summary`, `warnings`, `changes`, `decisions`, `parsimony`, `timing`, `word` and `object` (all
that was collected about one rule, slot, allomorph, environment, feature or warning). A word the Trial did not
measure is reported as not measured in that Trial, with how to include it. Re-parsing a word on a Proposal's Trial
copy leaves both front ends; tracing and inspecting the project as it stands remain.

### 4. The Composer bank, found by Subject and goal

Composers are reached only through `motif_find_composer` and `motif_compose`, so finding one never depends on a
host's own tool search.

- A Composer is named for the intent a linguist would recognise, and its one name is its CLI verb, which is also
  the `verb` of a compose step.
- Each Composer declares the Subjects it serves from a closed list kept in the catalog: the grammar Subjects are
  Phenomena (sound inventory, natural classes, phonological rules and environments, conditioned allomorphy, affix
  slots and templates, inflection features and agreement, noun class and gender, inflection classes, stem
  alternation, infixation, reduplication), and the others are lexicography and description (lexical entries and
  glosses, senses and definitions, example sentences, grammar explanation, texts and analyses, Parsimony
  dispositions). A new Subject enters the list with its first Composer.
- Each Composer's Help gives a few example goals in a linguist's words, translatable like the rest of Help.
  `motif_find_composer` searches those goals with the Title and Description. A Composer without a Subject or an
  example goal fails the build.
- Each Subject has a help page: what that issue needs, in which order, and its pitfalls.
- No Composer is written for one language.

### 5. No primitive route; a new Phenomenon's Composer arrives in days

Agents cannot use low-level operations: they never stage Layer 0 primitives, even into a Draft behind a Trial, and
ADR 0029 stands without a revisit clause (the owner closed the gated-route option). About half of the new surface
the first grammars needed was linguistic policy that a generic setter would let an agent author wrongly, and the
most harmful failures were silent linguistic side effects that a setter makes easier. So a new language's long tail
is unblocked by adding a Composer quickly, and the Composer definition makes that days of work, not weeks.

**When the Composer lowers to existing operation kinds** (the usual case, about a day), the person adding it writes
four things:

1. **The domain work, in the Runner:** an intent record, its closed parser (unknown properties are refused, ids are
   compared by identity as in ADR 0068) and the build step that lowers the intent to existing Motif operations.
2. **One Composer definition, in Commands, about ten lines:** the verb (also the compose step's `verb`), the provenance
   name, the closed shape of its fields, its exposure, its Subjects, and when needed which earlier edit it replaces,
   what a batch label names, and its related objects.
3. **Its Help:** a Title, a Description, one line per field, two to four example goals in a linguist's words naming no
   language, and a Help page. A new Subject also gets its entry and its help page.
4. **Its tests:** positive cases, refusals, and Dry Run and Apply read-back on a blank project, plus a real-parser test
   when parsing behaviour is the point.

Everything else is generated from the definition and never edited by hand: the CLI verb, its usage and dispatch, the
catalog entry, membership and schema in `motif_compose`, `motif_find_composer`'s results, the per-Composer tool in the
full profile, and the descriptions both renderings show. The test suite fails when Help, a Subject or an example goal is
missing.

**When it needs a new operation kind**, the operation family's definition of done applies in full (closed schema,
prose semantics, validation and lowering, preview effects, Apply and read-back, conflict and rebase behaviour,
snapshot and diff, positive, negative, rollback, round-trip and conformance fixtures, and a coverage-manifest row).
That adds days per family and is where a Phenomenon really costs time. Either way, a Composer must name an intent a
linguist would recognise, never a field or a mechanism (ADR 0029's review criterion), and is never written for one
language.

### 6. Naming and shape

- Tool names are `motif_<verb>` or `motif_<verb>_<noun>`; arguments are camelCase.
- One idea has one name on every tool: `proposal` for a Draft or a Proposal, `project` for the project.
- An id is passed back exactly as Motif returned it, prefix included.
- Every tool takes `detail` (`concise` by default, or `detailed`); a response that would exceed about four thousand
  tokens pages instead of inlining.
- A Proposal has a one-line label and a **Rationale**: Markdown saying what it changes and why, shown rendered to
  people and agents and required before Finalize. An agent writes it when it starts the Proposal and revises it with
  `motif_proposal` as it composes. Citations stay on the operations they support (ADR 0064).
- A person answers in the Proposal's note, which only a person writes. An agent reads it with the Proposal and never
  writes it. Each of the two records the Proposal content it was written for, so a reader can tell when it is out of
  date. An MCP server cannot start an agent's turn on any host Motif supports, so every response about a Proposal
  says whether the person has written the note since the Rationale was last set, and its next step says to read it.

### 7. Refusals and next steps

A refusal is the API teaching its caller. Every refusal carries a code from one closed registry, which maps the code
to its reason, its hint and its next step for the CLI, the MCP server and Help alike, and names the precondition
that is missing and the call that satisfies it, in the rendering the caller used. No read returns plausible zeros
or an empty list when it could not look; it says it could not look and why. A success whose next step is obvious
says it in `next`.

### 8. Skills are generated from the catalog

What a skill says about commands is generated: each command's route on both renderings side by side, the Subject
index of Composers, and the crosswalk between CLI verbs and MCP tools. What is written by hand is written once: the
slice playbook (pick a Subject and cite the source; start a Proposal with its Rationale; read the person's note; compose; run a Trial;
read the results; Finalize; record the evidence), including how ids returned by one step feed the next. The build
fails when a skill names a tool, a CLI verb or a Help link that does not resolve.

### 9. Versioning and evaluation

Before 1.0 a rename is a rename, as for stored shapes (AGENTS.md rule 18): no tool aliases and no deprecated argument
names. Each release's notes carry the generated difference of the MCP tool listings. A Composer can be retired or
merged freely, because a Proposal stores Motif operations and names the Composer only as provenance. The stability
promise after 1.0 is a later decision.

Whether the API serves agents is measured, not argued. Each Subject gets a small task in the public test grammars,
across typologically different languages, run by the existing harness (ADR 0059). Besides its current measures
(calls by tool, tool errors, turns, tokens, wall time), each run records refusals by code and whether the agent
found the expected Composer. Grading stays on the resulting grammar, never on the sequence of calls. Real sessions
are recorded the same way on the person's machine only: the activity log records each call's tool, Composer and
refusal code and whether it followed the previous response's next step. "Followed" means the call used the tool the
previous `next` named; arguments are not compared. Activity-log lines written before a field existed are still read,
with that field blank. `motif activity` reads the activity log a session names and answers the same questions of it;
a default log for every session is a later step. Nothing is sent anywhere.

## Consequences

- Composer authoring, the default profile, the skills and the Trial each change in separate slices; until the
  Trial collects Parsimony and explanations, `motif_results` reports those aspects as not collected rather than
  computing them live.
- The Difference that only the MCP server could produce moves into the shared commands.
- The comment on a Proposal is renamed Rationale; stored Proposals carry the new name with no migration (rule 18).
- Agents will soon edit prose as well as rules: senses and definitions from corpus evidence, and grammar explanation
  beside the rules it explains. Those arrive as Composers under the lexicography and description Subjects.

## Rejected

- **A gated primitive route** for the long tail, staging Layer 0 primitives into a Draft behind a Trial: closed by
  the owner (agents cannot use low level); see section 5.
- **One tool per Composer on the default profile**, relying on the host's tool search: the hosts' search is lossy,
  and the count grows with every Composer.
- **A try-a-word against a Proposal**: it computes evidence outside the Trial, so two readers of the same Proposal
  could see different answers.
- **Separate Dry Run, Trial, warnings, Difference and Parsimony tools**: they are one question, how the Proposal will
  affect the project.
