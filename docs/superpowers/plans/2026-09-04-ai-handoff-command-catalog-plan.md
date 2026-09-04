# AI handoff phase 1: command catalog implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for
> tracking.

**Goal:** Replace the CLI-owned command implementations with one typed command catalog that the CLI and the
future Avalonia application can both call, without changing any existing verb's observable behaviour.

**Architecture:** `SIL.Motif.Commands` owns command decisions and store orchestration. Each handler accepts a
typed request and returns `CommandOutcome<T>` containing either a typed Contract response or a typed `Refusal`;
the CLI alone parses argv, renders text or JSON, records invocation usage, and wakes the job runner. A catalog
descriptor list is the mechanically checked join between handlers and CLI verbs.

**Tech Stack:** C# 14, `net10.0`, System.Text.Json, SQLite, LibLCM, xUnit.

**Depends on:** [the approved design](../specs/2026-09-04-ai-handoff-design.md).

**Produces the seam consumed by:**
[phase 2](2026-09-04-ai-handoff-pipeline-plan.md) and
[phase 3](2026-09-04-ai-handoff-application-plan.md).

**Standing rules:**

- Run `./test.ps1` for every red/green check and in the foreground before every commit; it intentionally has
  no filter mode.
- Do not change a human sentence during extraction. Typed carriers and stable codes are new; wording is not.
- A command handler contains no `Console`, no JSON serialization, and no text rendering.
- The CLI contains no store, LibLCM, Proposal, Assessment, or job decision after this phase.
- Use `./build.ps1` and `./test.ps1`, never `dotnet build` or `dotnet test` directly.

---

### Task 1: Record the new boundary before code starts

**Files:**

- Create: `docs/adr/0043-one-command-catalog-two-front-ends.md`
- Modify: `AGENTS.md`
- Modify: `CONTEXT.md`
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-09-03-motif-application-direction-design.md`

- [ ] **Step 1: Write ADR 0043.** Its non-specialist opener says that Motif's window and command line now do
  the same work through one shared catalog, so results and refusals cannot diverge. Its decisions supersede
  ADR 0040 decisions 1–3 and retain decisions 4–7. State these invariants explicitly:

```text
1. Command implementations are in-process and UI-agnostic.
2. Every command returns a typed result or typed Refusal.
3. Every catalogued command has a CLI verb; store-free interaction may remain application-only.
4. Every Motif project targets net10.0; Contract remains LibLCM-free and is a wire description, not a
   binary compatibility promise to net48.
5. The database remains the only coordination boundary between Motif processes.
```

- [ ] **Step 2: Amend the binding repository text.** Update `AGENTS.md`'s compatibility table and rationale,
  the `Motif API` glossary entry, and README's project/FieldWorks descriptions. Remove claims that Contract
  or Runner still needs `netstandard2.0`; retain the rule that Contract has no LibLCM dependency.
- [ ] **Step 3: Check the documentation for contradictory live claims.** Run:

```powershell
rg -n "netstandard2\.0|one API|FieldWorks.*Contract|Contract.*FieldWorks" AGENTS.md CONTEXT.md README.md `
  docs/superpowers/specs/2026-09-03-motif-application-direction-design.md
```

Expected: historical statements are clearly labelled as superseded; current statements all describe the
catalog and `net10.0`.

- [ ] **Step 4: Run the repository gate and commit.** Run `./build.ps1` and expect comment hygiene and compile
  to pass. Commit:

```powershell
git add AGENTS.md CONTEXT.md README.md docs/adr/0043-one-command-catalog-two-front-ends.md `
  docs/superpowers/specs/2026-09-03-motif-application-direction-design.md
git commit -m "docs: adopt one typed command catalog"
```

---

### Task 2: Retarget Contract and delete its compatibility shim

**Files:**

- Modify: `src/SIL.Motif.Contract/SIL.Motif.Contract.csproj`
- Modify: `src/SIL.Motif.Runner/SIL.Motif.Runner.csproj`
- Delete: `src/SIL.Motif.Contract/Compatibility/IsExternalInit.cs`
- Modify: `tests/SIL.Motif.Tests/Contract/CompatibilityTargetTests.cs`

- [ ] **Step 1: Write the failing target test.** Replace the old compatibility assertions with:

```csharp
[Fact]
public void EveryProductProjectTargetsOnlyNet10()
{
    foreach (var project in ProductProjects())
    {
        var document = XDocument.Load(project);
        var targets = document.Descendants()
            .Where(element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
            .Select(element => element.Value)
            .ToArray();
        Assert.Equal(["net10.0"], targets);
    }
}

[Fact]
public void ContractHasNoLibLcmReference()
{
    var text = File.ReadAllText(Project("SIL.Motif.Contract"));
    Assert.DoesNotContain("SIL.LCModel", text, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run red.** Run `./test.ps1`. Expected: `CompatibilityTargetTests` fails because Contract still
  targets `netstandard2.0`; all earlier tests retain their prior result.
- [ ] **Step 3: Retarget and simplify.** Set Contract to `<TargetFramework>net10.0</TargetFramework>`, remove
  the explicit `System.Text.Json` package, delete `IsExternalInit.cs`, and replace Runner's stale
  multi-targeting comment with the current single-runtime reason.
- [ ] **Step 4: Run `./test.ps1`.** Expected: all tests pass and no output path contains `netstandard2.0`.
- [ ] **Step 5: Commit.**

```powershell
git add src/SIL.Motif.Contract src/SIL.Motif.Runner tests/SIL.Motif.Tests/Contract/CompatibilityTargetTests.cs
git commit -m "build: target net10 across motif"
```

---

### Task 3: Add the typed outcome and refusal contracts

**Files:**

- Create: `src/SIL.Motif.Contract/Commands/CommandOutcome.cs`
- Create: `src/SIL.Motif.Contract/Commands/Refusal.cs`
- Modify: `src/SIL.Motif.Contract/Responses/FailureEnvelope.cs`
- Create: `tests/SIL.Motif.Tests/Contract/CommandOutcomeTests.cs`
- Modify: `tests/SIL.Motif.Tests/Contract/ResponseBindingTests.cs`

- [ ] **Step 1: Write tests for the closed carrier.** Pin success, refusal, JSON round-trip, nonblank stable
  code, nonblank message, immutable facts, and the impossibility of holding both a value and a refusal.

```csharp
[Fact]
public void RefusedOutcomeCarriesBranchableFacts()
{
    var refusal = new Refusal("proposal.not-found", FailureReason.NotFound,
        "Proposal proposal/absent was not found.",
        new Dictionary<string, string> { ["proposalId"] = "proposal/absent" });

    var outcome = CommandOutcome<string>.Refused(refusal);

    Assert.False(outcome.Succeeded);
    Assert.Null(outcome.Value);
    Assert.Equal("proposal/absent", outcome.Refusal!.Facts["proposalId"]);
}
```

- [ ] **Step 2: Run red.** Expected: `CommandOutcome<>` and `Refusal` do not exist.
- [ ] **Step 3: Implement the contracts.** Use this public shape:

```csharp
public sealed record Refusal
{
    public Refusal(string code, FailureReason reason, string message,
        IReadOnlyDictionary<string, string>? facts = null)
    {
        Code = string.IsNullOrWhiteSpace(code)
            ? throw new ArgumentException("A stable refusal code is required.", nameof(code))
            : code;
        Reason = reason;
        Message = string.IsNullOrWhiteSpace(message)
            ? throw new ArgumentException("A refusal message is required.", nameof(message))
            : message;
        Facts = facts is null
            ? ImmutableDictionary<string, string>.Empty
            : facts.ToImmutableDictionary(StringComparer.Ordinal);
    }

    public string Code { get; }
    public FailureReason Reason { get; }
    public string Message { get; }
    public IReadOnlyDictionary<string, string> Facts { get; }
}

public sealed record CommandOutcome<T>
{
    private CommandOutcome(T? value, Refusal? refusal)
    {
        if ((value is null) == (refusal is null))
            throw new ArgumentException("A command outcome requires exactly one value or refusal.");
        Value = value;
        Refusal = refusal;
    }

    public bool Succeeded => Refusal is null;
    public T? Value { get; }
    public Refusal? Refusal { get; }
    public static CommandOutcome<T> Success(T value) => new(value, null);
    public static CommandOutcome<T> Refused(Refusal refusal) => new(default, refusal);
}

```

`FailureEnvelope` gains `Code` and maps `Detail` from `Refusal.Facts`; its existing reason-to-exit-code map
stays unchanged.
- [ ] **Step 4: Run `./test.ps1`.** Expected: the Contract tests and full suite pass.
- [ ] **Step 5: Commit.**

```powershell
git add src/SIL.Motif.Contract/Commands src/SIL.Motif.Contract/Responses/FailureEnvelope.cs `
  tests/SIL.Motif.Tests/Contract
git commit -m "feat: add typed command outcomes"
```

---

### Task 4: Create `SIL.Motif.Commands` and move the decision-bearing files

**Files:**

- Create: `src/SIL.Motif.Commands/SIL.Motif.Commands.csproj`
- Move: `src/SIL.Motif.Cli/Commands.cs` to `src/SIL.Motif.Commands/ProposalCommands.cs`
- Move: `src/SIL.Motif.Cli/CompareCommands.cs` to `src/SIL.Motif.Commands/CompareCommands.cs`
- Move: `src/SIL.Motif.Cli/ConfigCommands.cs` to `src/SIL.Motif.Commands/ConfigCommands.cs`
- Move: `src/SIL.Motif.Cli/CorpusCommands.cs` to `src/SIL.Motif.Commands/CorpusCommands.cs`
- Move: `src/SIL.Motif.Cli/JobCommands.cs` to `src/SIL.Motif.Commands/JobCommands.cs`
- Move: `src/SIL.Motif.Cli/OperationDependencyGraph.cs` to `src/SIL.Motif.Commands/OperationDependencyGraph.cs`
- Move: `src/SIL.Motif.Cli/ProjectStoreCommand.cs` to `src/SIL.Motif.Commands/ProjectStoreCommand.cs`
- Move: `src/SIL.Motif.Cli/ReportCommands.cs` to `src/SIL.Motif.Commands/ReportCommands.cs`
- Move: `src/SIL.Motif.Cli/Store/` to `src/SIL.Motif.Commands/Store/`
- Modify: `Motif.sln`, `src/SIL.Motif.Cli/SIL.Motif.Cli.csproj`,
  `tests/SIL.Motif.Tests/SIL.Motif.Tests.csproj`
- Modify: CLI tests that import `SIL.Motif.Cli`

- [ ] **Step 1: Add a dependency-direction test.** Assert that `SIL.Motif.Commands` references Contract,
  Host, Model, Projection, Runner, LiveHost, and Worker; CLI references Commands and Contract; Commands does
  not reference CLI.
- [ ] **Step 2: Run red.** Expected: the Commands project does not exist.
- [ ] **Step 3: Add the project.** Its reference block is:

```xml
<ItemGroup>
  <ProjectReference Include="..\SIL.Motif.Contract\SIL.Motif.Contract.csproj" />
  <ProjectReference Include="..\SIL.Motif.Host\SIL.Motif.Host.csproj" />
  <ProjectReference Include="..\SIL.Motif.Model\SIL.Motif.Model.csproj" />
  <ProjectReference Include="..\SIL.Motif.Projection\SIL.Motif.Projection.csproj" />
  <ProjectReference Include="..\SIL.Motif.Runner\SIL.Motif.Runner.csproj" />
  <ProjectReference Include="..\SIL.Motif.LiveHost\SIL.Motif.LiveHost.csproj" />
  <ProjectReference Include="..\SIL.Motif.Worker\SIL.Motif.Worker.csproj" />
</ItemGroup>
```

- [ ] **Step 4: Move the files without changing behaviour.** Use `git mv`, change namespaces from
  `SIL.Motif.Cli` to `SIL.Motif.Commands`, update test imports, and rename the former `Commands` class to
  `ProposalCommands`. Leave `Program.cs`, `RunnerKick.cs`, `NeedsReconciliationException.cs`, argv parsing,
  usage text, and all rendering in CLI.
- [ ] **Step 5: Run `./test.ps1`.** Expected: the same test count and all tests pass.
- [ ] **Step 6: Commit.**

```powershell
git add Motif.sln src/SIL.Motif.Commands src/SIL.Motif.Cli tests/SIL.Motif.Tests
git commit -m "refactor: extract command implementation project"
```

---

### Task 5: Type Proposal and project commands

**Files:**

- Create: `src/SIL.Motif.Contract/Responses/ProposalCommandResponses.cs`
- Create: `src/SIL.Motif.Commands/Requests/ProposalCommandRequests.cs`
- Modify: `src/SIL.Motif.Commands/ProposalCommands.cs`
- Create: `src/SIL.Motif.Cli/Rendering/ProposalCommandRenderer.cs`
- Modify: `src/SIL.Motif.Cli/Program.cs`
- Modify: `tests/SIL.Motif.Tests/Cli/CommandsRefusalsTests.cs`
- Create: `tests/SIL.Motif.Tests/Commands/ProposalCommandOutcomeTests.cs`

- [ ] **Step 1: Define and test the typed responses.** Use these response records; existing projection types
  remain the response for `open`, `analyses`, `list`, `show`, `apply`, and `log`:

```csharp
public sealed record DraftCreatedResponse(string DraftName, string ProposalId, string? Label);
public sealed record DraftChangedResponse(string DraftName, int OperationCount, IReadOnlyList<string> OperationIds);
public sealed record DraftFieldChangedResponse(string DraftName, string Field, string Value);
public sealed record ProposalFinalizedResponse(string DraftName, string ProposalId, string IntentDigest);
public sealed record DraftDiscardedResponse(string DraftName);
public sealed record ProposalStatusChangedResponse(string ProposalId, string Status, string? RelatedProposalId);
public sealed record ProposalSplitResponse(string SourceProposalId, IReadOnlyList<DraftChangedResponse> Drafts);
```

Requests are one immutable record per public handler, with the current method parameters in their current
order and names. `CancellationToken` is a handler parameter, not serialized request data.
- [ ] **Step 2: Run red.** Expected: the typed records are absent and the handlers still return `CommandResult`.
- [ ] **Step 3: Convert success paths.** Each handler returns `CommandOutcome<T>` and constructs the records
  above or the existing projection. Delete `OpenJson`, `AnalysesJson`, `ListJson`, `ShowJson`, `ApplyJson`, and
  `LogJson`; format is no longer a command concern.
- [ ] **Step 4: Convert every Proposal refusal.** Use stable lower-case dot codes and exact facts. The required
  families are:

```text
project.not-found, project.busy, store.unsupported, store.inconsistent
draft.not-found, draft.name-collision, draft.invalid
proposal.not-found, proposal.invalid-id, proposal.invalid-status, proposal.inconsistent
operation.invalid-id, operation.invalid-target, operation.invalid-writing-system,
operation.invalid-dependency, operation.slot-collision, operation.cascading-delete
corpus.not-found, corpus.document-not-found
apply.project-in-use, apply.dry-run-missing, apply.drift, apply.not-ready
```

For dynamic ids, targets, statuses, and field names, add those values to `Facts`. Preserve the current
message byte-for-byte apart from removing the `error: ` prefix, which belongs to the CLI renderer.
- [ ] **Step 5: Recreate the human output in `ProposalCommandRenderer`.** Make it a total type switch. JSON
  serialization uses the same response record directly. A refusal becomes `FailureEnvelope` only here.
- [ ] **Step 6: Strengthen the refusal suite.** Each former `Fail`, `Invalid`, `Missing`, and
  `ProjectStoreCommand.Refuse` branch asserts code, reason, message, facts, and unchanged store bytes. Add a
  source guard:

```csharp
var commandsRoot = Path.Combine(RepoPaths.FindRepoRoot(), "src", "SIL.Motif.Commands");
Assert.Empty(Directory.EnumerateFiles(commandsRoot, "*.cs", SearchOption.AllDirectories)
    .SelectMany(File.ReadLines)
    .Where(line => line.Contains("CommandResult", StringComparison.Ordinal)
        || line.Contains("error: ", StringComparison.Ordinal)));
```

- [ ] **Step 7: Run `./test.ps1`.** Expected: current human snapshots and
  exit codes stay unchanged; JSON failures now also carry `code` and `detail`.
- [ ] **Step 8: Commit.**

```powershell
git add src/SIL.Motif.Contract src/SIL.Motif.Commands src/SIL.Motif.Cli tests/SIL.Motif.Tests
git commit -m "refactor: type proposal command results"
```

---

### Task 6: Type Corpus, configuration, Report, comparison, and job commands

**Files:**

- Create: `src/SIL.Motif.Contract/Responses/WriteCommandResponses.cs`
- Create: `src/SIL.Motif.Commands/Requests/CorpusCommandRequests.cs`
- Create: `src/SIL.Motif.Commands/Requests/AssessmentCommandRequests.cs`
- Modify: `src/SIL.Motif.Commands/CorpusCommands.cs`, `ConfigCommands.cs`, `ReportCommands.cs`,
  `CompareCommands.cs`, `JobCommands.cs`
- Create: `src/SIL.Motif.Cli/Rendering/CommandTextRenderer.cs`
- Modify: corresponding tests under `tests/SIL.Motif.Tests/Cli/`

- [ ] **Step 1: Add the remaining response records and binding tests.** Existing Contract projections remain
  authoritative for reads. Add only the write acknowledgements that do not yet have a shape:

```csharp
public sealed record CorpusAddedResponse(string CorpusId, string Description);
public sealed record CorpusDocumentAddedResponse(string CorpusId, string DocumentId, string Source);
public sealed record CorpusBundleAddedResponse(string CorpusId, int DocumentCount);
public sealed record JobEnqueuedResponse(string JobId, string Kind, string ProjectKey);
public sealed record JobMovedResponse(string JobId, double QueueOrder);
public sealed record JobStateChangedResponse(string JobId, string Status);
```

- [ ] **Step 2: Run red.** Expected: command groups still return rendered `CommandResult`.
- [ ] **Step 3: Convert the handlers.** Remove every `asJson` parameter and return existing response records
  or the new acknowledgements. Give every refusal a stable code under `config.*`, `corpus.*`, `report.*`,
  `comparison.*`, or `job.*`; put ids, requested kind, status, and queue target in `Facts`.
- [ ] **Step 4: Move all rendering to CLI.** Keep current text byte-for-byte and serialize the typed value for
  JSON. `RunnerKick.After()` stays immediately after successful enqueue/requeue in `Program.cs`.
- [ ] **Step 5: Run `./test.ps1`.** Expected: pass with no `CommandResult` in
  Commands.
- [ ] **Step 6: Commit.**

```powershell
git add src/SIL.Motif.Contract src/SIL.Motif.Commands src/SIL.Motif.Cli tests/SIL.Motif.Tests
git commit -m "refactor: type every command result"
```

---

### Task 7: Build the catalog and enforce CLI parity

**Files:**

- Create: `src/SIL.Motif.Commands/Catalog/CommandCatalog.cs`
- Create: `src/SIL.Motif.Commands/Catalog/CommandDescriptor.cs`
- Create: `src/SIL.Motif.Cli/CliVerbCatalog.cs`
- Create: `tests/SIL.Motif.Tests/Commands/CommandCatalogParityTests.cs`
- Modify: `src/SIL.Motif.Cli/Program.cs`

- [ ] **Step 1: Write the failing parity tests.** Pin unique command names, unique request types, unique
  refusal codes, and exact equality between catalog names and CLI verb names. Nested CLI verbs use their
  complete names (`config show`, `jobs assessments`, `jobs cancel`, `jobs list`, `jobs move`, `jobs requeue`,
  `jobs show`).

```csharp
[Fact]
public void EveryCataloguedCommandHasExactlyOneCliVerb()
{
    var commands = CommandCatalog.All.Select(command => command.Name).Order().ToArray();
    var verbs = CliVerbCatalog.All.Select(verb => verb.CommandName).Order().ToArray();
    Assert.Equal(commands, verbs);
}
```

- [ ] **Step 2: Run red.** Expected: the two catalogs do not exist.
- [ ] **Step 3: Implement descriptors.** Keep this reflection-bearing type in Commands, not the wire-only
  Contract assembly:

```csharp
public sealed record CommandDescriptor(string Name, Type RequestType, Type ResponseType);
```

  `CommandCatalog.All` is the sole enumeration of command handlers; each descriptor contains the command name
  and its request/response types. `CliVerbCatalog.All` owns usage
  and dispatch metadata; `PrintUsage` iterates it rather than repeating a second hand-written list.
- [ ] **Step 4: Make `Program.cs` a shell.** It may parse argv, record Known projects and usage shapes, invoke
  one handler, wake the runner after queue-changing successes, and render. It may not open either database or
  inspect a command response to make a domain decision.
- [ ] **Step 5: Add source-boundary tests.** Assert no command project file contains `Console.`,
  `ProjectionJson.Serialize`, or CLI namespaces; assert CLI has no references to `Microsoft.Data.Sqlite` or
  `SIL.LCModel`.
- [ ] **Step 6: Run `./test.ps1`.** Expected: pass.
- [ ] **Step 7: Commit.**

```powershell
git add src/SIL.Motif.Commands src/SIL.Motif.Cli tests/SIL.Motif.Tests/Commands `
  tests/SIL.Motif.Tests/Cli
git commit -m "test: enforce command and cli parity"
```

---

### Task 8: Phase acceptance gate

**Files:**

- Modify only if verification exposes a defect.

- [ ] **Step 1: Run `./build.ps1`.** Expected: comment hygiene and compilation pass.
- [ ] **Step 2: Run `./test.ps1` in the foreground.** Expected: the full suite passes; record the count in the
  commit message or handoff.
- [ ] **Step 3: Run these structural checks.** Expected: all return no matches except the intentional CLI
  renderer references.

```powershell
rg -n "Console\.|ProjectionJson\.Serialize|CommandResult" src/SIL.Motif.Commands
rg -n "Microsoft\.Data\.Sqlite|SIL\.LCModel" src/SIL.Motif.Cli
rg -n "netstandard2\.0" src tests Motif.sln
```

- [ ] **Step 4: Exercise one read, one write, one queued job, and one refusal through the executable.** Compare
  human output with the pre-extraction test fixtures and deserialize every `--json` result through Contract.
- [ ] **Step 5: Commit only verification fixes.** Do not squash the prior task commits; they are the review
  boundaries for the extraction.

## Phase 1 exit criteria

- All existing verbs behave identically through the real executable.
- Every command has typed input and typed success/refusal output.
- Every refusal has a stable code and branchable facts.
- The catalog and CLI verb set are equal by test.
- `SIL.Motif.Commands` has no console or serialization concern.
- `SIL.Motif.Cli` has no domain or persistence decision.
- All product projects target `net10.0`, and Contract remains LibLCM-free.
