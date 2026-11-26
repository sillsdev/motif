# CLI and command API

This page is a current orientation to Motif's command surface, not a second command specification. Older CLI-only, `net48`/`netstandard2.0`, and `--store` descriptions are superseded; the [current architecture overview](architecture.md) describes today's front ends, process coordination and targets.

## Shared command source

The command catalog in `SIL.Motif.Commands` lists typed requests, responses and handlers. The CLI catalog maps each command to its invocation names and usage lines. The Avalonia application calls the shared command handlers in-process rather than invoking the CLI or interpreting CLI JSON. See [`CommandCatalog`](../src/SIL.Motif.Commands/Catalog/CommandCatalog.cs) and [`CliVerbCatalog`](../src/SIL.Motif.Cli/CliVerbCatalog.cs) for those source inventories.

Use the built-in Help output for the current command reference:

```text
motif help
motif help <command> --full
motif help --all --json
```

The shared user-facing Guides, including agent Guides, are authored under [`src/SIL.Motif.Help/Content/en/guide/`](../src/SIL.Motif.Help/Content/en/guide/). For current agent guidance, see [Start here](../src/SIL.Motif.Help/Content/en/guide/agents/start-here.md), [Output and exit codes](../src/SIL.Motif.Help/Content/en/guide/agents/output-and-exit-codes.md), and [Work with jobs](../src/SIL.Motif.Help/Content/en/guide/agents/work-with-jobs.md). Command and glossary Help is authored under `src/SIL.Motif.Help/Content/en/` and embedded by `SIL.Motif.Help` with logical resource names beginning `help/`.

Two entry points are not catalogued commands. `motif mcp [--profile <file | shipped name>] [--activity-log <file.jsonl>]` serves the tools to an AI assistant over standard input and output. `motif settings advanced-ai` reads the current mode; append `on` or `off` to change it. `motif help mcp --full` explains the MCP startup gate. `motif help` lists Advanced AI, Autonomous and Developer verbs with their enablement instructions even while those modes are off; `motif help autonomous` explains the Autonomous audience. When Advanced AI is off, refusals name the setting command; in a sandbox, set `MOTIF_ADVANCED_AI_MODE_PATH` to a writable preference file. Autonomous commands require `MOTIF_AUTONOMOUS=1` or the global `--autonomous` flag (the same switch that makes Apply an Autonomous-mode Apply), and are never MCP tools. Developer commands require `MOTIF_DEVELOPER_COMMANDS=1`, which refusals name directly.

`baseline capture --project <fwdata> [--no-background] [--json]` publishes a Baseline from the saved `.fwdata`. By default it queues a Source index refresh and a Parsimony Baseline bundle job and wakes the job runner, so those jobs commit after the command returns. `--no-background` (MCP `no_background`) is the foreground-only mode for workflows admitted under a lock that ends with the command: it queues no job and starts no runner, and records the Source index as pending (visible in status reads) until a later `baseline capture` or Apply without the switch refreshes it (a runner start does not). `assess`, `dry-run`, `trial` and `apply` take the same switch (MCP `no_background` on `motif_assess`, `motif_dry_run`, `motif_trial`): a Baseline recapture they need runs inline, no Source index or Parsimony job is queued, Apply wakes no runner, and `dry-run` and `trial` require `--wait`. The switch takes no value; `--no-background=false` is refused.

Apply records its receipt before its follow-up stages (Source index queueing, Progress publication, Assessment promotion and cleanup). A stage that meets a locked store waits and retries three times (about 2.6 s of waiting between attempts, on top of the 15 s busy timeout each attempt already allows); one that still fails never turns the Apply into a failure: the result note says the apply succeeded, names the stage, and says what was left undone, and for Source index and Progress names the verb that finishes it (`baseline capture`).

Draft authoring commands, including `label` and `comment`, use Advanced AI mode. The Released Apply commands `apply <proposalId>`, `apply --proposal <id>`, and `apply --all-pending` are human-only and unavailable as agent tools. Plain `apply` has an optional `--force` switch that skips Readiness reasons only; it does not bypass project fit checks. The `apply --proposal` route has no force option.

`store artifacts --root <worker-root> --stores <stores.json> --json` is a CLI-only Developer command for a read-only inventory of `assessment-runs`, `assessment-sources`, and `baselines`. The manifest has `schemaVersion: 1`, a `referenceDomainComplete` declaration, and an explicit `stores` array; each entry names one database path, its project path, and its FieldWorks identity. List every store that may refer to the named worker root before declaring the domain complete. The command does not discover stores from `KnownProjects`, accept globs, start a Worker or parser, or expose an App or MCP route.

For each listed store, inventory acquires the existing `.use.lock` without creating it, copies the database and any existing WAL/SHM sidecars to private scratch, and validates only that snapshot. Hard-linked database, sidecar, or generated-artifact paths are refused; when platform file identity cannot be proved, affected records remain `Unknown`. Assessment BaselineTokens resolve only through exact recorded Baseline tokens; malformed or unmatched tokens leave the Baseline family uncertain. A missing or busy lock, unsupported store, malformed reference shape, or incomplete declared domain leaves affected files `Unknown`. The response reports exact paths, logical bytes, owners, retaining records and reason codes without linguistic payload. `referenceDomainComplete` reports whether the named stores and supported references were understood; `inventoryComplete` reports whether the bounded path walk finished. The total `logicalBytes` is omitted when the reference domain is incomplete or when a link, changing path, or scan limit prevents a complete walk. This inventory-only slice reports no `Eligible` files and has no apply command; it does not authorize deletion.

Example manifest:

```json
{
  "schemaVersion": 1,
  "referenceDomainComplete": true,
  "stores": [
    {
      "databasePath": "/data/project.motif.db",
      "projectPath": "/data/project.fwdata",
      "projectIdentity": "fieldworks-project-identity"
    }
  ]
}
```

## Process and data boundary

Scripts and external integrations call the `motif` executable and read its JSON responses. Those response shapes are defined in `SIL.Motif.Contract`, which has no LibLCM reference; the repository targets `net10.0` only. Non-.NET integrations can read the JSON contract without loading a Motif assembly. The current response types are in [`SIL.Motif.Contract.Responses`](../src/SIL.Motif.Contract/Responses/), and the shared failure envelope and exit-code mapping are defined by [`FailureEnvelope`](../src/SIL.Motif.Contract/Responses/FailureEnvelope.cs).

Each `show --json` operation includes `placement` and `storageIdOverride`. An unspecified field is explicitly null. A placement object names the preceding neighbor with `after`, the following neighbor with `before`, or both. Clients comparing authored intent can detect incomplete older records by checking for both top-level fields before comparing them.

Detailed `assess --json` readings carry a `signature` whose `morphemes` array is in parser order. Each morpheme identifies its entry, allomorph, MSA, and applicable inflection type. A resolved sense carries its ID. A parser result that could mean several senses sharing one MSA lists the candidates with `senseStatus: "ambiguous"`. A stored FieldWorks analysis uses its selected sense when available. Feature values name their authored role, full path, and symbolic value IDs. An empty `featureValues` array means the supported feature structures were read and had no values. A null Reading signature means canonical identity is unavailable, for example when a source reference cannot be resolved or a feature structure cannot be represented. The parser's reading positions are in `sourceIndex`, and repeated readings are retained.

New Assessment records freeze this identity from the measured source. Stored Assessment and Trial word evidence carry it with the ordered parser analyses when available.

Trial review preserves observed Readings in order, including repeated Readings. A Reading with unavailable canonical identity has `identityState: "unavailable"`, `identityDigest: null`, and `signature: null`. If either side contains such a Reading, the canonical comparison stays unknown and the case remains visible under the `review` filter.

FieldWorks integration follows the save-boundary contract: release the project, call `motif apply --all-pending`, inspect the documented response, then reload when required. See the [compatibility target and integration contract](../AGENTS.md#compatibility-targets) rather than relying on older in-process `net48` plans.

## Normative references

- [Current architecture](architecture.md) — actual project-reference graph and ownership boundaries.
- [Semantic change contract](proposal-contract.md) — portable Proposal input and semantic rules.
- [Proposal lifecycle](proposal-lifecycle.md) — workflow and state transitions.
- [Shared Help and agent Guides](../src/SIL.Motif.Help/Content/en/guide/) — product explanations for CLI and desktop readers.
