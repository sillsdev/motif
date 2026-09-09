# PanGloss invocation module implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for
> tracking.

**Goal:** Realise [ADR 0044](../../adr/0044-every-parser-process-is-one-pangloss-invocation.md): every
`pangloss` process Motif starts goes through one module in `SIL.Motif.Host` that owns queue admission,
containment, a wall-clock cap, and returns an outcome instead of throwing.

**Architecture:** A new `SIL.Motif.Host.PanGloss` namespace receives the machine queue and the Windows job
object from Worker, and gains `PanGlossInvoker`: given a typed request for one of the three subcommands Motif
uses (`batch`, `stats`, `import`), it takes a queue slot, starts the executable inside the job, drains both
streams, enforces the cap, and returns a `PanGlossOutcome`. The six existing launchers collapse into thin
callers of it; `PanGlossAssessmentProcess` (the `assess` route the shipped binary lacks) stays outside, per
ADR 0044 decision 5. `FakePanGloss` gains `batch` and loses `assess`. Commands map outcomes to their
existing Refusal codes.

**Tech Stack:** C# 14, `net10.0`, xUnit, Windows Job Objects, the `FakePanGloss` test executable.

**Standing rules:**

- Run `./test.ps1` in the foreground before every commit. `./build.ps1` runs comment hygiene: implementation
  comments (`//`, and `///` on private members) are one line and at most 110 characters; no plan, ADR-number,
  issue or date references in code; no attribution.
- Never read real project data. Fixtures come from `NewLangProjFixture`, `SeededProject`, and `FakePanGloss`.
- Commit messages: a `type: subject` line, a short body saying why, then the trailer
  `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Source files under `src/` and `tests/` are CRLF. Use the Edit tool, or `perl -0pi` with `\r\n` in patterns,
  never a bare `\n` heredoc into an existing file.
- `MOTIF_PANGLOSS_EXE` set in the environment would redirect the real locator; tests never rely on it.

**Left open, by design:** whether `IAssessor.ProduceAsync` should return a result instead of throwing
`AssessorUnavailableException` (that is ADR 0042's seam, not ADR 0044's), and everything in grill items K46,
K48 and K49.

---

### Task 1: Move admission and containment into Host

**Files:**

- Move: `src/SIL.Motif.Worker/PanGloss/MachinePanGlossQueue.cs` → `src/SIL.Motif.Host/PanGloss/MachinePanGlossQueue.cs`
- Move: `src/SIL.Motif.Worker/PanGloss/MachineSlotLease.cs` → `src/SIL.Motif.Host/PanGloss/MachineSlotLease.cs`
- Move: `src/SIL.Motif.Worker/PanGloss/WindowsCpuJob.cs` → `src/SIL.Motif.Host/PanGloss/WindowsCpuJob.cs`
- Move: `src/SIL.Motif.Worker/PanGloss/WindowsCpuJobGovernor.cs` → `src/SIL.Motif.Host/PanGloss/WindowsCpuJobGovernor.cs`
- Move: `src/SIL.Motif.Worker/WorkerMutexOwner.cs` → `src/SIL.Motif.Host/PanGloss/WorkerMutexOwner.cs`
- Modify: `src/SIL.Motif.Worker/JobRunnerHost.cs`, `src/SIL.Motif.Commands/Assess/AssessCommand.cs`,
  `src/SIL.Motif.Commands/Handoff/HandoffCommand.cs`
- Modify: `tests/SIL.Motif.Tests/Worker/MachinePanGlossQueueTests.cs`, `tests/SIL.Motif.Tests/Worker/WindowsCpuJobTests.cs`,
  `tests/SIL.Motif.Tests/Commands/AssessCommandTests.cs`, `tests/SIL.Motif.Tests/Handoff/HandoffWriterTests.cs`

- [ ] **Step 1: Move the five files with history.**

```bash
mkdir -p src/SIL.Motif.Host/PanGloss
git mv src/SIL.Motif.Worker/PanGloss/MachinePanGlossQueue.cs src/SIL.Motif.Host/PanGloss/
git mv src/SIL.Motif.Worker/PanGloss/MachineSlotLease.cs src/SIL.Motif.Host/PanGloss/
git mv src/SIL.Motif.Worker/PanGloss/WindowsCpuJob.cs src/SIL.Motif.Host/PanGloss/
git mv src/SIL.Motif.Worker/PanGloss/WindowsCpuJobGovernor.cs src/SIL.Motif.Host/PanGloss/
git mv src/SIL.Motif.Worker/WorkerMutexOwner.cs src/SIL.Motif.Host/PanGloss/
```

- [ ] **Step 2: Change the namespaces.** In each moved file replace `namespace SIL.Motif.Worker.PanGloss;` (or
  `namespace SIL.Motif.Worker;` in `WorkerMutexOwner.cs`) with `namespace SIL.Motif.Host.PanGloss;`. In
  `WorkerMutexOwner.cs` change `internal sealed class WorkerMutexOwner` to `public sealed class WorkerMutexOwner`
  (Worker's `JobRunnerHost` still constructs it). In `WindowsCpuJobGovernor.cs` delete the line
  `using SIL.Motif.Host.Parser;` is **kept** (it still implements `IParserProcessGovernor` until Task 5), and
  rewrite its `<remarks>` to one sentence: `/// <remarks>Bridges the job object to the governor seam the launchers still take.</remarks>`
  (the old remarks describe a project layout that no longer holds).

- [ ] **Step 3: Give the queue a test-only observation hook.** In `MachinePanGlossQueue.cs`, after the
  `SlotOwnership` property add:

```csharp
    /// <summary>Observes each job object the moment a job is admitted into it. Set only by tests.</summary>
    internal Action<WindowsCpuJob>? JobAdmitted { get; set; }
```

  and in `RunAdmittedJobAsync` change the signature and body so the hook fires before the work runs:

```csharp
    private async Task RunAdmittedJobAsync(QueuedJob job, MachineSlotLease lease,
        CancellationTokenSource linked)
    {
        try
        {
            using var cpuJob = new WindowsCpuJob();
            JobAdmitted?.Invoke(cpuJob);
            await job.ExecuteAsync(cpuJob, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            lease.Dispose();
            linked.Dispose();
        }
    }
```

  (`RunAdmittedJobAsync` was `static`; it is an instance method now. The call site `_ = RunAdmittedJobAsync(job, lease, linked);` is unchanged.)

- [ ] **Step 4: Repoint every `using`.**

```bash
grep -rl "SIL.Motif.Worker.PanGloss" src tests --include=*.cs | grep -v "/obj/" \
  | xargs perl -pi -e 's/using SIL\.Motif\.Worker\.PanGloss;/using SIL.Motif.Host.PanGloss;/'
perl -pi -e 's/^(using SIL\.Motif\.Worker;\r?)$/$1\nusing SIL.Motif.Host.PanGloss;\r/' src/SIL.Motif.Worker/JobRunnerHost.cs
```

  Then open `src/SIL.Motif.Worker/JobRunnerHost.cs` and confirm the new `using` line ends in CRLF like its
  neighbours (`file` reports only CRLF terminators). If `JobRunnerHost.cs` has no `using SIL.Motif.Worker;`
  line, add `using SIL.Motif.Host.PanGloss;` by hand in its `using` block.

- [ ] **Step 5: Build and run the moved tests.**

Run: `pwsh -NoProfile -File ./build.ps1`
Expected: build succeeds, comment hygiene `TOTAL 0`.

Run: `dotnet test tests/SIL.Motif.Tests --no-build --filter "FullyQualifiedName~MachinePanGlossQueueTests|FullyQualifiedName~WindowsCpuJobTests|FullyQualifiedName~AssessCommandTests|FullyQualifiedName~HandoffWriterTests"`
Expected: all pass.

- [ ] **Step 6: Gate and commit.**

Run: `pwsh -NoProfile -File ./test.ps1`
Expected: `Passed!` with 0 failed.

```bash
git add -A src tests
git commit -F - <<'EOF'
refactor: move parser admission and containment into Host

The queue and the job object are facts about the parser process, and the parser
process is Host's. They sat in Worker because that is where they were first
needed, which left Host able to see containment only through a one-adapter seam.
No dependency direction changes: Worker still references Host.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

### Task 2: FakePanGloss models the binary's real surface

**Files:**

- Modify: `tests/FakePanGloss/Program.cs`
- Modify: `tests/SIL.Motif.Tests/Parser/FakeParserSeamTests.cs`

- [ ] **Step 1: Write the failing test for the new `batch` arm.** Create
  `tests/SIL.Motif.Tests/Parser/FakePanGlossBatchTests.cs`:

```csharp
using System.Diagnostics;
using System.Text.Json;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Parser;

/// <summary>
/// Pins the fake parser's <c>batch</c> arm to the row shape <c>BatchTsvParser</c> reads, so a test that
/// drives Motif through the fake exercises the same TSV contract the real binary honours.
/// </summary>
public sealed class FakePanGlossBatchTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-fake-batch-" + Guid.NewGuid().ToString("N"));

    public FakePanGlossBatchTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }

    [Fact]
    public void Batch_WritesOneTsvRowPerWord_AndACacheWhenAsked()
    {
        var project = Path.Combine(_root, "p.fwdata");
        File.WriteAllText(project, "never read");
        var words = Path.Combine(_root, "words.txt");
        File.WriteAllLines(words, ["motifa", "zzz"]);
        var outPath = Path.Combine(_root, "out.tsv");
        var cache = Path.Combine(_root, "cache.bin");

        var exit = Run("batch", project, words, outPath, "--word-timeout-ms", "1000", "--threads", "1",
            "--stats", "--cache", cache);

        Assert.Equal(0, exit);
        var rows = File.ReadAllText(outPath).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, rows.Length);
        Assert.Equal(["0", "motifa", "3", "ok", "motifa-sig"], rows[0].TrimEnd('\r').Split('\t'));
        Assert.Equal(["1", "zzz", "3", "none", "-"], rows[1].TrimEnd('\r').Split('\t'));
        Assert.True(File.Exists(cache));
        var argv = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(_root, "_pangloss-argv.json")));
        Assert.Equal("batch", argv![0]);
    }

    [Fact]
    public void Assess_IsNoLongerASubcommandTheFakeAnswers()
    {
        var project = Path.Combine(_root, "p.fwdata");
        File.WriteAllText(project, "never read");

        var exit = Run("assess", project, "--report", Path.Combine(_root, "r.json"));

        Assert.Equal(64, exit);
    }

    private static int Run(params string[] args)
    {
        var start = new ProcessStartInfo(FakeParser.ExecutablePath)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var err = process.StandardError.ReadToEndAsync();
        var out_ = process.StandardOutput.ReadToEndAsync();
        process.WaitForExit();
        _ = err.Result; _ = out_.Result;
        return process.ExitCode;
    }
}
```

- [ ] **Step 2: Run red.**

Run: `pwsh -NoProfile -File ./build.ps1; dotnet test tests/SIL.Motif.Tests --no-build --filter "FullyQualifiedName~FakePanGlossBatchTests"`
Expected: both tests FAIL (`batch` exits 64 as unrecognised; `assess` exits 0).

- [ ] **Step 3: Rewrite `tests/FakePanGloss/Program.cs`'s dispatch and arms.** Replace the `Main` switch,
  delete `RunAssess` and `Report`, add `RunBatch` and `BatchTsv`, and trim `Behaviour`. The resulting file's
  behaviour section is:

```csharp
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: pangloss <batch|import|stats> ...");
            return 64;
        }
        return args[0] switch
        {
            "batch" => RunBatch(args),
            "import" => RunImport(args),
            "stats" => RunStats(args),
            _ => Unrecognised(args[0]),
        };
    }

    private static int Unrecognised(string command)
    {
        Console.Error.WriteLine($"usage: pangloss <batch|import|stats> ... (got '{command}')");
        return 64;
    }

    // batch <project> <words.txt> <out.tsv> [--word-timeout-ms N] [--threads N] [--stats] [--cache <path>]
    private static int RunBatch(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine(
                "usage: pangloss batch <grammar> <words.txt> <out.tsv> [--word-timeout-ms N] [--threads N] [--stats] [--cache <path>]");
            return 64;
        }
        var projectPath = args[1];
        var wordsPath = args[2];
        var outPath = args[3];
        string? cachePath = null;
        for (var i = 4; i < args.Length; i++)
        {
            if (args[i] == "--cache" && i + 1 < args.Length) cachePath = args[++i];
        }
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
        RecordArgv(directory, args);
        var behaviour = Behaviour.Read(directory);
        if (behaviour.HeartbeatPath is { } heartbeat) return Tick(heartbeat);
        if (behaviour.DelayMilliseconds > 0)
            Thread.Sleep(behaviour.DelayMilliseconds);
        switch (behaviour.Mode)
        {
            case "noReport":
                // Exits cleanly having written nothing: the caller must not read success from the code alone.
                return behaviour.ExitCode;
            case "fail":
                Console.Error.WriteLine(behaviour.StandardError ?? "the fake parser was told to fail");
                return behaviour.ExitCode == 0 ? 1 : behaviour.ExitCode;
            default:
                var words = File.Exists(wordsPath) ? File.ReadAllLines(wordsPath) : Array.Empty<string>();
                File.WriteAllText(outPath, BatchTsv(behaviour, words));
                // Motif digests the cache and never reads it, so any bytes stand in for PanGloss's SQLite.
                if (cachePath is not null) File.WriteAllText(cachePath, "fake stats cache");
                return behaviour.ExitCode;
        }
    }

    // idx\tword\tms\tstatus\tsignature — the row shape Motif's BatchTsvParser reads.
    private static string BatchTsv(Behaviour behaviour, IReadOnlyList<string> words)
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < words.Count; i++)
        {
            var known = behaviour.Words.FirstOrDefault(w => w.Word == words[i]);
            var status = known is { Outcome: "complete" } ? "ok" : "none";
            var signature = known is null ? "-" : words[i] + "-sig";
            builder.Append(i).Append('\t').Append(words[i]).Append('\t').Append(3).Append('\t')
                .Append(status).Append('\t').Append(signature).Append('\n');
        }
        return builder.ToString();
    }
```

  `RunImport`, `RunStats`, `RecordArgv`, `GrammarJson`, `StatsText`, `StatsJsonl` and `Tick` are unchanged.
  Replace the `FakeWord` and `Behaviour` records with:

```csharp
    private sealed record FakeWord(string Word, string Outcome);

    private sealed record Behaviour
    {
        public string Mode { get; init; } = "succeed";
        public int ExitCode { get; init; }
        public int DelayMilliseconds { get; init; }
        public string? HeartbeatPath { get; init; }
        public string? StandardError { get; init; }
        public string SemanticDigest { get; init; } = "sha256:" + new string('b', 64);
        public string SourceSha256 { get; init; } = "sha256:" + new string('c', 64);
        public string ModelFingerprint { get; init; } = "fp-1";
        public IReadOnlyList<FakeWord> Words { get; init; } = [new FakeWord("motifa", "complete")];

        internal static Behaviour Read(string? directory)
        {
            if (directory is null) return new Behaviour();
            var path = Path.Combine(directory, BehaviourFileName);
            if (!File.Exists(path)) return new Behaviour();
            return JsonSerializer.Deserialize<Behaviour>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Behaviour();
        }
    }
```

  Rewrite the class `<summary>`/`<remarks>` so they no longer describe `assess`: the fake "answers the three
  subcommands Motif sends the shipped binary — `batch`, `import`, `stats` — and nothing else". Remove
  `using System.Globalization;` only if `Tick` no longer needs it (it does: keep it).

- [ ] **Step 4: Retire the tests that depended on the fake's `assess`.** In
  `tests/SIL.Motif.Tests/Parser/FakeParserSeamTests.cs`, delete the test
  `ASuccessfulAssessment_AssignsTheStartedProcessToTheSuppliedGovernor` entirely (its subject disappears in
  Task 5), and on the other eight `[Fact]` attributes write
  `[Fact(Skip = "The shipped pangloss has no assess subcommand (grill K46); the fake no longer pretends otherwise.")]`.
  Change the class `<summary>` to say the class is parked until K46 decides who produces the report.

- [ ] **Step 5: Run green.**

Run: `pwsh -NoProfile -File ./build.ps1; dotnet test tests/SIL.Motif.Tests --no-build --filter "FullyQualifiedName~FakePanGlossBatchTests|FullyQualifiedName~FakeParserSeamTests|FullyQualifiedName~PanGlossCandidateExportTests|FullyQualifiedName~PanGlossAssessmentProcessTests"`
Expected: 2 passed, 8 skipped, the export and assessment-process tests still pass (they refuse before launching).

- [ ] **Step 6: Gate and commit.**

Run: `pwsh -NoProfile -File ./test.ps1`
Expected: `Passed!` with 0 failed. Note the skipped count rose by 8.

```bash
git add -A tests
git commit -F - <<'EOF'
test: FakePanGloss answers batch, import and stats, and no longer assess

The fake implemented a subcommand the shipped binary does not have, so it could
pass tests the binary would fail. It now models the real surface, and gains the
batch arm the invocation module's tests need. The eight tests that drove the
assess route through the fake are skipped, not deleted, until K46 is decided.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

### Task 3: The PanGloss invocation module

**Files:**

- Create: `src/SIL.Motif.Host/PanGloss/PanGlossOutcome.cs`
- Create: `src/SIL.Motif.Host/PanGloss/PanGlossRequest.cs`
- Create: `src/SIL.Motif.Host/PanGloss/IPanGlossInvoker.cs`
- Create: `src/SIL.Motif.Host/PanGloss/PanGlossInvoker.cs`
- Create: `tests/SIL.Motif.Tests/TestFixtures/FakeInvoker.cs`
- Create: `tests/SIL.Motif.Tests/PanGloss/PanGlossInvokerTests.cs`

- [ ] **Step 1: Write the failing tests.** Create `tests/SIL.Motif.Tests/PanGloss/PanGlossInvokerTests.cs`:

```csharp
using System.Text.Json;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>
/// The invocation module against the fake executable: argument shaping per subcommand, both streams
/// drained, admission and containment, the wall-clock cap, cancellation, and every failure as an outcome.
/// </summary>
public sealed class PanGlossInvokerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-invoker-" + Guid.NewGuid().ToString("N"));

    public PanGlossInvokerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }

    [Fact]
    public async Task Stats_ArgvIsStatsGrammarCacheThenForwardedInOrder_AndCompletedCarriesBothStreams()
    {
        var grammar = Project("stats");
        var cache = Path.Combine(_root, "stats.cache");
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Stats(grammar, cache, ["--group", "word", "--format", "jsonl"]),
            "test:stats", CancellationToken.None);

        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        Assert.Contains("\"group\":\"word\"", completed.Output, StringComparison.Ordinal);
        Assert.Equal(["stats", grammar, "--cache", cache, "--group", "word", "--format", "jsonl"], Argv(grammar));
    }

    [Fact]
    public async Task Stats_ANonZeroExitIsRefusedWithItsStandardError()
    {
        var grammar = Project("stats-fail");
        FakeParser.Behave(_root, new { mode = "fail", exitCode = 3, standardError = "stats query exploded" });
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Stats(grammar, Path.Combine(_root, "c"), []), "test:stats", CancellationToken.None);

        var refused = Assert.IsType<PanGlossOutcome.Refused>(outcome);
        Assert.Equal(3, refused.ExitCode);
        Assert.Contains("stats query exploded", refused.StandardError, StringComparison.Ordinal);
        Assert.Contains("pangloss stats exited 3", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALargeStandardErrorDoesNotDeadlock_BecauseBothStreamsAreDrainedBeforeWaiting()
    {
        var grammar = Project("stats-large");
        FakeParser.Behave(_root, new { mode = "fail", exitCode = 1, standardError = new string('x', 1 << 20) });
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Stats(grammar, Path.Combine(_root, "c"), []), "test:stats", CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.IsType<PanGlossOutcome.Refused>(outcome);
    }

    [Fact]
    public async Task Import_ArgvIsImportThenTheTwoPaths_AndTheGrammarFileIsWritten()
    {
        var fwdata = Project("import");
        var grammarJson = Path.Combine(_root, "grammar.json");
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Import(fwdata, grammarJson), "test:import", CancellationToken.None);

        Assert.IsType<PanGlossOutcome.Completed>(outcome);
        Assert.True(File.Exists(grammarJson));
        Assert.Equal(["import", fwdata, grammarJson], Argv(fwdata));
    }

    [Fact]
    public async Task Import_AZeroExitThatWroteNoGrammarIsIncomplete_NotCompleted()
    {
        var fwdata = Project("import-empty");
        FakeParser.Behave(_root, new { mode = "noReport", exitCode = 0 });
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Import(fwdata, Path.Combine(_root, "g.json")), "test:import", CancellationToken.None);

        var incomplete = Assert.IsType<PanGlossOutcome.Incomplete>(outcome);
        Assert.Contains("wrote no grammar", incomplete.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Batch_ArgvCarriesTheWordTimeoutOneThreadAndTheCacheWhenAsked_AndCompletedCarriesTheRows()
    {
        var project = Project("batch");
        var cache = Path.Combine(_root, "batch.cache");
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Batch(project, ["motifa", "zzz"], TimeSpan.FromMilliseconds(1500), cache),
            "test:batch", CancellationToken.None);

        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        var rows = completed.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, rows.Length);
        Assert.StartsWith("0\tmotifa\t", rows[0], StringComparison.Ordinal);
        var argv = Argv(project);
        Assert.Equal("batch", argv[0]);
        Assert.Equal(project, argv[1]);
        Assert.EndsWith("words.txt", argv[2], StringComparison.Ordinal);
        Assert.EndsWith("out.tsv", argv[3], StringComparison.Ordinal);
        Assert.Equal(["--word-timeout-ms", "1500", "--threads", "1", "--stats", "--cache", cache], argv[4..]);
        Assert.True(File.Exists(cache));
    }

    [Fact]
    public async Task Batch_WithoutACacheSendsNoStatsFlags()
    {
        var project = Project("batch-plain");
        using var invoker = Invoker();

        await invoker.RunAsync(
            new PanGlossRequest.Batch(project, ["motifa"], TimeSpan.FromSeconds(1)), "test:batch", CancellationToken.None);

        Assert.Equal(["--word-timeout-ms", "1000", "--threads", "1"], Argv(project)[4..]);
    }

    [Fact]
    public async Task Batch_AZeroExitThatWroteNoCacheIsIncomplete()
    {
        var project = Project("batch-nocache");
        FakeParser.Behave(_root, new { mode = "noReport", exitCode = 0 });
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Batch(project, ["motifa"], TimeSpan.FromSeconds(1), Path.Combine(_root, "never.cache")),
            "test:batch", CancellationToken.None);

        Assert.IsType<PanGlossOutcome.Incomplete>(outcome);
    }

    [Fact]
    public async Task AnAbsentExecutableIsUnavailable_NotAnException()
    {
        using var queue = NewQueue();
        using var invoker = new PanGlossInvoker(executablePath: null, queue);

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Import(Project("absent"), Path.Combine(_root, "g.json")), "test", CancellationToken.None);

        var unavailable = Assert.IsType<PanGlossOutcome.Unavailable>(outcome);
        Assert.Contains("MOTIF_PANGLOSS_EXE", unavailable.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExecutableThatWillNotStartIsUnavailable_NotAnException()
    {
        using var queue = NewQueue();
        using var invoker = new PanGlossInvoker(Path.Combine(_root, "no-such-pangloss.exe"), queue);

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Import(Project("nostart"), Path.Combine(_root, "g.json")), "test", CancellationToken.None);

        Assert.IsType<PanGlossOutcome.Unavailable>(outcome);
    }

    [Fact]
    public async Task TheWallClockCapStopsAHungParser_AndReportsTimedOut()
    {
        var project = Project("hang");
        var heartbeat = Path.Combine(_root, "heartbeat.txt");
        FakeParser.Behave(_root, new { heartbeatPath = heartbeat });
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Import(project, Path.Combine(_root, "g.json")), "test:hang", CancellationToken.None,
            wallClockCap: TimeSpan.FromMilliseconds(400));

        var timedOut = Assert.IsType<PanGlossOutcome.TimedOut>(outcome);
        Assert.Equal(TimeSpan.FromMilliseconds(400), timedOut.Cap);
        await AssertStoppedTicking(heartbeat);
    }

    [Fact]
    public async Task CancellationStopsTheParser_AndReportsCancelled()
    {
        var project = Project("cancel");
        var heartbeat = Path.Combine(_root, "heartbeat.txt");
        FakeParser.Behave(_root, new { heartbeatPath = heartbeat });
        using var invoker = Invoker();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Import(project, Path.Combine(_root, "g.json")), "test:cancel", cts.Token);

        Assert.IsType<PanGlossOutcome.Cancelled>(outcome);
        await AssertStoppedTicking(heartbeat);
    }

    [Fact]
    public async Task EveryLaunchRunsInsideTheAdmittedJobObject()
    {
        var project = Project("contained");
        var heartbeat = Path.Combine(_root, "heartbeat.txt");
        FakeParser.Behave(_root, new { heartbeatPath = heartbeat });
        using var queue = NewQueue();
        WindowsCpuJob? admitted = null;
        queue.JobAdmitted = job => admitted = job;
        using var invoker = new PanGlossInvoker(FakeParser.ExecutablePath, queue);
        using var cts = new CancellationTokenSource();

        var run = invoker.RunAsync(
            new PanGlossRequest.Import(project, Path.Combine(_root, "g.json")), "test:contained", cts.Token);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && (admitted is null || admitted.QueryTotalProcessCount() == 0))
            await Task.Delay(20);

        Assert.NotNull(admitted);
        Assert.Equal(1u, admitted!.QueryTotalProcessCount());
        cts.Cancel();
        Assert.IsType<PanGlossOutcome.Cancelled>(await run);
    }

    private PanGlossInvoker Invoker() => new(FakeParser.ExecutablePath, NewQueue());

    private static MachinePanGlossQueue NewQueue() => new(new[]
    {
        "Local\\MotifInvokerTests-" + Guid.NewGuid().ToString("N") + "-0",
        "Local\\MotifInvokerTests-" + Guid.NewGuid().ToString("N") + "-1",
    });

    private string Project(string name)
    {
        var path = Path.Combine(_root, name + ".fwdata");
        File.WriteAllText(path, "the fake parser never reads this.");
        return path;
    }

    private static string[] Argv(string besidePath) =>
        JsonSerializer.Deserialize<string[]>(
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(besidePath)!, "_pangloss-argv.json")))!;

    private static async Task AssertStoppedTicking(string heartbeat)
    {
        Assert.True(File.Exists(heartbeat), "The fake parser never started ticking.");
        await Task.Delay(200);
        var before = File.ReadAllText(heartbeat);
        await Task.Delay(250);
        Assert.Equal(before, File.ReadAllText(heartbeat));
    }
}
```

  Note: the invoker owns the queue it is given and disposes it, so tests that build a queue only for a
  hook still `using` both; disposing twice is safe (`MachinePanGlossQueue.Dispose` returns when already disposed).

- [ ] **Step 2: Run red.**

Run: `pwsh -NoProfile -File ./build.ps1`
Expected: FAIL to compile — `PanGlossInvoker`, `PanGlossRequest`, `PanGlossOutcome` do not exist.

- [ ] **Step 3: Write the outcome type.** Create `src/SIL.Motif.Host/PanGloss/PanGlossOutcome.cs`:

```csharp
namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// What one PanGloss invocation came to. Exactly one case, never an exception: a caller pattern-matches
/// and maps the failure cases to its own Refusal codes.
/// </summary>
public abstract record PanGlossOutcome
{
    private PanGlossOutcome() { }

    /// <summary>One human sentence saying what happened, suitable for a Refusal's message.</summary>
    public abstract string Message { get; }

    /// <summary>The parser exited zero and wrote what the request promised.</summary>
    /// <param name="Output">What the subcommand produced: the JSONL or text rows of <c>stats</c>, the TSV rows of
    /// <c>batch</c>, nothing for <c>import</c>.</param>
    /// <param name="StandardError">Everything the parser wrote to its error stream, kept for its warnings.</param>
    /// <param name="Elapsed">Wall-clock time from process start to exit.</param>
    public sealed record Completed(string Output, string StandardError, TimeSpan Elapsed) : PanGlossOutcome
    {
        public override string Message => "The parser completed.";
    }

    /// <summary>The parser exited nonzero. Its own words are in <see cref="StandardError"/>.</summary>
    public sealed record Refused(int ExitCode, string StandardError, string StandardOutput, string Detail)
        : PanGlossOutcome
    {
        public override string Message => Detail;
    }

    /// <summary>The parser exited zero but did not write what the request promised — never read as success.</summary>
    public sealed record Incomplete(string Detail, string StandardError) : PanGlossOutcome
    {
        public override string Message => Detail;
    }

    /// <summary>No parser ran: the executable is absent or would not start.</summary>
    public sealed record Unavailable(string Detail) : PanGlossOutcome
    {
        public override string Message => Detail;
    }

    /// <summary>The wall-clock cap expired and the process tree was killed.</summary>
    public sealed record TimedOut(TimeSpan Cap, string Detail) : PanGlossOutcome
    {
        public override string Message => Detail;
    }

    /// <summary>The caller cancelled; if a process had started, its tree was killed.</summary>
    public sealed record Cancelled : PanGlossOutcome
    {
        public override string Message => "The parser run was cancelled.";
    }
}
```

- [ ] **Step 4: Write the request types.** Create `src/SIL.Motif.Host/PanGloss/PanGlossRequest.cs`:

```csharp
using System.Diagnostics;
using System.Globalization;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// One typed request for one subcommand the shipped <c>pangloss</c> binary has. This file is the only place
/// Motif writes down the binary's command-line surface; the <c>assess</c> subcommand is absent because the
/// binary has none.
/// </summary>
public abstract record PanGlossRequest
{
    private PanGlossRequest() { }

    /// <summary>The subcommand this request runs, as the binary spells it.</summary>
    public abstract string Subcommand { get; }

    /// <summary>Throws for a request a caller has built wrongly; this is programmer error, not an outcome.</summary>
    internal abstract void Validate();

    /// <summary>Writes whatever the subcommand must read from disk into the invocation's scratch directory.</summary>
    internal virtual Task PrepareAsync(string scratch, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Appends the subcommand and its arguments, one item each so paths need no quoting.</summary>
    internal abstract void AddArguments(ProcessStartInfo startInfo, string scratch);

    /// <summary>Turns a zero exit into the request's output, or reports what the parser promised and did not write.</summary>
    internal abstract PanGlossOutcome Finish(string scratch, string standardOutput, string standardError, TimeSpan elapsed);

    /// <summary>
    /// <c>pangloss batch</c> over a word list, one thread, a per-word limit, and optionally the per-object
    /// statistics cache. One thread because the parser's own hazards guidance says fan-out multiplies memory
    /// on deep-truncation grammars, and the queue already serialises parsers machine-wide.
    /// </summary>
    public sealed record Batch(
        string ProjectFilePath, IReadOnlyList<string> Words, TimeSpan PerWordLimit, string? StatsCachePath = null)
        : PanGlossRequest
    {
        public override string Subcommand => "batch";

        internal override void Validate()
        {
            if (string.IsNullOrWhiteSpace(ProjectFilePath)) throw new ArgumentException("Required.", nameof(ProjectFilePath));
            ArgumentNullException.ThrowIfNull(Words);
            if (PerWordLimit <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(PerWordLimit), "A per-word limit must be positive.");
            if (!File.Exists(ProjectFilePath))
                throw new FileNotFoundException("The project file the parser must read does not exist.", ProjectFilePath);
        }

        internal override Task PrepareAsync(string scratch, CancellationToken cancellationToken) =>
            File.WriteAllLinesAsync(Path.Combine(scratch, "words.txt"), Words, cancellationToken);

        internal override void AddArguments(ProcessStartInfo startInfo, string scratch)
        {
            startInfo.ArgumentList.Add("batch");
            startInfo.ArgumentList.Add(ProjectFilePath);
            startInfo.ArgumentList.Add(Path.Combine(scratch, "words.txt"));
            startInfo.ArgumentList.Add(Path.Combine(scratch, "out.tsv"));
            startInfo.ArgumentList.Add("--word-timeout-ms");
            startInfo.ArgumentList.Add(((int)PerWordLimit.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--threads");
            startInfo.ArgumentList.Add("1");
            if (StatsCachePath is null) return;
            startInfo.ArgumentList.Add("--stats");
            startInfo.ArgumentList.Add("--cache");
            startInfo.ArgumentList.Add(StatsCachePath);
        }

        internal override PanGlossOutcome Finish(string scratch, string standardOutput, string standardError, TimeSpan elapsed)
        {
            if (StatsCachePath is not null && !File.Exists(StatsCachePath))
            {
                return new PanGlossOutcome.Incomplete(
                    $"pangloss batch --stats exited 0 but wrote no cache to '{StatsCachePath}'.", standardError);
            }
            var outPath = Path.Combine(scratch, "out.tsv");
            var tsv = File.Exists(outPath) ? File.ReadAllText(outPath) : string.Empty;
            return new PanGlossOutcome.Completed(tsv, standardError, elapsed);
        }
    }

    /// <summary>
    /// <c>pangloss stats</c> over a cache the <see cref="Batch"/> pass wrote. Motif contributes the grammar and
    /// cache paths; everything else is forwarded in order and unchanged, because PanGloss owns that vocabulary.
    /// </summary>
    public sealed record Stats(string GrammarPath, string CachePath, IReadOnlyList<string> ForwardedArguments)
        : PanGlossRequest
    {
        public override string Subcommand => "stats";

        internal override void Validate()
        {
            if (string.IsNullOrWhiteSpace(GrammarPath)) throw new ArgumentException("Required.", nameof(GrammarPath));
            if (string.IsNullOrWhiteSpace(CachePath)) throw new ArgumentException("Required.", nameof(CachePath));
            ArgumentNullException.ThrowIfNull(ForwardedArguments);
        }

        internal override void AddArguments(ProcessStartInfo startInfo, string scratch)
        {
            startInfo.ArgumentList.Add("stats");
            startInfo.ArgumentList.Add(GrammarPath);
            startInfo.ArgumentList.Add("--cache");
            startInfo.ArgumentList.Add(CachePath);
            foreach (var argument in ForwardedArguments) startInfo.ArgumentList.Add(argument);
        }

        internal override PanGlossOutcome Finish(string scratch, string standardOutput, string standardError, TimeSpan elapsed) =>
            new PanGlossOutcome.Completed(standardOutput, standardError, elapsed);
    }

    /// <summary><c>pangloss import</c>: the grammar snapshot of a saved <c>.fwdata</c>, written where the caller says.</summary>
    public sealed record Import(string FwDataPath, string GrammarJsonPath) : PanGlossRequest
    {
        public override string Subcommand => "import";

        internal override void Validate()
        {
            if (string.IsNullOrWhiteSpace(FwDataPath)) throw new ArgumentException("Required.", nameof(FwDataPath));
            if (string.IsNullOrWhiteSpace(GrammarJsonPath)) throw new ArgumentException("Required.", nameof(GrammarJsonPath));
            if (!File.Exists(FwDataPath))
                throw new FileNotFoundException("The project file the parser must read does not exist.", FwDataPath);
        }

        internal override void AddArguments(ProcessStartInfo startInfo, string scratch)
        {
            startInfo.ArgumentList.Add("import");
            startInfo.ArgumentList.Add(FwDataPath);
            startInfo.ArgumentList.Add(GrammarJsonPath);
        }

        internal override PanGlossOutcome Finish(string scratch, string standardOutput, string standardError, TimeSpan elapsed) =>
            File.Exists(GrammarJsonPath)
                ? new PanGlossOutcome.Completed(string.Empty, standardError, elapsed)
                : new PanGlossOutcome.Incomplete(
                    $"pangloss import exited 0 but wrote no grammar to '{GrammarJsonPath}'.", standardError);
    }
}
```

- [ ] **Step 5: Write the interface.** Create `src/SIL.Motif.Host/PanGloss/IPanGlossInvoker.cs`:

```csharp
namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// The one way a <c>pangloss</c> process is started. The interface exists so a command or Assessor can be
/// exercised without a process; <see cref="PanGlossInvoker"/> is the implementation that really launches one.
/// </summary>
public interface IPanGlossInvoker
{
    /// <summary>
    /// Runs one request after machine-queue admission, inside the machine's job object, under
    /// <paramref name="wallClockCap"/> (the invoker's default when null). Never throws for anything the
    /// parser did; <paramref name="label"/> names the work in the queue's diagnostics.
    /// </summary>
    Task<PanGlossOutcome> RunAsync(
        PanGlossRequest request, string label, CancellationToken cancellationToken, TimeSpan? wallClockCap = null);
}
```

- [ ] **Step 6: Write the invoker.** Create `src/SIL.Motif.Host/PanGloss/PanGlossInvoker.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Runs the <c>pangloss</c> executable: one queue slot, one job object, both streams drained, one wall-clock
/// cap, and an outcome for whatever happened.
/// </summary>
/// <remarks>
/// <para>
/// Six launchers used to own copies of this sequence, and the containment they were each meant to call was
/// wired at none of them. Here a caller cannot obtain a process at all, only an outcome, so admission and
/// containment cannot be skipped and a failure cannot escape as an exception.
/// </para>
/// <para>
/// The default cap is the parser's own ratified execution limit rather than a number Motif chose. The
/// per-word limit is a <see cref="PanGlossRequest.Batch"/> argument and bounds a word, not the process.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class PanGlossInvoker : IPanGlossInvoker, IDisposable
{
    /// <summary>The parser's ratified execution limit, applied to every invocation that names no cap.</summary>
    public static readonly TimeSpan DefaultWallClockCap = TimeSpan.FromMinutes(10);

    private readonly string? _executable;
    private readonly MachinePanGlossQueue _queue;

    /// <summary>Locates the executable and competes for the machine's real parser slots.</summary>
    public PanGlossInvoker() : this(PanGlossExecutable.TryLocate(), new MachinePanGlossQueue())
    {
    }

    /// <summary>An explicit executable (or none) and an explicit queue, so a test can isolate both.</summary>
    internal PanGlossInvoker(string? executablePath, MachinePanGlossQueue queue)
    {
        _executable = executablePath;
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    /// <inheritdoc />
    public async Task<PanGlossOutcome> RunAsync(
        PanGlossRequest request, string label, CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Required.", nameof(label));
        var cap = wallClockCap ?? DefaultWallClockCap;
        if (cap <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(wallClockCap), "A cap must be positive.");
        request.Validate();

        if (_executable is null) return new PanGlossOutcome.Unavailable(MissingExecutableMessage);

        try
        {
            return await _queue.RunAsync(label,
                (cpuJob, token) => LaunchAsync(_executable, request, cpuJob.AssignProcess, cap, token),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new PanGlossOutcome.Cancelled();
        }
    }

    public void Dispose() => _queue.Dispose();

    /// <summary>
    /// The launch itself, after admission: <paramref name="contain"/> receives the process the instant it
    /// starts, before either side knows whether the run will succeed.
    /// </summary>
    internal static async Task<PanGlossOutcome> LaunchAsync(
        string executable, PanGlossRequest request, Action<Process> contain, TimeSpan cap,
        CancellationToken cancellationToken)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "SIL.Motif.PanGloss", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            await request.PrepareAsync(scratch, cancellationToken).ConfigureAwait(false);

            var startInfo = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            request.AddArguments(startInfo, scratch);

            Process? process;
            try
            {
                process = Process.Start(startInfo);
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                return new PanGlossOutcome.Unavailable($"Could not start '{executable}': {exception.Message}");
            }
            if (process is null) return new PanGlossOutcome.Unavailable($"Could not start '{executable}'.");

            using (process)
            {
                contain(process);
                var clock = Stopwatch.StartNew();

                // Read both streams before waiting: a full pipe buffer deadlocks a process that is still writing.
                var stdErrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
                var stdOutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);

                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(cap);
                try
                {
                    await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                    return cancellationToken.IsCancellationRequested
                        ? new PanGlossOutcome.Cancelled()
                        : new PanGlossOutcome.TimedOut(cap,
                            $"pangloss {request.Subcommand} did not finish within {cap.TotalMinutes:0.#} minutes and was stopped.");
                }

                var standardError = await stdErrTask.ConfigureAwait(false);
                var standardOutput = await stdOutTask.ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    return new PanGlossOutcome.Refused(process.ExitCode, standardError, standardOutput,
                        $"pangloss {request.Subcommand} exited {process.ExitCode}:" + Environment.NewLine + standardError.Trim());
                }
                return request.Finish(scratch, standardOutput, standardError, clock.Elapsed);
            }
        }
        finally
        {
            // Best effort: a leaked scratch directory must not turn a completed run into a failure.
            try { Directory.Delete(scratch, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string MissingExecutableMessage =>
        "Could not find the pangloss executable. Build it with `cargo build --release -p pg-cli` in the " +
        $"PanGloss checkout, or set {PanGlossExecutable.PathVariable} to its path.";
}
```

- [ ] **Step 7: Write the in-process fake for callers' tests.** Create `tests/SIL.Motif.Tests/TestFixtures/FakeInvoker.cs`:

```csharp
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Stands in for the invocation module without a process: records every request and answers with whatever
/// <see cref="Respond"/> says, completing by default.
/// </summary>
internal sealed class FakeInvoker : IPanGlossInvoker
{
    public List<(PanGlossRequest Request, string Label)> Requests { get; } = new();

    /// <summary>What to answer; may write files a request promises (a batch cache, an import's grammar).</summary>
    public Func<PanGlossRequest, PanGlossOutcome> Respond { get; set; } =
        _ => new PanGlossOutcome.Completed(string.Empty, string.Empty, TimeSpan.Zero);

    public Task<PanGlossOutcome> RunAsync(
        PanGlossRequest request, string label, CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
    {
        Requests.Add((request, label));
        if (cancellationToken.IsCancellationRequested) return Task.FromResult<PanGlossOutcome>(new PanGlossOutcome.Cancelled());
        return Task.FromResult(Respond(request));
    }
}
```

- [ ] **Step 8: Run green.**

Run: `pwsh -NoProfile -File ./build.ps1; dotnet test tests/SIL.Motif.Tests --no-build --filter "FullyQualifiedName~PanGlossInvokerTests"`
Expected: 13 passed.

- [ ] **Step 9: Gate and commit.**

Run: `pwsh -NoProfile -File ./test.ps1`
Expected: `Passed!` with 0 failed.

```bash
git add -A src tests
git commit -F - <<'EOF'
feat: the PanGloss invocation module

One way to run the parser: a typed request for batch, stats or import; queue
admission; the job object; both streams drained; a wall-clock cap at the
parser's own ratified limit; --threads 1 on batch; and an outcome instead of an
exception for everything the parser did or failed to do. Nothing calls it yet.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

### Task 4: The batch pass and the Assessor cross the module

**Files:**

- Create: `src/SIL.Motif.Host/Parser/ParserUnavailableException.cs`
- Rewrite: `src/SIL.Motif.Host/Parser/PanGlossParser.cs`
- Modify: `src/SIL.Motif.Host/Assess/PanGlossAssessor.cs` (delete `IPanGlossStatsRunner` and `PanGlossStatsProcess`)
- Modify: `src/SIL.Motif.Host/Assess/IAssessor.cs`
- Modify: `src/SIL.Motif.Host/Parser/IPanGlossAssessor.cs`, `src/SIL.Motif.Host/Parser/PanGlossAssessmentProcess.cs`
- Modify: `src/SIL.Motif.Commands/Assess/AssessCommand.cs:157-158` (drop the governor argument only)
- Modify: `src/SIL.Motif.Worker/Program.cs:519-531`
- Modify: `tests/SIL.Motif.Tests/TestFixtures/FakeAssessor.cs`, `tests/SIL.Motif.Tests/Assess/PanGlossAssessorTests.cs`,
  `tests/SIL.Motif.Tests/Parser/GrammarCoverageFigureIntegrationTests.cs`, `tests/SIL.Motif.Tests/Parser/ParserSeamIntegrationTests.cs`
- Delete: `tests/SIL.Motif.Tests/Parser/PanGlossGovernorTests.cs`

- [ ] **Step 1: Write the failing test for the thin parser.** Create `tests/SIL.Motif.Tests/Parser/PanGlossParserTests.cs`:

```csharp
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Parser;

/// <summary>The batch pass is now a reading of one invocation's outcome; nothing here starts a process.</summary>
public sealed class PanGlossParserTests
{
    private static readonly string Project = Path.GetTempFileName();

    [Fact]
    public async Task ACompletedBatchBecomesAnAnalysis_WithWarningsFromStandardError()
    {
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(
                "0\tmotifa\t12\tok\tsig\n1\tzzz\t7\tnone\t-\n", "warning: one thing\nnoise\n", TimeSpan.FromSeconds(1)),
        };
        var parser = new PanGlossParser(invoker);

        var result = await parser.AnalyseBatchAsync(Project, ["motifa", "zzz"], ParserEngine.FstPrunedByHermitCrab,
            TimeSpan.FromMilliseconds(1500), "test", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Analysis!.Words.Count);
        Assert.Equal(1500, result.Analysis.PerWordTimeoutMs);
        Assert.Equal(["warning: one thing"], result.Analysis.Warnings);
        var request = Assert.IsType<PanGlossRequest.Batch>(Assert.Single(invoker.Requests).Request);
        Assert.Null(request.StatsCachePath);
    }

    [Fact]
    public async Task ARefusedRunWithARecognisedFstRefusal_ReturnsTheRefusal()
    {
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Refused(1,
                "error: capability: the FST cannot represent this grammar", string.Empty, "pangloss batch exited 1"),
        };

        var result = await new PanGlossParser(invoker).AnalyseBatchAsync(Project, ["x"],
            ParserEngine.FstPrunedByHermitCrab, TimeSpan.FromSeconds(1), "test", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Refusal);
    }

    [Fact]
    public async Task AnyOtherOutcome_IsReturnedAsIs_NeverThrown()
    {
        var invoker = new FakeInvoker { Respond = _ => new PanGlossOutcome.Unavailable("no parser") };

        var result = await new PanGlossParser(invoker).AnalyseBatchAsync(Project, ["x"],
            ParserEngine.FstPrunedByHermitCrab, TimeSpan.FromSeconds(1), "test", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Refusal);
        Assert.IsType<PanGlossOutcome.Unavailable>(result.Outcome);
    }
}
```

  If `ParserRefusalRecognizer.Recognize` does not recognise the sample stderr line above, open
  `src/SIL.Motif.Host/Parser/ParserRefusal.cs`, read what it recognises, and use a line it does. Do not change
  the recogniser.

- [ ] **Step 2: Run red.**

Run: `pwsh -NoProfile -File ./build.ps1`
Expected: FAIL to compile — `PanGlossParser` has no constructor taking an invoker and no `AnalyseBatchAsync`.

- [ ] **Step 3: Move the exception to its own file.** Create `src/SIL.Motif.Host/Parser/ParserUnavailableException.cs`:

```csharp
namespace SIL.Motif.Host.Parser;

/// <summary>
/// Raised by the assess route, which the shipped binary does not have, when the parser could not be run at
/// all. Every other launch reports the same condition as a <see cref="PanGloss.PanGlossOutcome.Unavailable"/>.
/// </summary>
public sealed class ParserUnavailableException : Exception
{
    public ParserUnavailableException(string message) : base(message) { }
}
```

- [ ] **Step 4: Rewrite `src/SIL.Motif.Host/Parser/PanGlossParser.cs`** in full:

```csharp
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Host.Parser;

/// <summary>The outcome of asking the parser to analyse a batch.</summary>
/// <param name="Analysis">The results, when the run completed. <c>null</c> otherwise.</param>
/// <param name="Refusal">Why the FST path declined, when the parser's exit was a recognised refusal.</param>
/// <param name="Outcome">The invocation's own outcome, which explains every case where <paramref name="Analysis"/> is null.</param>
public sealed record ParserRunResult(BatchAnalysis? Analysis, ParserRefusal? Refusal, PanGlossOutcome Outcome)
{
    public bool Succeeded => Analysis is not null;
}

/// <summary>
/// Reads one <c>pangloss batch</c> invocation back as typed analyses.
/// </summary>
/// <remarks>
/// <para>
/// <b>The project file is the input, and that is the whole point.</b> PanGloss reads <c>.fwdata</c> directly,
/// which needs no FieldWorks assemblies, and answers in FieldWorks GUIDs where the HermitCrab-XML route
/// answers in synthetic keys that cannot be tied back to the entry or rule a Proposal edited.
/// </para>
/// <para>
/// <b>Motif must save before calling this.</b> The parser reads the file, so anything uncommitted in an open
/// cache is invisible to it, the same precondition as the Dry Run's scratch copy.
/// <see cref="FwDataProjectLoader.Save"/> waits for the write to reach disk.
/// </para>
/// </remarks>
public sealed class PanGlossParser
{
    private readonly IPanGlossInvoker _invoker;

    public PanGlossParser(IPanGlossInvoker invoker) =>
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));

    /// <summary>Analyses <paramref name="words"/> against the grammar in <paramref name="projectFilePath"/>.</summary>
    /// <param name="perWordLimit">
    /// The per-word deadline. Words that hit it come back as <see cref="WordOutcome.TimedOut"/> and are never
    /// counted as analysis failures; see <see cref="BatchAnalysis.IsLowerBound"/>. Required: without one, a
    /// single hard word can take the process down.
    /// </param>
    /// <param name="label">Names this run in the machine queue's diagnostics.</param>
    public async Task<ParserRunResult> AnalyseBatchAsync(
        string projectFilePath, IReadOnlyList<string> words, ParserEngine engine, TimeSpan perWordLimit,
        string label, CancellationToken cancellationToken)
    {
        var outcome = await _invoker.RunAsync(
            new PanGlossRequest.Batch(projectFilePath, words, perWordLimit), label, cancellationToken)
            .ConfigureAwait(false);

        switch (outcome)
        {
            case PanGlossOutcome.Completed completed:
                var analysis = new BatchAnalysis(
                    Words: BatchTsvParser.Parse(completed.Output),
                    Engine: engine,
                    PerWordTimeoutMs: (int)perWordLimit.TotalMilliseconds,
                    ProjectPath: projectFilePath,
                    Warnings: ExtractWarnings(completed.StandardError));
                return new ParserRunResult(analysis, null, outcome);
            case PanGlossOutcome.Refused refused when ParserRefusalRecognizer.Recognize(refused.StandardError) is { } refusal:
                return new ParserRunResult(null, refusal, outcome);
            default:
                return new ParserRunResult(null, null, outcome);
        }
    }

    /// <summary>Keeps the parser's warnings, ignored elsewhere they'd taint the coverage figure.</summary>
    private static IReadOnlyList<string> ExtractWarnings(string stdErr) =>
        stdErr.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("warning:", StringComparison.OrdinalIgnoreCase)
                        || l.StartsWith("capability:", StringComparison.OrdinalIgnoreCase))
            .ToList();
}
```

- [ ] **Step 5: The Assessor seam loses its governor and gains a typed unavailability.** In
  `src/SIL.Motif.Host/Assess/IAssessor.cs`, change `ProduceAsync` to:

```csharp
    Task<IReadOnlyList<ProducedAssessment>> ProduceAsync(
        AssessmentScope scope, string exportedCandidate, CancellationToken cancellationToken);
```

  remove `using SIL.Motif.Host.Parser;` if nothing else in the file needs it, and add after `AssessorRefusalException`:

```csharp
/// <summary>
/// Raised when an Assessor could not measure at all — its parser was absent, would not start, timed out, or
/// exited refusing — as distinct from a kind it declines to produce.
/// </summary>
public sealed class AssessorUnavailableException : Exception
{
    public AssessorUnavailableException(string assessor, string reason)
        : base($"'{assessor}' could not run: {reason}")
    {
        Assessor = assessor;
    }

    public string Assessor { get; }
}
```

  In `src/SIL.Motif.Host/Parser/IPanGlossAssessor.cs` remove the `IParserProcessGovernor? governor = null`
  parameter from `RunAsync` and the sentence about it in the doc comment. In
  `src/SIL.Motif.Host/Parser/PanGlossAssessmentProcess.cs` remove the same parameter from `RunAsync` and from
  `RunProcessAsync`, and delete the line `governor?.Contain(process);`.

- [ ] **Step 6: Rewrite `PanGlossAssessor`.** In `src/SIL.Motif.Host/Assess/PanGlossAssessor.cs` delete
  `IPanGlossStatsRunner` and `PanGlossStatsProcess` entirely (with their doc comments), keep
  `IAssessorCachePathResolver`, and replace the `PanGlossAssessor` class with:

```csharp
/// <summary>
/// PanGloss as an <see cref="IAssessor"/>: the first Assessor, and proof the seam needs no PanGloss-specific
/// caller.
/// </summary>
/// <remarks>
/// <para>
/// Composes two seams: <see cref="IPanGlossAssessor"/> for <see cref="AssessmentKind.Correctness"/> (GUID-keyed
/// analyses against manual analysis) and the <see cref="IPanGlossInvoker"/> for both
/// <see cref="AssessmentKind.ParseTime"/> (a plain batch, read back through <see cref="PanGlossParser"/>) and
/// <see cref="AssessmentKind.ObjectTiming"/> (the same batch with <c>--stats --cache</c>, whose cache PanGloss
/// owns the format of and Motif only ever digests).
/// </para>
/// <para>
/// <see cref="AssessmentKind.EngineSize"/> is never declared: PanGloss emits build time and engine size on
/// stderr, and scraping stderr for them was rejected rather than adopted. <see cref="AssessmentKind.Difference"/>
/// and <see cref="AssessmentKind.Completion"/> are never declared either: both compare two Assessments, which
/// is the comparison mechanism's job.
/// </para>
/// <para>
/// <see cref="ProduceAsync"/> always runs the GUID-keyed assess pass first, whatever was asked for: it is the
/// only route that carries the grammar's own hash and the rest of the report header, which every produced
/// kind must cite and which this type never derives on its own.
/// </para>
/// </remarks>
public sealed class PanGlossAssessor : IAssessor
{
    /// <summary>The name this Assessor is registered and cited under.</summary>
    public const string AssessorName = "pangloss";

    private static readonly IReadOnlyList<AssessmentKind> Supported =
        [AssessmentKind.ParseTime, AssessmentKind.Correctness, AssessmentKind.ObjectTiming];
    private static readonly IReadOnlyList<AssessmentKind> DefaultCollected =
        [AssessmentKind.ParseTime, AssessmentKind.Correctness];

    private readonly IAssessorCachePathResolver _cachePaths;
    private readonly IPanGlossInvoker _invoker;
    private readonly PanGlossParser _parser;
    private readonly IPanGlossAssessor _reportRunner;

    /// <param name="cachePaths">Resolves where this Assessor's stats cache lives for a grammar and engine.</param>
    /// <param name="invoker">Runs every parser process this Assessor needs.</param>
    /// <param name="reportRunner">Runs the GUID-keyed assess pass; defaults to a real one.</param>
    public PanGlossAssessor(
        IAssessorCachePathResolver cachePaths, IPanGlossInvoker invoker, IPanGlossAssessor? reportRunner = null)
    {
        _cachePaths = cachePaths ?? throw new ArgumentNullException(nameof(cachePaths));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _parser = new PanGlossParser(invoker);
        _reportRunner = reportRunner ?? new PanGlossAssessmentProcess();
    }

    /// <inheritdoc />
    public string Name => AssessorName;

    /// <inheritdoc />
    public IReadOnlyList<AssessmentKind> SupportedKinds => Supported;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProducedAssessment>> ProduceAsync(
        AssessmentScope scope, string exportedCandidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (string.IsNullOrWhiteSpace(exportedCandidate))
            throw new ArgumentException("Required.", nameof(exportedCandidate));

        var wanted = scope.Collect.Count == 0 ? DefaultCollected : scope.Collect;
        foreach (var kind in wanted)
        {
            if (!Supported.Contains(kind))
                throw new AssessorRefusalException(AssessorName, kind, ReasonNotProduced(kind));
        }
        if (!PanGlossEngineNames.TryParse(scope.Engine, out var engine))
        {
            throw new ArgumentException(
                $"'{scope.Engine}' does not name an engine {AssessorName} recognizes.", nameof(scope));
        }

        var grammarSourcePath = LocateGrammarSource(exportedCandidate);

        AssessReport report;
        try
        {
            // Every produced kind cites this hash, and only the assess pass carries it — never derived here.
            report = await _reportRunner.RunAsync(exportedCandidate, cancellationToken).ConfigureAwait(false);
        }
        catch (ParserUnavailableException exception)
        {
            throw new AssessorUnavailableException(AssessorName, exception.Message);
        }

        var results = new List<ProducedAssessment>();
        if (wanted.Contains(AssessmentKind.Correctness))
        {
            results.Add(Produced(report, AssessmentKind.Correctness,
                new AssessmentRaw.WordMeasurements(report.Words)));
        }
        if (wanted.Contains(AssessmentKind.ParseTime))
        {
            var runResult = await _parser.AnalyseBatchAsync(
                grammarSourcePath, scope.Words, engine, scope.PerWordLimit, "assess:parse-time", cancellationToken)
                .ConfigureAwait(false);
            if (!runResult.Succeeded)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (runResult.Refusal is { } refusal)
                    throw new InvalidOperationException($"{AssessorName} could not measure parse time: {refusal.Detail}");
                throw new AssessorUnavailableException(AssessorName, runResult.Outcome.Message);
            }
            results.Add(Produced(report, AssessmentKind.ParseTime, new AssessmentRaw.Batch(runResult.Analysis!)));
        }
        if (wanted.Contains(AssessmentKind.ObjectTiming))
        {
            var cachePath = _cachePaths.PathFor(report.GrammarSourceSha256, AssessorName, scope.Engine);
            var outcome = await _invoker.RunAsync(
                new PanGlossRequest.Batch(grammarSourcePath, scope.Words, scope.PerWordLimit, cachePath),
                "assess:object-timing", cancellationToken).ConfigureAwait(false);
            if (outcome is not PanGlossOutcome.Completed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new AssessorUnavailableException(AssessorName, outcome.Message);
            }
            results.Add(Produced(report, AssessmentKind.ObjectTiming,
                new AssessmentRaw.FileCache(cachePath, DigestOfFile(cachePath))));
        }
        return results;
    }
```

  Keep `Produced`, `ReasonNotProduced`, `LocateGrammarSource` and `DigestOfFile` exactly as they are. Add
  `using SIL.Motif.Host.PanGloss;` at the top and delete `using System.Diagnostics;` and
  `using System.Globalization;` if nothing else in the file uses them.

- [ ] **Step 7: Minimal compile fixes in the callers.** In `src/SIL.Motif.Commands/Assess/AssessCommand.cs`
  change the `ProduceAsync` call to
  `(cpuJob, jobToken) => assessor.ProduceAsync(scope, exportedCandidate, jobToken),` (the `WindowsCpuJobGovernor`
  for the stats summary remains until Task 5), and add a `catch (AssessorUnavailableException ex)` arm beside the
  existing `catch (ParserUnavailableException ex)` that returns the same `ParserUnavailable(request.ProjectPath, ex)`
  refusal — change `ParserUnavailable`'s parameter type to `Exception`. Change both construction sites
  `new PanGlossAssessor(new StatsCacheStore(ownership))` in `AssessCommand.cs` and
  `src/SIL.Motif.Commands/Handoff/HandoffCommand.cs` to `new PanGlossAssessor(new StatsCacheStore(ownership), new PanGlossInvoker())`
  (add `using SIL.Motif.Host.PanGloss;`). This constructs an invoker whose queue is disposed with nothing until
  Task 5 rewires the commands; that is acceptable for one commit.

  In `src/SIL.Motif.Worker/Program.cs`, replace `TryBuildTrialHandler`'s catalog construction with:

```csharp
        // No executable, no Trial handler: a Trial job would otherwise fail on every attempt.
        if (PanGlossExecutable.TryLocate() is null) return null;
        IAssessorCatalog catalog;
        try
        {
            var ownership = WorkspaceOwnership.Bootstrap(options.Root);
            catalog = new AssessorCatalog(new IAssessor[]
            {
                new PanGlossAssessor(new StatsCacheStore(ownership), new PanGlossInvoker()),
            });
        }
        catch (ArgumentException)
        {
            return null;
        }
```

  (add `using SIL.Motif.Host.PanGloss;`). The invoker lives as long as the runner process; its queue's
  background loop ends with the process.

- [ ] **Step 8: Update the fakes and tests.** In `tests/SIL.Motif.Tests/TestFixtures/FakeAssessor.cs` remove
  `LastGovernor`, the `governor` parameter, the assignment, and `using SIL.Motif.Host.Parser;`.

  In `tests/SIL.Motif.Tests/Assess/PanGlossAssessorTests.cs`: delete `FakeStatsRunner` and `FakeBatchParser`;
  change `FakeReportRunner.RunAsync` to `(string exportedCandidate, CancellationToken cancellationToken)` and
  drop `LastGovernor`; add this helper to the class:

```csharp
    // One invoker answers both batch passes: rows for the plain one, a cache file for the --stats one.
    private static FakeInvoker Invoker(string tsvRows = "0\tmotifa\t12\tok\tsig\n", byte[]? cacheBytes = null) => new()
    {
        Respond = request =>
        {
            if (request is PanGlossRequest.Batch { StatsCachePath: { } cachePath })
                File.WriteAllBytes(cachePath, cacheBytes ?? []);
            return new PanGlossOutcome.Completed(tsvRows, string.Empty, TimeSpan.Zero);
        },
    };
```

  and rewrite every constructor call to the new shape. The mapping is mechanical:
  `new PanGlossAssessor(_cachePaths, new FakeBatchParser(X), new FakeReportRunner(R), new FakeStatsRunner(B))`
  becomes `new PanGlossAssessor(_cachePaths, Invoker(cacheBytes: B), new FakeReportRunner(R))`; where a test
  built a `BatchAnalysis batch` and passed `new FakeBatchParser(new ParserRunResult(batch, null))`, pass
  `Invoker(tsvRows: "0\tmotifa\t12\tok\tsig\n")` instead and replace `Assert.Same(batch, raw.Analysis)` with
  assertions on the produced rows (`Assert.Single(raw.Analysis.Words)`, `Assert.Equal("motifa", raw.Analysis.Words[0].Word)`,
  `Assert.Equal(12, raw.Analysis.Words[0].ElapsedMs)`). Where a test asserted on a runner's `LastGovernor`,
  delete that assertion. Add `using SIL.Motif.Host.PanGloss;` and `using SIL.Motif.Tests.TestFixtures;`.

  Delete `tests/SIL.Motif.Tests/Parser/PanGlossGovernorTests.cs` (`git rm`): containment is pinned by
  `PanGlossInvokerTests.EveryLaunchRunsInsideTheAdmittedJobObject`.

  In `tests/SIL.Motif.Tests/Parser/GrammarCoverageFigureIntegrationTests.cs` replace the block from
  `var parser = new PanGlossParser();` through `Assert.NotNull(report);` with:

```csharp
        using var invoker = new PanGlossInvoker();
        var parser = new PanGlossParser(invoker);

        var batchResult = parser.AnalyseBatchAsync(projectPath, corpus.Words, ParserEngine.FstPrunedByHermitCrab,
            TimeSpan.FromSeconds(5), "test:coverage", CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(batchResult.Succeeded, batchResult.Refusal?.Detail ?? batchResult.Outcome.Message);

        var report = new PanGlossAssessmentProcess().RunAsync(Path.GetDirectoryName(projectPath)!, CancellationToken.None)
            .GetAwaiter().GetResult();
```

  change `[RealParserFact]` on that test to
  `[RealParserFact(Skip = "The shipped pangloss has no assess subcommand (grill K46); the figure needs its report.")]`,
  replace later `report!` with `report`, and add `using SIL.Motif.Host.PanGloss;`.

  In `tests/SIL.Motif.Tests/Parser/ParserSeamIntegrationTests.cs` replace
  `var (report, refusal) = new PanGlossParser().Assess(_projectPath, words);` and the following `Assert.Null(refusal);` with

```csharp
        var report = new PanGlossAssessmentProcess().RunAsync(Path.GetDirectoryName(_projectPath)!, CancellationToken.None)
            .GetAwaiter().GetResult();
```

  (`words` becomes unused: delete its declaration), replace `report!` with `report`, and mark the test
  `[RealParserFact(Skip = "The shipped pangloss has no assess subcommand (grill K46).")]`. If
  `RealParserFactAttribute` does not expose `Skip`, open `tests/SIL.Motif.Tests/TestFixtures/RealParserFactAttribute.cs`
  and confirm it derives from `FactAttribute`; `Skip` is inherited.

- [ ] **Step 9: Run green.**

Run: `pwsh -NoProfile -File ./build.ps1; dotnet test tests/SIL.Motif.Tests --no-build --filter "FullyQualifiedName~PanGlossParserTests|FullyQualifiedName~PanGlossAssessorTests|FullyQualifiedName~AssessCommandTests|FullyQualifiedName~HandoffWriterTests|FullyQualifiedName~TrialJobHandlerTests"`
Expected: all pass.

- [ ] **Step 10: Gate and commit.**

Run: `pwsh -NoProfile -File ./test.ps1`
Expected: `Passed!` with 0 failed.

```bash
git add -A src tests
git commit -F - <<'EOF'
refactor: the batch pass and the Assessor run through the invocation module

PanGlossParser becomes a reading of one batch outcome, asynchronous and sealed;
the stats-collecting batch runner disappears into the same request with a cache
path. The Assessor seam drops its governor parameter, since containment is no
longer a caller's job, and reports a parser it could not run as a typed
AssessorUnavailableException rather than the launcher's exception leaking through.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

### Task 5: Stats, Assess and Handoff cross the module; the old seams retire

**Files:**

- Modify: `src/SIL.Motif.Commands/Assess/StatsCommand.cs`, `src/SIL.Motif.Commands/Assess/AssessCommand.cs`,
  `src/SIL.Motif.Commands/Handoff/HandoffCommand.cs`
- Delete: `src/SIL.Motif.Host/Assess/IPanGlossStatsQuery.cs`, `src/SIL.Motif.Host/Assess/PanGlossStatsQueryProcess.cs`,
  `src/SIL.Motif.Host/Parser/IPanGlossGrammarImporter.cs`, `src/SIL.Motif.Host/Parser/PanGlossGrammarImportProcess.cs`,
  `src/SIL.Motif.Host/Parser/IParserProcessGovernor.cs`, `src/SIL.Motif.Host/PanGloss/WindowsCpuJobGovernor.cs`,
  `tests/SIL.Motif.Tests/TestFixtures/RecordingGovernor.cs`, `tests/SIL.Motif.Tests/Assess/PanGlossStatsQueryTests.cs`,
  `tests/SIL.Motif.Tests/Parser/PanGlossGrammarImportTests.cs`
- Modify: `tests/SIL.Motif.Tests/Commands/StatsCommandTests.cs`, `tests/SIL.Motif.Tests/Commands/AssessCommandTests.cs`,
  `tests/SIL.Motif.Tests/Handoff/HandoffWriterTests.cs`

- [ ] **Step 1: Write the failing tests first.** In `tests/SIL.Motif.Tests/Commands/StatsCommandTests.cs` the
  class's private `Run(fwDataPath, proposalId, output, forwarded, Func<IPanGlossStatsQuery>)` helper (line 229)
  becomes:

```csharp
    private static CommandOutcome<StatsCommandResponse> Run(string fwDataPath, string? proposalId,
        StatsOutputKind output, IReadOnlyList<string> forwarded, IPanGlossInvoker invoker,
        CancellationToken cancellationToken = default) =>
        StatsCommand.Run(new StatsRequest(fwDataPath, proposalId, output, forwarded), invoker, cancellationToken);
```

  Replace the `FakeStatsQuery(string standardOutput)` class (line 292) with a helper that builds a
  `FakeInvoker`, and read what the fake saw from the recorded request:

```csharp
    // Answers every stats request with the same rows; what the command sent is read back from Requests.
    private static FakeInvoker Completing(string standardOutput) => new()
    {
        Respond = _ => new PanGlossOutcome.Completed(standardOutput, string.Empty, TimeSpan.Zero),
    };

    private static PanGlossRequest.Stats SeenStats(FakeInvoker fake) =>
        Assert.IsType<PanGlossRequest.Stats>(Assert.Single(fake.Requests).Request);
```

  so `fake.SeenCachePath` becomes `SeenStats(fake).CachePath` and `fake.SeenForwardedArguments` becomes
  `SeenStats(fake).ForwardedArguments`. Delete `CancellingStatsQuery` and `UnreachableQuery()`; where
  `UnreachableQuery()` was passed, pass `new FakeInvoker()` (the refusal happens before any request is made,
  so assert `Assert.Empty(fake.Requests)` where the old test relied on the query being unreachable). Rewrite the
  two existing failure tests and add two more:

```csharp
    [Fact]
    public void AnUnavailableParserIsRefusedRatherThanEscapingAsAnException()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        CaptureBaseline(fwDataPath);
        RecordAssessment(fwDataPath, proposalId: null, cachePath: "cache.sqlite");
        var fake = new FakeInvoker { Respond = _ => new PanGlossOutcome.Unavailable("no pangloss here") };

        var outcome = Run(fwDataPath, null, StatsOutputKind.Text, [], fake);

        Assert.False(outcome.Succeeded);
        Assert.Equal("stats.parser-unavailable", outcome.Refusal!.Code);
        Assert.Equal("no pangloss here", outcome.Refusal.Message);
    }

    [Fact]
    public void CancellationDuringTheQueryIsRefusedAsCancelled()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        CaptureBaseline(fwDataPath);
        RecordAssessment(fwDataPath, proposalId: null, cachePath: "cache.sqlite");
        var fake = new FakeInvoker { Respond = _ => new PanGlossOutcome.Cancelled() };

        var outcome = Run(fwDataPath, null, StatsOutputKind.Text, [], fake);

        Assert.False(outcome.Succeeded);
        Assert.Equal("stats.cancelled", outcome.Refusal!.Code);
    }

    [Fact]
    public void ARefusedStatsRunBecomesAParserRefusedRefusal_CarryingTheExitCode()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        CaptureBaseline(fwDataPath);
        RecordAssessment(fwDataPath, proposalId: null, cachePath: "cache.sqlite");
        var fake = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Refused(2, "stale cache", string.Empty, "pangloss stats exited 2:\nstale cache"),
        };

        var outcome = Run(fwDataPath, null, StatsOutputKind.Text, [], fake);

        Assert.False(outcome.Succeeded);
        Assert.Equal("stats.parser-refused", outcome.Refusal!.Code);
        Assert.Equal("2", outcome.Refusal.Facts["exitCode"]);
        Assert.Contains("stale cache", outcome.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATimedOutStatsRunBecomesATimedOutRefusal()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        CaptureBaseline(fwDataPath);
        RecordAssessment(fwDataPath, proposalId: null, cachePath: "cache.sqlite");
        var fake = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.TimedOut(TimeSpan.FromMinutes(10),
                "pangloss stats did not finish within 10 minutes and was stopped."),
        };

        var outcome = Run(fwDataPath, null, StatsOutputKind.Text, [], fake);

        Assert.Equal("stats.timed-out", outcome.Refusal!.Code);
        Assert.Equal("10", outcome.Refusal.Facts["capMinutes"]);
    }
```

  Add `using SIL.Motif.Host.PanGloss;` and `using SIL.Motif.Tests.TestFixtures;` (if absent); remove
  `using SIL.Motif.Host.Parser;` once nothing else in the file needs it.

- [ ] **Step 2: Run red.**

Run: `pwsh -NoProfile -File ./build.ps1`
Expected: FAIL to compile — `StatsCommand.Run` does not take an `IPanGlossInvoker`.

- [ ] **Step 3: Rewrite `StatsCommand`'s seam.** Replace the public `Stats` method and the `Run` signature,
  the collaborator construction and the query call with:

```csharp
    /// <summary>Queries statistics through a real parser invocation.</summary>
    public static CommandOutcome<StatsCommandResponse> Stats(
        StatsRequest request, CancellationToken cancellationToken = default)
    {
        using var invoker = new PanGlossInvoker();
        return Run(request, invoker, cancellationToken);
    }

    /// <summary>
    /// Queries statistics through an explicitly supplied invoker — a fake stands in for PanGloss in tests.
    /// Admission and containment are the invoker's, so this command holds no queue and no governor.
    /// </summary>
    internal static CommandOutcome<StatsCommandResponse> Run(
        StatsRequest request, IPanGlossInvoker invoker, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(invoker);
```

  Keep the argument checks and the Baseline/Assessment resolution unchanged. Delete the `statsQueryFactory()`
  try/catch block entirely. Replace the `PanGlossStatsOutput output; try { ... } catch (OperationCanceledException) { ... }`
  block with:

```csharp
            var outcome = invoker.RunAsync(
                    new PanGlossRequest.Stats(baseline.FwDataPath, assessment.CachePath, forwarded),
                    "stats:" + workspaceKey, cancellationToken)
                .GetAwaiter().GetResult();
            if (outcome is not PanGlossOutcome.Completed completed)
                return CommandOutcome<StatsCommandResponse>.Refused(ParserRefusal(outcome, request.ProjectPath));
```

  and use `completed.Output` where `output.StandardOutput` was used. Add the mapping helper:

```csharp
    // One place turns the invocation's outcome into this command's refusal vocabulary.
    private static Refusal ParserRefusal(PanGlossOutcome outcome, string projectPath) => outcome switch
    {
        PanGlossOutcome.Cancelled => new Refusal(
            "stats.cancelled", FailureReason.Refused, "The statistics query was cancelled.",
            Fact(("projectPath", projectPath))),
        PanGlossOutcome.Unavailable unavailable => new Refusal(
            "stats.parser-unavailable", FailureReason.Refused, unavailable.Message,
            Fact(("projectPath", projectPath))),
        PanGlossOutcome.TimedOut timedOut => new Refusal(
            "stats.timed-out", FailureReason.Refused, timedOut.Message,
            Fact(("projectPath", projectPath), ("capMinutes", timedOut.Cap.TotalMinutes.ToString("0.#", CultureInfo.InvariantCulture)))),
        PanGlossOutcome.Refused refused => new Refusal(
            "stats.parser-refused", FailureReason.Refused, refused.Message,
            Fact(("projectPath", projectPath), ("exitCode", refused.ExitCode.ToString(CultureInfo.InvariantCulture)))),
        _ => new Refusal(
            "stats.parser-unavailable", FailureReason.Refused, outcome.Message,
            Fact(("projectPath", projectPath))),
    };
```

  Add `using System.Globalization;` and `using SIL.Motif.Host.PanGloss;`; remove `using SIL.Motif.Host.Assess;`
  and `using SIL.Motif.Host.Parser;` if nothing else needs them (`AssessmentKind` is in `Host.Assess`: keep that one).
  Fix the class remarks: the sentence naming `IPanGlossStatsQuery` becomes "hands
  `StatsRequest.ForwardedArguments` to the invocation untouched".

- [ ] **Step 4: Rewrite `AssessCommand`'s seam.** Replace `Assess(request, managedRoot, ...)` and `Run` with:

```csharp
    public static CommandOutcome<AssessCommandResponse> Assess(
        AssessRequest request, string managedRoot, Action<AssessmentProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var ownership = WorkspaceOwnership.Bootstrap(managedRoot);
        using var invoker = new PanGlossInvoker();
        return Run(request, managedRoot, new PanGlossAssessor(new StatsCacheStore(ownership), invoker), invoker,
            onProgress, cancellationToken);
    }

    /// <summary>
    /// Measures the project against explicitly supplied collaborators — a fake Assessor and a fake invoker stand
    /// in for a real PanGloss in tests. Admission and containment are the invoker's; this command holds neither.
    /// </summary>
    internal static CommandOutcome<AssessCommandResponse> Run(
        AssessRequest request, string managedRoot, IAssessor assessor, IPanGlossInvoker invoker,
        Action<AssessmentProgress>? onProgress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(assessor);
        ArgumentNullException.ThrowIfNull(invoker);
```

  Delete the factory try/catch at the top of the store callback and the `<remarks>` about factories. Replace the
  queue-wrapped `ProduceAsync` with:

```csharp
            IReadOnlyList<ProducedAssessment> produced;
            try
            {
                produced = assessor.ProduceAsync(scope, exportedCandidate, cancellationToken).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return CommandOutcome<AssessCommandResponse>.Refused(Cancelled(request.ProjectPath));
            }
            catch (AssessorUnavailableException ex)
            {
                return CommandOutcome<AssessCommandResponse>.Refused(ParserUnavailable(request.ProjectPath, ex.Message));
            }
```

  and the summary rendering with:

```csharp
            string summaryMarkdown;
            if (statsCachePath is null)
            {
                summaryMarkdown = "(no per-object statistics were collected)" + Environment.NewLine;
            }
            else
            {
                var summary = invoker.RunAsync(
                        new PanGlossRequest.Stats(baseline.FwDataPath, statsCachePath, Array.Empty<string>()),
                        "assess:stats:" + workspaceKey, cancellationToken)
                    .GetAwaiter().GetResult();
                switch (summary)
                {
                    case PanGlossOutcome.Completed completed:
                        summaryMarkdown = "```" + Environment.NewLine + completed.Output + "```" + Environment.NewLine;
                        break;
                    case PanGlossOutcome.Cancelled:
                        return CommandOutcome<AssessCommandResponse>.Refused(Cancelled(request.ProjectPath));
                    default:
                        return CommandOutcome<AssessCommandResponse>.Refused(ParserUnavailable(request.ProjectPath, summary.Message));
                }
            }
```

  Delete `RenderSummary`; change `ParserUnavailable` to take `(string projectPath, string message)` and use
  `message`. Remove the `catch (ParserUnavailableException ex)` arm. Drop `using SIL.Motif.Host.Parser;` and
  `using SIL.Motif.Worker.PanGloss;`/`using SIL.Motif.Host.PanGloss;` as appropriate (keep the latter). Update
  the class `<summary>` to say the Assessor runs "through the PanGloss invocation, which admits and contains
  every parser process" instead of "under the machine-wide PanGloss admission queue".

- [ ] **Step 5: Rewrite `HandoffCommand`'s seam.** Replace `Handoff(request, managedRoot, ...)` and `Run`'s
  signature with:

```csharp
    public static CommandOutcome<HandoffCommandResponse> Handoff(
        HandoffRequest request, string managedRoot, Action<AssessmentProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var ownership = WorkspaceOwnership.Bootstrap(managedRoot);
        using var invoker = new PanGlossInvoker();
        return Run(request, managedRoot, new PanGlossAssessor(new StatsCacheStore(ownership), invoker), invoker,
            onProgress, cancellationToken);
    }

    /// <summary>
    /// Writes a Handoff folder against explicitly supplied collaborators — a fake Assessor and a fake invoker
    /// stand in for a real PanGloss in tests. Admission and containment are the invoker's.
    /// </summary>
    internal static CommandOutcome<HandoffCommandResponse> Run(
        HandoffRequest request, string managedRoot, IAssessor assessor, IPanGlossInvoker invoker,
        Action<AssessmentProgress>? onProgress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(assessor);
        ArgumentNullException.ThrowIfNull(invoker);
```

  Delete `var grammarImporter = grammarImporterFactory();`. The nested Assess call becomes
  `AssessCommand.Run(new AssessRequest(request.ProjectPath, request.Selection), managedRoot, assessor, invoker, ForwardExceptComplete(onProgress), cancellationToken)`.
  Replace the queue-wrapped import with:

```csharp
                    Report(onProgress, AssessmentStage.ImportingGrammar, "Importing the grammar...");
                    var import = invoker.RunAsync(
                            new PanGlossRequest.Import(baseline.FwDataPath, Path.Combine(incoming, HandoffWriter.GrammarFileName)),
                            "handoff:import:" + request.ProjectPath, cancellationToken)
                        .GetAwaiter().GetResult();
                    if (import is PanGlossOutcome.Cancelled) return Cancelled(request.ProjectPath);
                    if (import is not PanGlossOutcome.Completed)
                    {
                        return new Refusal("handoff.parser-unavailable", FailureReason.Refused, import.Message,
                            Fact(("projectPath", request.ProjectPath)));
                    }
```

  Replace the queue-wrapped statistics-group loop with a plain loop (each group's query takes its own
  admission inside the invoker):

```csharp
                        foreach (var group in HandoffWriter.StatisticsGroups)
                        {
                            var groupOutcome = StatsCommand.Run(
                                new StatsRequest(request.ProjectPath, null, StatsOutputKind.Text,
                                    new[] { "--group", group, "--format", "jsonl" }),
                                invoker, cancellationToken);
                            if (!groupOutcome.Succeeded) return groupOutcome.Refusal;

                            File.WriteAllText(
                                Path.Combine(statisticsDir, group + ".jsonl"), groupOutcome.Value!.Text);
                        }
```

  Remove the `catch (ParserUnavailableException ex)` arm at the bottom (nothing throws it any more; the
  `OperationCanceledException` arm stays). Fix `using` lines to match.

- [ ] **Step 6: Delete the retired seams.**

```bash
git rm src/SIL.Motif.Host/Assess/IPanGlossStatsQuery.cs src/SIL.Motif.Host/Assess/PanGlossStatsQueryProcess.cs
git rm src/SIL.Motif.Host/Parser/IPanGlossGrammarImporter.cs src/SIL.Motif.Host/Parser/PanGlossGrammarImportProcess.cs
git rm src/SIL.Motif.Host/Parser/IParserProcessGovernor.cs src/SIL.Motif.Host/PanGloss/WindowsCpuJobGovernor.cs
git rm tests/SIL.Motif.Tests/TestFixtures/RecordingGovernor.cs
git rm tests/SIL.Motif.Tests/Assess/PanGlossStatsQueryTests.cs tests/SIL.Motif.Tests/Parser/PanGlossGrammarImportTests.cs
```

  The argv, both-streams, deadlock and cancellation behaviours those two test files pinned are now pinned by
  `PanGlossInvokerTests`. Grep for any remaining reference:
  `grep -rn "IPanGlossStatsQuery\|IPanGlossGrammarImporter\|IParserProcessGovernor\|WindowsCpuJobGovernor\|PanGlossStatsQueryProcess\|PanGlossGrammarImportProcess\|RecordingGovernor" src tests --include=*.cs | grep -v /obj/`
  and fix each hit; the docs mention in `src/SIL.Motif.Commands/ReportCommands.cs:20` about
  `PanGlossParser` still holds and stays.

- [ ] **Step 7: Update `AssessCommandTests` and `HandoffWriterTests`.** In both, delete `NewQueue()`, every
  `using var queue = NewQueue();`, every `WindowsCpuJobGovernor` assertion, and the `FakeStatsQuery`,
  `RecordingStatsQuery`, `RecordingGrammarImporter` and `ThrowingGrammarImporter` classes. Every
  `AssessCommand.Run(request, root, () => assessor, () => statsQuery, queue, progress, token)` becomes
  `AssessCommand.Run(request, root, assessor, invoker, progress, token)` with
  `var invoker = new FakeInvoker { Respond = _ => new PanGlossOutcome.Completed("fake stats", string.Empty, TimeSpan.Zero) };`.
  Every `HandoffCommand.Run(request, root, NewAssessor, NewRealStatsQuery, NewRealGrammarImporter, NewQueue(), progress, token)`
  becomes `HandoffCommand.Run(request, root, NewAssessor(), NewInvoker(), progress, token)` where

```csharp
    // The real module against the fake executable: the Handoff's grammar and statistics files come from it.
    private static PanGlossInvoker NewInvoker() => new(FakeParser.ExecutablePath, new MachinePanGlossQueue(new[]
    {
        "Local\\MotifHandoffWriterTests-" + Guid.NewGuid().ToString("N") + "-0",
        "Local\\MotifHandoffWriterTests-" + Guid.NewGuid().ToString("N") + "-1",
    }));
```

  (dispose it: `using var invoker = NewInvoker();` per test). The test that used `ThrowingGrammarImporter` to
  pin cleanup after a failed import now uses `new FakeInvoker { Respond = _ => new PanGlossOutcome.Unavailable("boom") }`
  and asserts the same cleanup plus `Assert.Equal("handoff.parser-unavailable", outcome.Refusal!.Code)`. The
  test at line 77-84 that asserted governors reached the importer and stats query is deleted; its subject is
  pinned by `PanGlossInvokerTests.EveryLaunchRunsInsideTheAdmittedJobObject`.

- [ ] **Step 8: Run green.**

Run: `pwsh -NoProfile -File ./build.ps1; dotnet test tests/SIL.Motif.Tests --no-build --filter "FullyQualifiedName~StatsCommandTests|FullyQualifiedName~AssessCommandTests|FullyQualifiedName~HandoffWriterTests|FullyQualifiedName~StatsArgvTests|FullyQualifiedName~AssessArgvTests"`
Expected: all pass. `StatsArgvTests` drives the real CLI against `FakePanGloss` and must still pass unchanged.

- [ ] **Step 9: Gate and commit.**

Run: `pwsh -NoProfile -File ./test.ps1`
Expected: `Passed!` with 0 failed.

```bash
git add -A src tests
git commit -F - <<'EOF'
refactor: stats, assess and handoff cross the invocation module

The three commands take an invoker instead of a queue, a governor and per-
subcommand factories. Each parser outcome maps to the command's refusal
vocabulary in one place; the stats verb now takes queue admission and
containment like every other launch. The statistics-query and grammar-import
launchers, their interfaces, the governor seam and its adapter retire.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

---

### Task 6: Close the ledger and record what moved

**Files:**

- Modify: `docs/known-issues.md`
- Modify: `docs/grill-plan-a.md`
- Modify: `README.md` only if it names `--engine`, `IPanGlossStatsQuery` or a deleted type (grep first)

- [ ] **Step 1: Correct the ledger forward.** In `docs/known-issues.md`, leave the "Unfinished by choice" entry
  about `motif stats` as written and append under `## Corrections`:

```markdown
### 2026-09-08 — `motif stats` takes queue admission

Closed by construction under ADR 0044: every parser process is started by the PanGloss invocation, which
takes machine-queue admission and runs inside the job object. The standalone `stats` verb can no longer
launch unadmitted, because nothing above the invocation can obtain a process.
```

- [ ] **Step 2: Record the K46 consequences.** Append to the K addendum at the end of `docs/grill-plan-a.md`:

```markdown
- *2026-09-08, after ADR 0044:* `PanGlossParser.Assess` is gone; the `assess` route survives only in
  `PanGlossAssessmentProcess`, outside the invocation module and uncontained, as the ADR prescribes.
  `FakePanGloss` no longer answers `assess`. Ten tests that drove that route are skipped with a K46 reason,
  not deleted: eight in `FakeParserSeamTests`, and the two `RealParserFact` tests that need a report
  (`GrammarCoverageFigureIntegrationTests`, `ParserSeamIntegrationTests`). Deciding K46 revives or removes them.
```

- [ ] **Step 3: Check the README.** `grep -n "IPanGlossStatsQuery\|PanGlossStatsQueryProcess\|IParserProcessGovernor\|--engine" README.md docs/*.md | grep -v "grill-plan-a\|known-issues\|adr/"`.
  Fix any hit that describes current behaviour (not historical design documents, which stay as written).

- [ ] **Step 4: Gate and commit.**

Run: `pwsh -NoProfile -File ./test.ps1`
Expected: `Passed!` with 0 failed.

```bash
git add docs README.md
git commit -F - <<'EOF'
docs: close the stats admission entry and record what ADR 0044 moved

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
EOF
```

- [ ] **Step 5: Tick every checkbox in this plan** that was completed, and commit the plan file with
  `docs: tick the PanGloss invocation plan`.
