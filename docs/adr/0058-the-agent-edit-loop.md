# ADR 0058 — The agent edit loop

An AI assistant can now check its own work before handing it over: it drafts a change, tries it against
the parser, reads exactly which words broke, and only then finalizes it for a person to review and apply.
It can work across several of the person's projects in one conversation, and it sees only the tools and
projects a person has allowed.

**Status:** accepted, 2026-10-09. Amends the 2026-10-03 agent MCP and A/B design.

## Context

The advanced-AI review of 2026-10-09 (Standards and Spec axes) found that the shipped MCP surface did not
let an agent close its own loop:

- `motif_trial` returned only an Assessment id, assessor, kind and time, so an agent could not see which
  words a change broke without guessing at another tool.
- `job dry-run` reads only finalized Proposals, so an agent had to finalize, and so hand over, a change
  before it could check it; the MCP workflow guide said the opposite and sent agents into refusals.
- The rule for what becomes an MCP tool was a hand-maintained map. It exposed Developer-surface commands
  (`start-proposal`, `set-gloss`, `author-lexeme-form`, `finalize`, `remove-operations`) and the tool
  descriptions were handwritten, so they drifted from Help and called a Trial a "test".
- `motif mcp` required `--project` at launch, which a plugin installed once cannot supply, and which
  stops a person asking for the same change in two related projects.

## Decision

### Dry Run and Trial on a Draft; evidence is bound to content

A Draft can be Dry Run and Trialled as well as a finalized revision. Evidence names the content digest it
ran against and the project state (the Baseline) it ran on. Evidence counts only for that same content on
that same project state.

- **Finalize carries evidence over.** Finalizing a Draft without changing it commits the same content, so
  its Dry Run and Trial evidence stays current.
- **Applying another Proposal Drifts the rest.** Applying one Proposal changes the project state, so every
  other Proposal's evidence measured against the earlier state becomes stale and must be measured again.
  "Merged" in this sense is always an Apply; there is no other merge.

### Which commands become MCP tools

A catalog command becomes an MCP tool exactly when its surface is `AdvancedAi` or `Released` **and** its
agent class is not `HumanOnly`. Nothing else is mapped by hand.

- The four drafting commands (`start-proposal`, `set-gloss`, `author-lexeme-form`, `finalize`) and
  `remove-operations` move from `Developer` to `AdvancedAi`. `remove-operations` is on by default.
- Apply, discard, store deletion and every other `HumanOnly` command never exist as tools, as before.
- "Finalize" is the word everywhere: the tool is `motif_finalize_proposal`, and "finish", "submit" and
  "commit" leave tool names, descriptions and guides.

### Descriptions come from the catalog

A tool's description is generated from the command's catalog entry, its Help Title and Description, and
its request record's field documentation. No description is written by hand, so Help, the CLI and the
agent read the same words, and the glossary's terms (Trial, never "test") bind all three. A test fails
when a description uses a term the glossary lists under *Avoid*.

The profile's verbosity default reaches every handler. Tool grouping is dropped from the design.

### What an agent reads back

- **Trial summary inline.** `motif_trial` returns a short summary in this order: words that should not
  parse and now do; words that parsed and no longer do; cases the parser did not finish; other changed
  analyses; counts. It never claims a parser-incomplete case was rejected.
- **Difference, paged.** A read tool pages through the Trial's full Difference by category.
- **`motif_assess`.** An agent can run an Assessment of the project as it stands, without a Proposal.

### Several projects, no open state

The server starts without a project. Every tool that reads or changes a project takes a `project`
argument naming a Known project; there is no "current project". `motif_list_projects` lists the Known
projects. Only a person adds a Known project (by opening it in Motif, or through the CLI); nothing an
agent can call adds one. `--project` is removed.

A Proposal belongs to exactly one project. The same change in two projects is two Proposals, each Dry
Run, Trialled, Finalized and Applied on its own; each one's description names the other.

### Advanced AI mode gates everything

While Advanced AI mode is off, `motif mcp` starts and offers no tools; a call explains how to turn the mode
on. Turning the mode on in the window is also what connects AI assistants (see the packaging section of
the spec amendment).

## Consequences

- Dry Run evidence keys gain the content digest and Baseline; the Parsimony evidence hashes are made
  consistent with them by giving each measure a versioned evidence preimage.
- The catalog classification becomes the single source of the MCP surface; `ToolSurfaceTests` pins the
  rule rather than a list.
- `motif-workflow` and the MCP workflow resource describe one path: draft, Dry Run, Trial, revise,
  Finalize. The guide is generated or tested against the tools so the two cannot disagree again.
- Rule 18 applies: stored evidence without the new keys is refused, not migrated.

## Rejected

- **Requiring Finalize before Dry Run.** It hands a person a change the agent has not checked.
- **An open-project session state.** It forbids cross-project requests and leaves an agent unsure which
  project a call reached.
- **Returning the full Difference inline.** It floods a small-context model; the summary plus a paged
  read keeps the first answer short and the rest reachable.
