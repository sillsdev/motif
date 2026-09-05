# Motif

**A PR-like collaboration system for semantic changes to language data.**

Motif lets humans and AI agents propose, inspect, check, discuss, approve, apply, and audit changes to
lexical and grammar data in a FieldWorks project. Grammar is the first product customer.

The model is Git/GitHub-like: exact candidate revisions, semantic diffs, CI-style checks, typed
review, approvals, stale-input detection, controlled landing, and auditable outcomes. A Proposal is
reviewed before it lands, not merged after the fact.

Motif does not put Git commits or textual patches around `.fwdata`. Its canonical input is a
**Proposal** containing named semantic operations such as `MergeLexicalEntries`, `SplitSense`, or
`CreateAffixProcessRule`. Those operations are lowered into LibLCM mutations and applied through one
unit of work.

> **Status: this is the target architecture and delivery plan, not the current implementation.**
>
> The repository contains a tested CLI-first control path, generated operation families, Proposal workflow,
> scratch Dry Runs, and stored Assessment reporting. The paired database, durable job system, PanGloss
> orchestrator, Apply Authorization, and Motif application are specified but not built. Nothing in the plans
> should be read as already shipped. The named-pipe worker protocol described in older documents has been
> withdrawn ([ADR 0040](docs/adr/0040-one-api-the-cli.md)). Verb implementations now live behind one typed
> command catalog that the CLI and a planned Motif application both call in-process
> ([ADR 0043](docs/adr/0043-one-command-catalog-two-front-ends.md)); the CLI remains the complete front end —
> the door an AI agent, a script, or a separate FieldWorks uses.

**Start with [Plan A](docs/plan-motif.md).** It is the live plan and owns both the milestones and the
work items.

## Delivery

**Motif delivers two user-facing things: the `motif` CLI and a Motif-owned Avalonia application.** Both are
front ends over
[one typed command catalog](docs/adr/0043-one-command-catalog-two-front-ends.md), so a result or a refusal is
the same whichever door it came through. A separate FieldWorks is not a third front end — it integrates by
calling exactly one CLI verb.

| | |
| --- | --- |
| `motif` CLI | `net10.0`. Batch, automation, and AI-agent use; the complete front end — every catalogued command has a verb — and owns the live project while it holds the FieldWorks lock |
| `SIL.Motif.App` | Planned, `net10.0` Avalonia, in this repository. Calls the catalog in-process; never parses `--json` and never holds a live project model |
| FieldWorks integration | One CLI call, `motif apply --all-pending`, run at a save boundary with the project released. FieldWorks reloads afterward, the FLExBridge pattern |

Everything else is infrastructure or a dependency — a job runner installed with Motif takes work that must
outlive a command, PanGloss is a subprocess, and `SIL.Motif.Contract` is a published contract that a separate
FieldWorks and non-.NET runners consume as the normative description of Motif's field and response shapes.
There is no Motif web app, network service, or mobile surface.

## Scope

**v1 is lexical and grammar. Text and analysis are staged, not excluded**
([ADR 0017](docs/adr/0017-text-and-analysis-destination-scope.md)). Today the Manifest classifies
`Segment`, `WfiAnalysis`, `WfiWordform`, `Text`, and `CmAgent` as `out` / `not-domain-reachable`,
leaving eight text-adjacent rows in scope, and text supplies immutable *evidence* — occurrences,
context, and selected analyses.

They are in the destination because **coverage gaps are the feeding ground for new and refined
rules**: the words no rule explains yet are the work queue, not a score. What defers them is not
appetite but identity — a manual analysis is two facts, and while *this analysis is human-approved*
has a durable GUID, *this occurrence uses it* has no durable identity anywhere in the model. Authorable
text still needs an occurrence-anchor contract, Unicode normalization and segmentation coordinates,
standoff annotations, provenance, reanchoring and refusal, lowering, and read-back. **Text import is
separable and much cheaper** — `Text`, `StText`, `StTxtPara`, and `Segment` are ordinary GUID-bearing
objects that fit the contract today.

Linked media is explicitly deferred. Deleting a lexical entry or other owner follows FieldWorks' normal
LibLCM cascade and may delete owned picture or audio reference objects, but Motif neither copies nor deletes
the external files. Adding, replacing, holding, restoring, or packaging linked media requires a separate
storage design. The binding initial boundary is
[the linked-media specification](docs/media-boundary-spec.md).

## The intended workflow

```text
author human or agent
        │
        ▼
immutable Proposal revision ────────┐
        │                            │ exact input binding
        ├── semantic validation      │
        ├── static-analysis checks   │
        ├── Motif Dry Run            │ what would change in LibLCM?
        ├── PanGloss Assessment      │ what happens to parsing?
        └── conformance/security     │
                                     ▼
                          typed Reviews and Decision
                                     │
                                final Preflight
                                     │
                         controlled atomic application
                                     │
                                     ▼
                         Receipt or explicit refusal
```

A review or check applies only to the exact Proposal revision, Baseline Token, artifacts, tool
contract, and policy revision it evaluated. Changed inputs make that evidence stale.

Every Apply receives an immediate `Applied` Receipt, explicit `Refused` result, or `Needs Reconciliation`
result after an ambiguous persistence boundary; it is never queued or deferred. Nothing is applied silently,
and a Proposal that cannot preserve authored meaning or a language-project invariant is refused
deterministically rather than merged optimistically.

## Responsibilities

| Component | Responsibility |
| --- | --- |
| **Motif** | Semantic operations; Proposal, Check Run, Review, Decision, Dry Run, authorization, rebase, and Receipt contracts |
| **Motif application** | The Avalonia surface a linguist keeps open beside FieldWorks — project selection, Proposal review, the Handoff. Calls the command catalog in-process; never parses `--json` and never holds a live project model |
| **Motif job runner** | Durable jobs, Baselines, per-project queues, PanGloss limits, cleanup, and reconciliation. Claims work from the paired database; nothing asks it anything |
| **LibLCM / FieldWorks** | Model invariants, project lifecycle, unit of work, persistence, and compatibility validation. **The only authority on Motif's path** |
| **FieldWorks integration** | Calls exactly one verb, `motif apply --all-pending`, at a save boundary with the project released, and reloads afterward — the pattern FLExBridge already uses. Lists, shows, trials, and authors nothing |
| **Lexbox** | Optional future sharing of Proposal and Receipt records |
| **PanGloss** | Immutable parser Assessments and parser facts; Motif policy decides what evidence is required |

Motif owns the Manifest, the generator, the semantic operations, and the lowering rules. **Its operations
target LibLCM objects directly** — there is no intermediate model and no generated code lands anywhere
else. The Manifest is an authority on only two things, scope and Construct naming; verbs and comparison
behaviour are derived from LibLCM's own declarations and checked against it
([ADR 0022](docs/adr/0022-structure-is-derived-policy-is-five-rows.md)).

## Authority

**The live LibLCM model is the only authority for language data.** The process owning the loaded `LcmCache`
is its sole writer. The sibling `Project.motif.db` owns workflow records, not a competing copy of linguistic
state.

Dry Runs never mutate the live model. They run against throwaway caches made from a saved, minimal,
file-backed Baseline. FieldWorks can therefore remain open while the CLI authors and evaluates Proposals
against an older Baseline; the result says “run against the project as of X” and warns when live state has
moved. Apply remains immediate in the live host and performs a final Preflight against the exact approved
evidence ([ADR 0039](docs/adr/0039-one-worker-baseline-and-live-host-authority.md)).

## Grammar first

Grammar is the first semantic customer because it exercises the difficult parts of the design:

- roughly 30 grammar Constructs and their cross-references;
- phonological rule order where sequence encodes feeding and bleeding;
- alpha variables whose current LibLCM representation derives identity from position, with a hard
  24-per-rule ceiling that throws and kills the whole grammar load;
- HermitCrab validation and interpretation;
- real-project round trips through the FieldWorks adapter and LibLCM;
- parser evidence through PanGloss Assessments.

A lexical `setGloss` operation is used only as the lifecycle control for baselines, reviews, Drift,
recovery, and Receipts. One grammar Construct then proves the full path before the remaining grammar
volume is generated. Lexical coverage expands afterward from the same Manifest.

## Delivery plan

- **[Plan A](docs/plan-motif.md)** — the live plan: milestones, model join, generator, scratch-cache
  dry run, FieldWorks adapter, review domain;
- [worker, Baseline, Dry Run, and Apply specification](docs/superpowers/specs/2026-08-20-baseline-dry-run-session-design.md)
  — the accepted local-process, queue, storage, and authority contract;
- [worker architecture implementation plan](docs/superpowers/plans/2026-08-22-motif-worker-baseline-implementation.md)
  — staged, test-first handoff for building it;
- [work in other repositories](docs/plan-cross-repo.md) — FieldWorks, PanGloss, liblcm, lexbox;
- [product architecture](docs/plan-product-architecture.md) — normative end-state boundaries;
- [overall product plan](docs/motif-overall-plan.md) — user workflow, evidence corpus, UI, and CLI.

An earlier design routed changes through a CRDT merge layer instead of LibLCM. It was assessed and
rejected; the reasoning is kept once, in [the adoption report](docs/harmony-adoption-report.md), and is a
historical record rather than part of any plan.

**Two scopes** ([ADR 0020](docs/adr/0020-cli-first-fieldworks-planned-not-built.md)). Scope 1
establishes the LibLCM seams and proves them through the CLI, with an AI agent as the author. Scope 2 is
the FieldWorks integration — planned in full, and deliberately not built until scope 1 works.

Milestone ids are stable; the order is `M1 → M2 → M4 → M5 → M6`, then scope 2.

| | | Scope |
| --- | --- | --- |
| **M1** | fail-closed model join, and a generator that reads it without a liblcm checkout | 1 |
| **M2** | one generated operation family applied end to end from the CLI, with a scratch-cache dry run | 1 |
| **M4** | a Proposal authored by an agent, reviewed, approved, applied, with a durable Receipt | 1 |
| **M5** | one grammar Construct authored, applied, and parsed by PanGloss | 1 |
| **M6** | the remaining grammar surface, and the ordered residue proven on real projects | 1 |
| **M3** | FieldWorks hosts Dry Run and Apply in-process, on `net48` | **2** |
| **M4b** | Receipts shared between people, through Lexbox | **2** |

M1 and M2 are mechanical. **M4 is the product**, and it is AI-facing first — the agent is the first
author, not the last. M5 is the first thing a linguist would recognise as the point.

Scope 2 is planned now so scope 1 cannot make it more expensive: one JSON stack everywhere, a Runner that
never owns a cache, and an apply that never calls `Save` are build-time invariants throughout, not later
concerns. (`netstandard2.0` on Contract/Model/Runner was the same kind of invariant, kept for a `net48`
FieldWorks host under [ADR 0040](docs/adr/0040-one-api-the-cli.md);
[ADR 0043](docs/adr/0043-one-command-catalog-two-front-ends.md) retired it along with that host.)

## Open decisions

The architecture has been source-checked, but it deliberately leaves project-owner decisions open.
**[The Plan A grill queue](docs/grill-plan-a.md) is the live list**, and
[grill-readiness.md](docs/grill-readiness.md) triages it into answered, decided, and genuinely open.

Recently closed by measurement rather than opinion: the scratch-copy question, where a memory-only copy of
a project turned out to lose every writing system's collation and valid-character settings, settling the
design on one canonical path ([ADR 0016](docs/adr/0016-scratch-cache-copy-not-undo.md)); and the manifest's
trustworthiness, where verbs turned out to be a pure function of LibLCM's own declarations, so the
generator derives them instead of trusting hand-typed rows
([ADR 0022](docs/adr/0022-structure-is-derived-policy-is-five-rows.md)).

Still genuinely open, and shaping the design: whether a reviewer can actually see that a phonological
reorder changed the grammar's meaning; whether review state must work offline; how Construct names get
settled, since only about a quarter are derivable from their class; and when the agent-facing contract
stops churning and declares itself stable.

Supporting records: the [decision log](docs/grill-decisions.md), the
[research synthesis](docs/research/2026-08-01-pr-like-collaboration-synthesis.md), and the
[evidence ledger](docs/research/2026-08-01-grill-evidence-ledger.md) — the last two predate Plan A and
are evidence rather than plans.

## Present implementation

The repository currently contains `SIL.Motif.Contract`, `SIL.Motif.Model`, `SIL.Motif.Runner`,
`SIL.Motif.Host`, `SIL.Motif.Cli`, `SIL.Motif.Generator`, and tests. The generator reads
`MasterLCModel.xml` from the NuGet package cache with no LibLCM checkout, joins it to the Manifest
fail-closed, derives operation metadata, and emits the implemented families. The current one-shot CLI can:

- parse and canonicalize a Proposal containing `lexical/lexSense/setGloss`;
- compute stable intent and effect digests;
- open a real FieldWorks project through the host;
- perform a file-backed scratch Dry Run without mutating or rolling back the live cache;
- execute declared prerequisite Proposals once in deterministic canonical order;
- detect footprint Drift and refuse an unbound apply;
- apply in one LibLCM unit of work, read back, persist, and record an applied marker;
- exercise the whole flow through the `motif` CLI — `open`, `analyses`, `new`, `add-set-gloss`,
  `finalize`, `list`, `show`, `dry-run`, `apply`, `log`, `baseline capture`, `assess`, `stats`, `handoff`.

The durable architecture above is not implemented yet. Today each argv invocation owns its process, Proposal
storage is file-based, the CLI opens projects directly, and Apply does not yet use the new authorization and
reconciliation protocol. One process per call is not a limitation being removed — it is the contract
([ADR 0040](docs/adr/0040-one-api-the-cli.md)); what is missing is the job runner behind it.

The analysis aggregate is a cheap read that never invokes PanGloss. Without an Assessment it reports
the project's manually approved analyses:

```powershell
motif analyses --project C:\path\to\project.fwdata [--json]
```

To include automatic analyses from an Assessment already stored in `.motif/motif.db`, name it and
supply both current hashes. Motif compares those caller-supplied values with the stored provenance so
the Report can say whether the Assessment is current or stale without running the parser:

```powershell
motif analyses --project C:\path\to\project.fwdata `
  --assessment <assessmentId> `
  --current-selection-sha256 <sha256> `
  --current-grammar-sha256 <sha256> `
  [--store <dir>] [--json]
```

`motif baseline capture` and `motif assess` are that separate operation, now shipped. A Baseline is a
saved-file capture of a project's semantic state — read from the project's own `.fwdata` on disk, never
from FieldWorks' in-memory state, so everything derived from it is *as of FieldWorks' last save* and
FieldWorks may keep the project open the whole time:

```powershell
motif baseline capture C:\path\to\project.fwdata [--json]
```

`motif assess` ensures a current Baseline exists (capturing one first if none does), composes a Selection
from whichever of four sources are named — every wordform, chosen Texts by GUID, a pasted or typed word
list, and the previous run's failed or slow-to-parse words — sends that Selection through PanGloss, and
records the outcome as Assessments:

```powershell
motif assess C:\path\to\project.fwdata --all-wordforms [--json]
```

`motif stats` then forwards a query straight through to PanGloss's own `stats` command against whichever
Assessment resulted, passing everything written after a standalone `--` through byte-for-byte:

```powershell
motif stats C:\path\to\project.fwdata -- --group pos
```

`motif handoff` composes all three into one self-explaining folder that an AI agent with no network and no
package installer can read on its own: the grammar, the chosen Texts, the exact selection that was parsed,
and — unless `--no-assess` — PanGloss's own statistics, alongside a reader script and reference documents
this repository maintains and copies in unchanged:

```powershell
motif handoff C:\path\to\project.fwdata --out C:\path\to\folder [--texts <guid,guid>] [--flextext] [--json]
```

The folder is built beside the requested `--out` path and moved into place only once its listing is
complete, so a cancelled run or a PanGloss failure leaves no destination directory at all; an existing
non-empty `--out` is refused rather than written into or cleared, so a mistyped path can never erase a real
folder. With no `--texts`, every Text is exported and every wordform selected; `--no-assess` skips the
statistics but still writes the grammar, the Texts, and the selection; `--flextext` additionally writes the
FLExText XML beside each Text's JSON mirror.

See [docs/cli-api.md](docs/cli-api.md) for the full flag set, JSON shapes, and refusal codes for all
four.

This is a tested control and proving surface for one operation kind, not evidence that the planned
product is complete.

**One target, `net10.0`, everywhere**
([ADR 0043](docs/adr/0043-one-command-catalog-two-front-ends.md), superseding
[ADR 0040](docs/adr/0040-one-api-the-cli.md) decision 3). `SIL.Motif.Contract` keeps no LibLCM reference — a
non-.NET runner still reads `motif --json` against it as a wire description — but that no longer requires
building it for `netstandard2.0`, because there is no `net48` host left to satisfy it. See
[AGENTS.md](AGENTS.md#compatibility-targets) for the full rationale.

All LibLCM-dependent projects pin `SIL.LCModel 11.0.0-beta0150`.

Run the tests with:

```powershell
./test.ps1
```

## Vocabulary and design constraints

[CONTEXT.md](CONTEXT.md) is the canonical glossary. In particular:

- **Motif operation** — one named unit of semantic intent;
- **Proposal** — the immutable, reviewable unit of operations;
- **Dry Run** — what a Proposal would do, observed on a throwaway cache opened from a saved Baseline;
- **Assessment** — an immutable PanGloss parser run;
- **Drift** — the live project no longer matches the evaluated baseline;
- **Receipt** — the durable record of controlled application;
- **Manifest** — reviewed classification of the LibLCM model surface;
- **Construct** — one staged grammar capability.

Canonical Proposal input is semantic intent, never a low-level LibLCM mutation script. The **intent
contract is public and versioned**; generated LibLCM Mutation Plans are **private and output-only**,
which is why drift is compared over effects and never over the plan. Only declared dependencies impose
order; array position does not. Unknown operation kinds and semantic properties fail closed. Diff is
exact-identity-based and linguistically unaware. Every operation family must satisfy the complete
schema, semantics, validation, lowering, Dry Run, apply, read-back, conflict/rebase, snapshot/diff,
rollback, round-trip, concurrency, compatibility, and coverage gate before it is complete.

The contract is not private to us: `SIL.Motif.Contract` is deliberately LibLCM-free because non-.NET
runners consume it, and [ADR 0007](docs/adr/0007-cross-language-digest-determinism.md) exists so
digests are reproducible across languages.

## Repository

The project was previously named LCAtom. That name is retired. The repository, product, namespaces,
solution, and CLI are now **Motif**:

<https://github.com/johnml1135/motif>
