# CLI and command API

This page is a current orientation to Motif's command surface, not a second command specification. Older CLI-only, `net48`/`netstandard2.0`, and `--store` descriptions are superseded; the [current architecture overview](current-architecture.md) describes today's front ends, process coordination and targets.

## Shared command source

The command catalog in `SIL.Motif.Commands` lists typed requests, responses and handlers. The CLI catalog maps each command to its invocation names and usage lines. The Avalonia application calls the shared command handlers in-process rather than invoking the CLI or interpreting CLI JSON. See [`CommandCatalog`](../src/SIL.Motif.Commands/Catalog/CommandCatalog.cs) and [`CliVerbCatalog`](../src/SIL.Motif.Cli/CliVerbCatalog.cs) for those source inventories.

Use the built-in Help output for the current command reference:

```text
motif help
motif help <command> --full
motif help --all --json
```

The shared user-facing Guides, including agent Guides, are authored under [`src/SIL.Motif.Help/Content/en/guide/`](../src/SIL.Motif.Help/Content/en/guide/). For current agent guidance, see [Start here](../src/SIL.Motif.Help/Content/en/guide/agents/start-here.md), [Output and exit codes](../src/SIL.Motif.Help/Content/en/guide/agents/output-and-exit-codes.md), and [Work with jobs](../src/SIL.Motif.Help/Content/en/guide/agents/work-with-jobs.md). Command and glossary Help is authored under `src/SIL.Motif.Help/Content/en/` and embedded by `SIL.Motif.Help` with logical resource names beginning `help/`.

## Process and data boundary

Scripts and external integrations call the `motif` executable and read its JSON responses. Those response shapes are defined in `SIL.Motif.Contract`, which has no LibLCM reference; the repository targets `net10.0` only. Non-.NET integrations can read the JSON contract without loading a Motif assembly. The current response types are in [`SIL.Motif.Contract.Responses`](../src/SIL.Motif.Contract/Responses/), and the shared failure envelope and exit-code mapping are defined by [`FailureEnvelope`](../src/SIL.Motif.Contract/Responses/FailureEnvelope.cs).

FieldWorks integration follows the save-boundary contract: release the project, call `motif apply --all-pending`, inspect the documented response, then reload when required. See the [compatibility target and integration contract](../AGENTS.md#compatibility-targets) rather than relying on older in-process `net48` plans.

## Normative references

- [Current architecture](current-architecture.md) — actual project-reference graph and ownership boundaries.
- [Semantic change contract](change-set-contract.md) — portable Proposal input and semantic rules.
- [Proposal lifecycle](proposal-lifecycle.md) — workflow and state transitions.
- [Shared Help and agent Guides](../src/SIL.Motif.Help/Content/en/guide/) — product explanations for CLI and desktop readers.
