# Motif becomes an application, and the CLI stays the door agents use

**In plain terms:** Motif has been a command-line tool with a FieldWorks view planned for later. It now becomes
its own desktop application for reviewing and landing grammar changes, built so that a linguist can keep
FieldWorks open beside it. FieldWorks itself will call Motif exactly once, to land the changes a person has
already asked for. The command line does not go away: it is how Claude Code and every other agent drive
Motif, and everything the application can do, the command line can do too.

This note records the direction settled on 2026-09-03 and decomposes it into the specs that follow. It is
the design for the *direction*; each numbered sub-project below gets its own design and plan.

## What changes, and what does not

| Was | Is now |
| --- | --- |
| Two deliverables: the `motif` CLI, and FieldWorks-owned Avalonia surfaces rendering `motif --json` | Two deliverables: the `motif` CLI, and a Motif-owned Avalonia application. FieldWorks integration is one CLI call |
| "There is one API and it is the CLI" ([ADR 0040](../../adr/0040-one-api-the-cli.md)) | One **command catalog**, with the CLI as its first and complete front end, the application as its second, and an MCP server as its third |
| Refusals are worded and printed at 59 `Fail(...)` sites inside the CLI | Refusals are a typed record in `SIL.Motif.Contract`, returned by the command and rendered by whichever front end asked |
| `SIL.Motif.Contract` keeps `netstandard2.0` for a `net48` FieldWorks | One target framework, `net10.0`, everywhere. A unified FieldWorks is `net10.0`; the non-.NET runners read Contract as a description, not an assembly |
| FieldWorks never loads Motif in-process (ADR 0040 decision 1) | A **separate** FieldWorks talks to Motif through one verb. A **unified** FieldWorks, if it comes, hosts the same views and the same command core in-process |
| `apply` has no queue; a person or agent runs it against a project they hold | A person **queues** a Proposal for apply. If FieldWorks holds the project, the Proposal waits as *pending* until FieldWorks calls `motif apply --all-pending`; if not, it is applied then and there |

What does **not** change, and is restated here so nobody reads the pivot as wider than it is:

- The database is the boundary between Motif's own processes (ADR 0040 decision 4). The application, the
  CLI, and the job runner coordinate through `Project.motif.db` and nothing else. Nothing gains a wire.
- The CLI and job runner ship as one artifact at one version (ADR 0040 decision 5). The application joins them.
- Motif never attaches to a live project as a shared-XML peer (ADR 0040 decision 6).
- A live project is touched only at a save boundary, and only by Baseline capture and Apply (ADR 0040 decision 7,
  [ADR 0039](../../adr/0039-one-worker-baseline-and-live-host-authority.md)).
- Readiness is computed from evidence and never granted by a person; `--force` overrides it. Queueing a
  Proposal is not approving it. It is saying *land this when you can*, and Readiness is still checked when the
  apply actually runs.
- Every non-negotiable design rule in `AGENTS.md` stands. The Runner is still handed a loaded model and never
  opens one; the Host still owns loading, saving, and the lock.

## Decisions

### 1. The stack

`net10.0`, Avalonia 12, [Semi.Avalonia](https://github.com/irihitech/Semi.Avalonia) for theme and controls,
[LiveMarkdown.Avalonia](https://github.com/DearVa/LiveMarkdown.Avalonia) for rendered prose (Reports, AI chat,
Descriptions). Other Avalonia packages as individual views need them. Where a choice is otherwise open, match
what FieldWorks' own Avalonia work chooses, so that a later merge is a move and not a port.

### 2. One command catalog, three front ends

The verb implementations leave `SIL.Motif.Cli` for a command layer. Each command takes typed input and returns
a typed result: either a Contract-shaped record or a Contract-shaped **Refusal**. The command layer knows
nothing about consoles, JSON text, or view models.

- **The CLI** parses arguments, runs the command, and prints the result as text or `--json`. It is the door
  Claude Code and every scripted agent use, and it stays complete: **every command in the catalog has a verb.**
- **The application** binds views to the same result records, in the same process. It never parses `--json`.
- **The MCP server** (sub-project 9) enumerates the catalog and exposes each command as a tool. It is a
  projection, not a second vocabulary.

**Parity is enforced, not promised.** A test enumerates the catalog and fails when a command has no CLI verb,
on the same terms as an unclassified model field fails the coverage manifest. The application may add
interaction the CLI cannot express, such as drag-and-drop or a live chat, but every *effect* such an interaction
has on the store is a catalogued command reachable from the CLI.

### 3. Refusals are typed

The 59 `Fail(...)` sites keep deciding and keep their wording. What changes is the carrier: a `Refusal` record
in Contract, with a stable code, the human sentence, and the facts it was computed from, so the application
can render it as more than a string and the CLI's failure envelope in [the CLI API](../../cli-api.md) is
populated from it rather than parallel to it. This is the largest single piece of the extraction and is
counted as such in sub-project 2's plan.

### 4. One target framework

`netstandard2.0` is retired from every project, Contract included. The `Compatibility/` shims under the Runner
go with it. The `System.Text.Json 8.0.5` pin, which existed only for the `netstandard2.0` line, is lifted to
the framework's.

### 5. The application never holds a live project model

The linguist is assumed to have FieldWorks open. The application reads the Motif store and the Baseline, asks
the job runner for anything that takes time, and observes progress by reading the database. Only Baseline
capture and Apply open the `.fwdata`, briefly, and only when FieldWorks does not hold it. One application
instance works on one project; several instances may run at once, each on its own project or even the same
one, because the database and the lock already arbitrate that.

### 6. The FieldWorks contract is one verb

A separate FieldWorks integrates by running, at a save boundary and with the project released:

```
motif apply --all-pending [--project <path>]
```

It applies every pending Proposal on that project that is Ready at that moment, each as its own atomic unit of
work, and reports per-Proposal what landed and what was refused and why. FieldWorks reloads afterwards, the
FLExBridge pattern. Nothing else crosses: FieldWorks does not list, show, trial, or author.

### 7. Pending is explicit

A Proposal enters *pending* only because a person or agent ran `apply` on it. When the project is free, `apply`
lands it immediately as today. When FieldWorks holds the project, `apply` records the Proposal as pending and
the application says so in as many words: **"FieldWorks has the project. Change pending."** The next
`--all-pending` call, or a later `apply` when the project is free, lands it.

Pending is a lifecycle state, added to the five in [the Proposal lifecycle](../../proposal-lifecycle.md),
because a person needs to see it and take it back. What happens when a pending Proposal is refused at apply
time, how pending Proposals are ordered, and how FieldWorks learns there is anything pending are settled in
sub-project 3, not here.

### 8. The sub-projects, in build order

Each is its own spec and plan. Later ones depend on the seams earlier ones prove.

| # | Sub-project | What it delivers | Depends on |
| --- | --- | --- | --- |
| 1 | **Direction ADR and plan amendment** | An ADR superseding ADR 0040 decisions 1 to 3, the Delivery tables in Plan A and the README, the *Motif API* and *Motif job runner* glossary entries, this note's decisions on the record | nothing |
| 2 | **Command catalog and the shell** | The command layer extracted from the CLI, typed Refusals, the parity test, one target framework; the Avalonia application with project selection, the Proposal list as expandable cards, and the single-Proposal page: intent, status, Assessments, Reports | 1 |
| 3 | **Apply queue** | The *pending* state, `apply` that queues when FieldWorks holds the project, `apply --all-pending`, and what the application shows | 2 |
| 4 | **Before and after** | Dry Run effects as a two-column view: only the entries that change and only the fields that change in them, one FieldWorks link per entry | 2 |
| 5 | **Change in analysis** | The Difference Assessment rendered so a linguist can review what re-parsed differently and act on a mis-analysed word. A compact word-and-analysis form and an expanded FieldWorks-like form, each with a link to the word or its text and line | 2 |
| 6 | **Timing** | Per-word and per-morpheme-or-rule timing from the timing Assessment kinds. A new kind of view; expect several design passes | 2 |
| 7 | **Proposal authoring** | Editing a Proposal as JSON with schema-driven completion, which requires the generator to publish a JSON Schema for the operation vocabulary | 2 |
| 8 | **Advice bundle** | Grammar, optional texts, and instructions as files a person drags into a hosted chat model, plus the instructions for doing so | 2 |
| 9 | **MCP server** | The catalog exposed as tools so Claude Code, Codex, and similar can read local files and author Proposals | 2, 7 |
| 10 | **AI proposal mode** | A chat in the application through which a subscription, locally hosted, or bring-your-own-key model of the Luna class makes and updates Proposals for concrete asks. Harness and retrieval are prototyped in `../linguistic-assistant`; Motif supplies the surface and the Proposal-writing seam | 2, 7, 9 |

Sub-projects 4, 5, 6, 7, and 8 are independent of one another and may be built in any order once 2 is in.

### 9. Project layout

| Project | Role |
| --- | --- |
| `SIL.Motif.Contract` | Shapes, now including command results and `Refusal`. `net10.0` |
| `SIL.Motif.Commands` *(new)* | The catalog: every verb's implementation, typed in and typed out. References Host, Projection, Runner |
| `SIL.Motif.Cli` | Argument parsing and printing over the catalog. Shrinks to that |
| `SIL.Motif.App` *(new)* | The Avalonia application. Binds to Contract records, calls the catalog in-process |
| `SIL.Motif.Mcp` *(new, sub-project 9)* | Tool projection over the catalog |
| everything else | unchanged |

The exact name of the command-layer project is sub-project 2's to settle; *Commands* is the placeholder.

## What this note deliberately leaves open

These are the questions to be grilled and then settled in the sub-project specs, listed so nobody mistakes
silence for a decision.

- **Pending under refusal.** When `--all-pending` finds a pending Proposal that is no longer Ready, does it stay
  pending with the refusal recorded, or fall back to *proposed*?
- **Pending order.** Several pending Proposals may touch the same entries. Do they land in queue order, and does
  one refusal stop the ones behind it?
- **How FieldWorks learns there is work.** Whether FieldWorks calls `--all-pending` on every save and gets a
  cheap "nothing pending", or whether something tells it first.
- **Detecting that FieldWorks has the project.** The `.fwdata.lock` is the obvious signal and ADR 0030 already
  relies on it; whether it is sufficient is to be confirmed.
- **FieldWorks links.** The `silfw://` link scheme for entries, and whether a word occurrence can be linked at
  all given that occurrences have no durable identity (ADR 0025 decision 3).
- **What parity exempts.** The list of application-only interactions and CLI-only verbs, written down and
  tested rather than assumed.
- **Where the Avalonia work lives.** This repository, matching FieldWorks' conventions, versus a sibling.
- **The boundary with `linguistic-assistant`.** What Motif owns of the harness, retrieval, and prompt work in
  sub-project 10, and what it consumes.
