# ADR 0043 — One command catalog, two front ends

**Status:** accepted, 2026-09-04. Supersedes [ADR 0040](0040-one-api-the-cli.md) decisions 1 to 3. ADR 0040
decisions 4 to 7 — the database as the only boundary between Motif's own processes, one shipped artifact at
one version, no shared-XML peering, and reaching a live project only at a save boundary — remain binding.
Realises decisions 2 and 4 of
[the application direction](../superpowers/specs/2026-09-03-motif-application-direction-design.md), and is
the first task of
[its command-catalog plan](../superpowers/plans/2026-09-04-ai-handoff-command-catalog-plan.md).

**In plain terms:** Motif's window and its command line are about to do the same work — proposing, checking,
and applying changes to a grammar — and from here on they do it through the same code, so a result or a
refusal from one can never quietly disagree with the other. Every project in this repository also settles on
one .NET version instead of two, because the second one existed only to let a version of FieldWorks that is
itself going away load a small piece of Motif directly.

## Context

ADR 0040 decided Motif has one API surface, and it is the CLI: an AI agent, a script, and FieldWorks all run
`motif <verb>` and read `--json`, and nothing else is a legitimate way to reach a Motif decision. That was
correct for the shape of Motif that existed in August — a batch CLI plus a FieldWorks add-in with no
plausible reason to be anything else. Two things have changed since.

**Motif is getting a second first-party front end.** The application direction commits to a Motif-owned
Avalonia application — the window a linguist keeps open beside FieldWorks to review and land Proposals — and
its first slice, the AI handoff, is already under construction. That application is not FieldWorks: it is
Motif's own process, built from this repository, shipped alongside the CLI. Making it talk to the CLI's own
argv-and-JSON surface, the way ADR 0040 requires of every consumer, would mean parsing text that application's
own process just rendered a moment earlier — the exact re-encoding cost ADR 0040's reasoning was built to
avoid, moved to a new boundary instead of removed.

**FieldWorks stopped being the reason `SIL.Motif.Contract` needed `netstandard2.0`.** That target existed for
one purpose: letting a `net48` FieldWorks process reference `Contract.dll` directly, to deserialise `motif
--json` and render a diff, without loading the rest of Motif. Two independent things now remove that need.
FieldWorks' own Avalonia work is a move to `net10.0`, so a future FieldWorks that references Contract at all
references a `net10.0` build of it. And the FieldWorks integration this repository now specifies does not
reference Contract for rendering in the first place — decision 6 of the application direction settles it as
exactly one CLI call, `motif apply --all-pending`, run at a save boundary with the project released and read
only by its exit code and its own summary, never deserialised into a Contract type. Nothing outside this
repository still needs Contract to load in a `net48` process.

**The 59 `Fail(...)` sites were always going to have to become typed values, not strings.** ADR 0040 already
named this the single largest piece of work a boundary would create; it does not go away when the boundary
does. It is this ADR's occasion, because a second front end that renders results in its own UI cannot format
a human sentence and then unformat it to decide what to show.

## Decision

This ADR establishes five invariants, stated here exactly because they are what the rest of this repository
is entitled to assume from now on:

1. Command implementations are in-process and UI-agnostic.
2. Every command returns a typed result or typed Refusal.
3. Every catalogued command has a CLI verb; store-free interaction may remain application-only.
4. Every Motif project targets `net10.0`; Contract remains LibLCM-free and is a wire description, not a
   binary compatibility promise to `net48`.
5. The database remains the only coordination boundary between Motif processes.

### 1. Command implementations move out of the CLI into one in-process, UI-agnostic catalog

A new project, `SIL.Motif.Commands`, receives every verb's implementation. It knows nothing about consoles,
JSON text, or view models: a command takes a typed request and returns a typed result. This replaces ADR
0040 decision 1's premise that nothing but the CLI's process boundary may reach a command — the boundary that
mattered was never *which process*, it was *who decides*. That authority stays exactly where ADR 0040 put
it, inside the command; what changes is that more than one front end may now call it directly.

A **separate** FieldWorks — the one that exists today, and the one this repository builds toward next — is
not one of those front ends. It still never opens `Project.motif.db` and never loads any Motif assembly
beyond what one CLI call requires, which after decision 6 of the application direction is nothing: it runs
`motif apply --all-pending` as a subprocess and reads its exit code and JSON summary. A **unified** FieldWorks,
were one ever built, would be a third first-party front end under this same rule, not an exception to it —
that question is left open by the application direction and is not decided here.

### 2. Every command returns a typed result or a typed Refusal, never a rendered string

The 59 `Fail(...)` sites keep deciding and keep their wording — no sentence a person currently reads changes —
but the carrier becomes a `Refusal` record in `SIL.Motif.Contract`: a stable code, the human sentence, and the
facts it was computed from. Success becomes the existing Contract-shaped projection or a new typed response
record. `CommandOutcome<T>` holds exactly one of a value or a Refusal, never both and never neither. This is
what lets the application render a Refusal as more than a paragraph of text, and it is what makes the CLI's
`--json` failure envelope populated from the same data the human message came from, rather than a second
hand-maintained copy of it.

### 3. Every catalogued command still has a CLI verb; only store-free interaction may be application-only

ADR 0040 decision 2 said the CLI is the one API surface. It is no longer the only one, but it stays the
**complete** one: a `CommandCatalog` enumerates every command, a `CliVerbCatalog` enumerates every CLI verb,
and a test fails when the two disagree. The rule this enforces is about effect, not about interface count —
**every effect a command has on the store is reachable from the CLI**, because the CLI is what an AI agent, a
script, and a test can drive, and a capability only the application can reach is a capability Claude Code
cannot use. An interaction with no effect on the store — a chat's turn-by-turn scrolling, a grid's client-side
sort — may stay application-only, because there is nothing there for a second front end to reproduce.

### 4. Every Motif project targets `net10.0`; Contract is a wire description, not a `net48` binary promise

`netstandard2.0` is retired from every project in this repository, `SIL.Motif.Contract` included. The
`Compatibility/` shims that existed to backfill gaps on the older target go with it, and so does the explicit
`System.Text.Json 8.0.5` pin, which existed only because it was the newest release still compatible with
`netstandard2.0` — `net10.0` supplies its own.

Contract keeps the rule ADR 0040 decision 3 stated, just without the target framework that used to serve it:
**it has no LibLCM reference**, so nothing that reads it needs LibLCM to make sense of it, and it remains the
normative, RFC 8785-canonicalised description of Motif's field and response shapes that the Python and Rust
runners ADR 0040 named read as a specification, not a loaded assembly. What changes is only that "crosses the
process boundary" no longer implies "must be loadable by a `net48` host," because no consumer left needs
that: a separate FieldWorks calls one CLI verb and never references Contract at all, a unified FieldWorks is
`net10.0`, and a non-.NET runner was never loading the .NET assembly to begin with.

### 5. The database remains the only coordination boundary between Motif's own processes

Unchanged from ADR 0040 decisions 4 and 5, restated because decisions 1 to 3 are superseded and this one is
not: the CLI, the application, and the job runner coordinate through `Project.motif.db` and nothing else. Two
front ends calling the same in-process catalog does not create a second channel between processes — a command
still reaches storage exactly once, through whichever process's catalog instance is executing it, and still
commits through the schema-generation guard `MotifDatabase` already enforces. Adding a caller inside one
process is not the same change as adding a channel between processes, and this ADR makes only the first kind.

## Consequences

- `SIL.Motif.Cli` shrinks to argument parsing, rendering, and dispatch; it holds no store, no LibLCM
  reference, and no domain decision after the plan this ADR opens finishes.
- The application can be built against the same tested command layer the CLI already exercises, rather than a
  second implementation or a JSON-parsing shim — the numbered plan this ADR precedes is exactly that
  extraction.
- A refusal rendered in the application and a refusal rendered by the CLI are now guaranteed to agree, because
  they are the same `Refusal` value read twice, not two independent renderings of the same intent.
- `net10.0` becomes the one target to reason about anywhere in this repository. The `System.Text.Json` versus
  Newtonsoft binding-redirect question ADR 0040 left open for a `net48` FieldWorks is retired along with the
  host it was a question about, rather than answered.
- The parity test becomes a standing gate: a command added to the catalog without a CLI verb fails the build,
  the same discipline the coverage manifest already applies to an unclassified LibLCM field.
- What this ADR does not settle: whether a unified FieldWorks becomes a third front end sharing this catalog,
  what `SIL.Motif.Commands`'s own internal shape looks like beyond "typed in, typed out," and the MCP server
  the application direction names as a third projection over the same catalog. Those stay open for the specs
  that own them.

## Rejected alternatives

- **Give the application its own copy of the command logic.** Rejected: it is the exact defect ADR 0040 was
  written to prevent — two implementations of one decision, which drift the day one of them is patched and the
  other is not.
- **Have the application shell out to `motif --json` like any other CLI consumer.** Rejected: the application
  is Motif's own process, so this would mean serialising a result to text and parsing it back inside the
  process that just computed it, for no isolation benefit — the re-encoding cost ADR 0040 fought to avoid,
  reintroduced at a boundary that buys nothing by existing.
- **Keep `netstandard2.0` on Contract as a hedge, in case a `net48` FieldWorks integration outlives the
  Avalonia migration.** Rejected: the FieldWorks integration this repository now specifies does not reference
  Contract at all, so there is no consumer left to hedge for, only a maintenance cost with nothing on the
  other side of it.
