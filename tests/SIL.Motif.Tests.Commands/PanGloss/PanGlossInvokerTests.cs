using System.Text.Json;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>
/// The invocation module against the fake executable: argument shaping per subcommand, both streams
/// drained, admission and containment, the wall-clock cap, cancellation, and every failure as an outcome.
/// </summary>
public sealed class PanGlossInvokerTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-invoker-" + Guid.NewGuid().ToString("N"));

    public PanGlossInvokerTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException exception)
        {
            _output.WriteLine($"Temporary parser cleanup refused (HRESULT 0x{exception.HResult:X8}): {exception.Message}");
        }
    }

    [Fact]
    public void SequentialBatchProgressNamesTheStartedWordAfterCompletedRows()
    {
        var path = Path.Combine(_root, "progress.tsv");
        File.WriteAllText(path, "0\tone\tSTARTED\n0\tone\t12\tanalysed\tsig\n1\ttwo\tSTARTED\n");
        var reader = new BatchProgressReader(["one", "two"]);

        var progress = reader.Read(path);

        Assert.Equal(1, progress?.Completed);
        Assert.Equal(2, progress?.Total);
        Assert.Equal("two", progress?.CurrentWord);

        File.AppendAllText(path, "1\ttwo\t8\tanalysed\tsig");
        Assert.Equal(1, reader.Read(path)?.Completed);

        File.AppendAllText(path, "\n");
        var finished = reader.Read(path);
        Assert.Equal(2, finished?.Completed);
        Assert.Null(finished?.CurrentWord);
    }

    [Fact]
    public void BatchProgressReaderPreservesAppendedPartialUtf8RowsInSequence()
    {
        var path = Path.Combine(_root, "incremental-progress.tsv");
        var word = "māŋ";
        var reader = new BatchProgressReader([word, "two"]);
        File.WriteAllBytes(path, []);

        Assert.Null(reader.Read(path));
        AppendBytes(path, Encoding.UTF8.GetBytes($"0\t{word}\tSTARTED\n"));
        var started = new TrialWordProgress(0, 2, word);
        Assert.Equal(started, reader.Read(path));

        var nextRows = Encoding.UTF8.GetBytes($"0\t{word}\t12\tanalysed\tñ\n1\ttwo\tSTARTED\n");
        var split = Array.IndexOf(nextRows, (byte)0xC3) + 1;
        AppendBytes(path, nextRows[..split]);
        Assert.Equal(started, reader.Read(path));
        AppendBytes(path, nextRows[split..]);
        var secondStarted = new TrialWordProgress(1, 2, "two");
        Assert.Equal(secondStarted, reader.Read(path));
        Assert.Equal(secondStarted, reader.Read(path));

        var finalRow = Encoding.UTF8.GetBytes("1\ttwo\t8\tanalysed\tfinal\n");
        AppendBytes(path, finalRow[..^1]);
        Assert.Equal(secondStarted, reader.Read(path));
        AppendBytes(path, finalRow[^1..]);
        Assert.Equal(new TrialWordProgress(2, 2, null), reader.Read(path));
    }

    [Fact]
    public void TextSnapshotKeepsUtf8BomDecodingAndHashesTheOriginalBytes()
    {
        var path = Path.Combine(_root, "bom.tsv");
        var text = "0\tword\t5\tok\tsignature\n";
        var body = Encoding.UTF8.GetBytes(text);
        var bytes = new byte[Encoding.UTF8.Preamble.Length + body.Length];
        Encoding.UTF8.Preamble.CopyTo(bytes.AsSpan());
        body.CopyTo(bytes.AsSpan(Encoding.UTF8.Preamble.Length));
        File.WriteAllBytes(path, bytes);

        var snapshot = BatchInvocationEvidence.ReadTextWithDigest(path);

        Assert.Equal(text, snapshot.Text);
        Assert.Equal(BatchInvocationEvidence.DigestFile(path), snapshot.Sha256);
        Assert.True(BatchInvocationEvidence.TextMatchesDigest(snapshot.Text, snapshot.Sha256));
    }

    [Fact]
    public void TextDigestStreamsASurrogatePairAcrossTheBufferBoundary()
    {
        var path = Path.Combine(_root, "unicode.tsv");
        var text = new string('a', 4095) + char.ConvertFromUtf32(0x1F642) + new string('z', 4097);
        File.WriteAllBytes(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));

        var snapshot = BatchInvocationEvidence.ReadTextWithDigest(path);

        Assert.Equal(text, snapshot.Text);
        Assert.True(BatchInvocationEvidence.TextMatchesDigest(snapshot.Text, snapshot.Sha256));
    }

    [Fact]
    public async Task BatchReportsTheCurrentWordBeforeTheNextOneCompletes()
    {
        var project = Project("batch-progress");
        var release = Path.Combine(_root, "release-word");
        FakeParser.Behave(_root, new { streamProgress = true, holdEachWordUntil = release });
        using var invoker = Invoker();
        var seen = new ConcurrentQueue<TrialWordProgress>();

        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(project,
            ["one", "two"], TimeSpan.FromSeconds(1))
        {
            OnProgress = progress =>
            {
                seen.Enqueue(progress);
                // The fake finishes a word only once Motif has reported that the parser is on it.
                if (progress.CurrentWord is not null)
                    File.WriteAllText(release + "." + progress.Completed, string.Empty);
            },
        }, "test:batch-progress", CancellationToken.None);

        Assert.True(outcome is PanGlossOutcome.Completed, outcome.Message);
        var updates = seen.ToArray();
        Assert.Contains(new TrialWordProgress(0, 2, "one"), updates);
        Assert.Contains(new TrialWordProgress(1, 2, "two"), updates);
        Assert.Equal(new TrialWordProgress(2, 2, null), updates[^1]);
        Assert.Equal(updates.Distinct(), updates);
        Assert.All(updates.Zip(updates.Skip(1)), pair => Assert.True(pair.First.Completed <= pair.Second.Completed));
    }

    [Fact]
    public async Task ProgressCallbackFailureDoesNotFailTheCompletedBatch()
    {
        var project = Project("batch-progress-callback-failure");
        FakeParser.Behave(_root, new { streamProgress = true, delayMilliseconds = 50 });
        using var invoker = Invoker();
        var callbacks = 0;

        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(project,
            ["one"], TimeSpan.FromSeconds(1))
        {
            OnProgress = _ =>
            {
                Interlocked.Increment(ref callbacks);
                throw new InvalidOperationException("progress sink unavailable");
            },
        }, "test:batch-progress-callback-failure", CancellationToken.None);

        Assert.IsType<PanGlossOutcome.Completed>(outcome);
        Assert.True(callbacks > 0);
    }

    [Fact]
    public void ProgressMatchesTheParserTrimmedWord()
    {
        var path = Path.Combine(_root, "trimmed-progress.tsv");
        File.WriteAllText(path, "0\tone\tSTARTED\n");

        Assert.Equal(" one ", new BatchProgressReader([" one "]).Read(path)?.CurrentWord);
    }

    [Fact]
    public async Task FastBatchPublishesItsFinalWordCount()
    {
        var project = Project("fast-batch-progress");
        using var invoker = Invoker();
        var seen = new ConcurrentQueue<TrialWordProgress>();

        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(project,
            ["one", "two"], TimeSpan.FromSeconds(1)) { OnProgress = seen.Enqueue },
            "test:fast-batch-progress", CancellationToken.None);

        Assert.IsType<PanGlossOutcome.Completed>(outcome);
        Assert.Contains(seen, item => item.Completed == 2 && item.Total == 2 && item.CurrentWord is null);
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
        Assert.Contains("\"orientation\":\"word\"", completed.Output, StringComparison.Ordinal);
        Assert.Equal(["stats", grammar, "--cache", cache, "--group", "word", "--format", "jsonl"], Argv(grammar));
    }

    [Fact]
    public async Task GrammarHealthReturnsItsReportWhenPanGlossExitsNonzero()
    {
        var grammar = Project("grammar-health-error");
        const string report = "{\"schema_version\":3,\"fieldworks_project\":{\"name\":\"p\",\"source\":\"argument\"},\"summary\":[],\"diagnostics\":[]}";
        FakeParser.Behave(_root, new { ExitCode = 1, GrammarHealthReportJson = report });
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.GrammarHealth(grammar, "p"), "test:grammar-health-error", CancellationToken.None);

        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        Assert.Equal(report, completed.Output);
    }

    [Fact]
    public async Task ChildEnvironmentKeepsDotnetRootSoAnApphostParserFindsItsRuntime()
    {
        var grammar = Project("dotnet-root-environment");
        var previous = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        // The running runtime's own root, so the fake apphost still starts with the variable set.
        var runtimeRoot = Path.GetFullPath(Path.Combine(
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        Environment.SetEnvironmentVariable("DOTNET_ROOT", previous ?? runtimeRoot);
        try
        {
            using var invoker = Invoker();
            var outcome = await invoker.RunAsync(
                new PanGlossRequest.Stats(grammar, Path.Combine(_root, "cache"), []),
                "test:dotnet-root-environment", CancellationToken.None);

            Assert.IsType<PanGlossOutcome.Completed>(outcome);
            Assert.Contains("DOTNET_ROOT", EnvironmentNames(grammar));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", previous);
        }
    }

    [Fact]
    public async Task ChildEnvironmentDoesNotReceiveAnUnrelatedParentVariable()
    {
        var grammar = Project("minimal-environment");
        var sentinel = "MOTIF_TEST_SENTINEL_" + Guid.NewGuid().ToString("N");
        var previous = Environment.GetEnvironmentVariable(sentinel);
        Environment.SetEnvironmentVariable(sentinel, "must-not-cross-process-boundary");
        try
        {
            using var invoker = Invoker();
            var outcome = await invoker.RunAsync(
                new PanGlossRequest.Stats(grammar, Path.Combine(_root, "cache"), []),
                "test:minimal-environment", CancellationToken.None);

            Assert.IsType<PanGlossOutcome.Completed>(outcome);
            Assert.DoesNotContain(sentinel, EnvironmentNames(grammar));
        }
        finally
        {
            // The real process variable is the boundary this test is proving.
            Environment.SetEnvironmentVariable(sentinel, previous);
        }
    }

    [Fact]
    public async Task AWrongDescriptionRefusesBeforeTheFirstBatch()
    {
        var project = Project("wrong-description");
        var executable = FakeParser.CopyWithWrongDescription(Path.Combine(_root, "wrong-parser"));
        using var queue = NewQueue();
        using var invoker = new PanGlossInvoker(executable, queue);

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Batch(project, ["motifa"], TimeSpan.FromSeconds(1)),
            "test:wrong-description", CancellationToken.None);

        var unavailable = Assert.IsType<PanGlossOutcome.Unavailable>(outcome);
        Assert.Contains(executable, unavailable.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(project)!, "_pangloss-argv.json")));
    }

    [Fact]
    public async Task BatchChecksAnalysesAgainstTheCachedDescriptionBeforeLaunchingThatRequest()
    {
        var project = Project("batch-capability-cache");
        var executable = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "batch-capability-parser"));
        FakeParser.OmitDescribeEntries(executable, "batch --analyses");
        using var queue = NewQueue();
        using var invoker = new PanGlossInvoker(executable, queue);

        var ordinary = await invoker.RunAsync(
            new PanGlossRequest.Batch(project, ["motifa"], TimeSpan.FromSeconds(1)),
            "test:batch-capability-cache", CancellationToken.None);
        var morphology = await invoker.RunAsync(
            new PanGlossRequest.Batch(project, ["motifa"], TimeSpan.FromSeconds(1)) { CollectAnalyses = true },
            "test:batch-capability-cache", CancellationToken.None);

        Assert.IsType<PanGlossOutcome.Completed>(ordinary);
        var unavailable = Assert.IsType<PanGlossOutcome.Unavailable>(morphology);
        Assert.Contains("batch --analyses", unavailable.Message, StringComparison.Ordinal);
        Assert.Equal(["describe", "batch"], FakeParser.Invocations(executable));
    }

    [Fact]
    public async Task TraceChecksItsEmittedFlagsBeforeLaunchingParse()
    {
        var grammar = Project("trace-capability");
        var executable = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "trace-capability-parser"));
        FakeParser.OmitDescribeEntries(executable, "parse --trace-details");
        using var queue = NewQueue();
        using var invoker = new PanGlossInvoker(executable, queue);

        var outcome = await invoker.RunAsync(new PanGlossRequest.Trace(grammar, "motifa"),
            "test:trace-capability", CancellationToken.None);

        var unavailable = Assert.IsType<PanGlossOutcome.Unavailable>(outcome);
        Assert.Contains("parse --trace-details", unavailable.Message, StringComparison.Ordinal);
        Assert.Equal(["describe"], FakeParser.Invocations(executable));
    }

    [Fact]
    public async Task TraceAllowsTheDescribedOptionalValueToBeOmittedBeforeItsOtherFlags()
    {
        var grammar = Project("trace-optional-value");
        var executable = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "trace-optional-parser"));
        using var queue = NewQueue();
        using var invoker = new PanGlossInvoker(executable, queue);

        var outcome = await invoker.RunAsync(new PanGlossRequest.Trace(grammar, "motifa"),
            "test:trace-optional-value", CancellationToken.None);

        Assert.IsType<PanGlossOutcome.Completed>(outcome);
        Assert.Equal(["parse", grammar, "motifa", "--trace", "--trace-format", "json", "--trace-details"],
            Argv(grammar));
        Assert.Equal(["describe", "parse"], FakeParser.Invocations(executable));
    }

    [Fact]
    public async Task ATraceWithAStepLimitPassesItAfterTheTraceFlags()
    {
        var grammar = Project("trace-step-limit");
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(new PanGlossRequest.Trace(grammar, "motifa") { StepLimit = 7 },
            "test:trace-step-limit", CancellationToken.None);

        Assert.IsType<PanGlossOutcome.Completed>(outcome);
        Assert.Equal(["parse", grammar, "motifa", "--trace", "--trace-format", "json", "--trace-details",
            "--step-cap", "7"], Argv(grammar));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ATraceWhoseSearchStoppedKeepsItsDocumentWhenPanGlossExitsNonzero(bool capped, bool timedOut)
    {
        var grammar = Project("trace-stopped-nonzero");
        FakeParser.Behave(_root, new
        {
            ExitCode = 1, TraceCapped = capped, TraceTimedOut = timedOut,
            TraceJson = "{\"type\":\"WordAnalysis\",\"inputShape\":\"motifa\",\"children\":[]}",
        });
        using var invoker = Invoker();
        var tracer = new PanGlossTracer(invoker);

        var outcome = await tracer.TraceAsync(grammar, "motifa", CancellationToken.None);

        var stopped = Assert.IsType<PanGlossTraceOutcome.Incomplete>(outcome);
        Assert.NotNull(stopped.Tree);
        Assert.Equal(capped, stopped.Details!.Capped);
        Assert.Equal(timedOut, stopped.Details.TimedOut);
    }

    [Fact]
    public async Task ATraceWhoseSearchCompletedIsRefusedWhenPanGlossExitsNonzero()
    {
        var grammar = Project("trace-complete-nonzero");
        FakeParser.Behave(_root, new { ExitCode = 1 });
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(new PanGlossRequest.Trace(grammar, "motifa"),
            "test:trace-complete-nonzero", CancellationToken.None);

        Assert.Equal(1, Assert.IsType<PanGlossOutcome.Refused>(outcome).ExitCode);
    }

    [Fact]
    public async Task ATraceWithNoDocumentIsRefusedWhenPanGlossExitsNonzero()
    {
        var grammar = Project("trace-no-document");
        FakeParser.Behave(_root, new { Mode = "fail", ExitCode = 1, StandardError = "pangloss parse: load failed" });
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(new PanGlossRequest.Trace(grammar, "motifa"),
            "test:trace-no-document", CancellationToken.None);

        var refused = Assert.IsType<PanGlossOutcome.Refused>(outcome);
        Assert.Contains("load failed", refused.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatsChecksTheCallersForwardedFlagsBeforeLaunchingStats()
    {
        var grammar = Project("stats-capability");
        var executable = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "stats-capability-parser"));
        using var queue = NewQueue();
        using var invoker = new PanGlossInvoker(executable, queue);

        var outcome = await invoker.RunAsync(new PanGlossRequest.Stats(grammar, Path.Combine(_root, "stats.cache"),
            ["--custom-filter", "selected"]), "test:stats-capability", CancellationToken.None);

        var unavailable = Assert.IsType<PanGlossOutcome.Unavailable>(outcome);
        Assert.Contains("stats --custom-filter", unavailable.Message, StringComparison.Ordinal);
        Assert.Equal(["describe"], FakeParser.Invocations(executable));
    }

    [Fact]
    public async Task StatsAcceptsInlineValuesForAdvertisedFlagsAndPreservesArgv()
    {
        var grammar = Project("stats-inline-values");
        var cache = Path.Combine(_root, "stats-inline-values.cache");
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Stats(grammar, cache, ["--group=word", "--format=jsonl"]),
            "test:stats-inline-values", CancellationToken.None);

        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        Assert.Contains("\"orientation\":\"word\"", completed.Output, StringComparison.Ordinal);
        Assert.Equal(["stats", grammar, "--cache", cache, "--group=word", "--format=jsonl"], Argv(grammar));
    }

    [Fact]
    public async Task StatsRefusesInlineValuesForAdvertisedSwitches()
    {
        var grammar = Project("stats-inline-switch");
        var executable = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "stats-inline-switch-parser"));
        FakeParser.AddStatsDescribeFlags(executable, ("--custom-switch", false));
        using var queue = NewQueue();
        using var invoker = new PanGlossInvoker(executable, queue);

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Stats(grammar, Path.Combine(_root, "stats-inline-switch.cache"),
                ["--custom-switch=value"]),
            "test:stats-inline-switch", CancellationToken.None);

        var unavailable = Assert.IsType<PanGlossOutcome.Unavailable>(outcome);
        Assert.Contains("stats --custom-switch", unavailable.Message, StringComparison.Ordinal);
        Assert.Equal(["describe"], FakeParser.Invocations(executable));
    }

    [Theory]
    [InlineData("--undeclared", false)]
    [InlineData("--custom-switch", true)]
    public async Task StatsDoesNotLetAValueTakeTheNextOption(string nextOption, bool advertised)
    {
        var grammar = Project("stats-missing-value");
        var executable = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "stats-missing-value-parser"));
        if (advertised) FakeParser.AddStatsDescribeFlags(executable, (nextOption, false));
        using var queue = NewQueue();
        using var invoker = new PanGlossInvoker(executable, queue);

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Stats(grammar, Path.Combine(_root, "stats-missing-value.cache"),
                ["--group", nextOption]),
            "test:stats-missing-value", CancellationToken.None);

        var unavailable = Assert.IsType<PanGlossOutcome.Unavailable>(outcome);
        Assert.Contains("does not accept a value for 'stats --group'", unavailable.Message,
            StringComparison.Ordinal);
        Assert.Equal(["describe"], FakeParser.Invocations(executable));
    }

    [Fact]
    public async Task GrammarHealthRequiresItsCommandBeforeLaunchingIt()
    {
        var grammar = Project("grammar-health-capability");
        var executable = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "grammar-health-capability-parser"));
        FakeParser.OmitDescribeEntries(executable, "grammar-health");
        using var queue = NewQueue();
        using var invoker = new PanGlossInvoker(executable, queue);

        var outcome = await invoker.RunAsync(new PanGlossRequest.GrammarHealth(grammar, "project"),
            "test:grammar-health-capability", CancellationToken.None);

        var unavailable = Assert.IsType<PanGlossOutcome.Unavailable>(outcome);
        Assert.Contains("grammar-health", unavailable.Message, StringComparison.Ordinal);
        Assert.Equal(["describe"], FakeParser.Invocations(executable));
    }

    [Fact]
    public async Task ImportRequiresItsCommandBeforeLaunchingIt()
    {
        var project = Project("import-capability");
        var executable = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "import-capability-parser"));
        FakeParser.OmitDescribeEntries(executable, "import");
        using var queue = NewQueue();
        using var invoker = new PanGlossInvoker(executable, queue);

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Import(project, Path.Combine(_root, "import-capability.json")),
            "test:import-capability", CancellationToken.None);

        var unavailable = Assert.IsType<PanGlossOutcome.Unavailable>(outcome);
        Assert.Contains("import", unavailable.Message, StringComparison.Ordinal);
        Assert.Equal(["describe"], FakeParser.Invocations(executable));
    }

    [Fact]
    public async Task AMalformedDescriptionRefusesAnImportBeforeTheRequestedCommand()
    {
        var project = Project("import-malformed-description");
        var executable = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "import-malformed-parser"));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(executable)!, "_fake-pangloss-describe-malformed"), string.Empty);
        using var queue = NewQueue();
        using var invoker = new PanGlossInvoker(executable, queue);

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Import(project, Path.Combine(_root, "import-malformed.json")),
            "test:import-malformed-description", CancellationToken.None);

        var unavailable = Assert.IsType<PanGlossOutcome.Unavailable>(outcome);
        Assert.Contains("invalid JSON", unavailable.Message, StringComparison.Ordinal);
        Assert.Equal(["describe"], FakeParser.Invocations(executable));
    }

    [Fact]
    public async Task CancellationDuringDescriptionDoesNotLaunchTheRequestedCommand()
    {
        var project = Project("import-cancel-description");
        var executable = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "import-cancel-parser"));
        var parserDirectory = Path.GetDirectoryName(executable)!;
        File.WriteAllText(Path.Combine(parserDirectory, "_fake-pangloss-describe-hang"), string.Empty);
        using var queue = NewQueue();
        using var invoker = new PanGlossInvoker(executable, queue);
        using var cancellation = new CancellationTokenSource();

        var run = invoker.RunAsync(new PanGlossRequest.Import(project, Path.Combine(_root, "import-cancel.json")),
            "test:import-cancel-description", cancellation.Token);
        await WaitUntilAsync(() => File.Exists(Path.Combine(parserDirectory, "_fake-pangloss-describe-started")),
            TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        Assert.IsType<PanGlossOutcome.Cancelled>(await run.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(["describe"], FakeParser.Invocations(executable));
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
        Assert.Equal(["--word-timeout-ms", "1500", "--step-cap",
            StepCap.DefaultSteps.ToString(CultureInfo.InvariantCulture), "--threads", "1", "--stats", "--cache", cache], argv[4..]);
        Assert.True(File.Exists(cache));
    }

    [Fact]
    public async Task Batch_WithoutACacheSendsNoStatsFlags()
    {
        var project = Project("batch-plain");
        using var invoker = Invoker();

        await invoker.RunAsync(
            new PanGlossRequest.Batch(project, ["motifa"], TimeSpan.FromSeconds(1)), "test:batch", CancellationToken.None);

        Assert.Equal(["--word-timeout-ms", "1000", "--step-cap",
            StepCap.DefaultSteps.ToString(CultureInfo.InvariantCulture), "--threads", "1"], Argv(project)[4..]);
    }

    [Fact]
    public async Task BatchDoesNotWriteInvocationDiagnosticsBesideAPublishedBaseline()
    {
        var publication = Path.Combine(_root, new string('a', 64));
        Directory.CreateDirectory(publication);
        var project = Path.Combine(publication, "MotifTestProj.fwdata");
        File.WriteAllText(project, "the fake parser never reads this.");
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Batch(project, ["motifa"], TimeSpan.FromSeconds(1)),
            "test:batch-baseline", CancellationToken.None);

        Assert.IsType<PanGlossOutcome.Completed>(outcome);
        Assert.False(File.Exists(Path.Combine(publication, "_pangloss-argv.json")));
        Assert.False(File.Exists(Path.Combine(publication, "_pangloss-environment.json")));
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
    public async Task Batch_ExplicitStepBudgetIsIndependentOfTheTimeLimit()
    {
        var project = Project("batch-budget");
        using var invoker = Invoker();

        await invoker.RunAsync(
            new PanGlossRequest.Batch(project, ["motifa"], TimeSpan.FromMilliseconds(700), PerWordStepLimit: 123),
            "test:batch-budget", CancellationToken.None);

        Assert.Equal(["--word-timeout-ms", "700", "--step-cap", "123", "--threads", "1"], Argv(project)[4..]);
    }

    [Fact]
    public async Task TryAWordTraceSendsNeitherPerWordLimit()
    {
        var project = Project("trace-unbounded");
        using var invoker = Invoker();

        await invoker.RunAsync(new PanGlossRequest.Trace(project, "motifa"),
            "test:trace", CancellationToken.None, Timeout.InfiniteTimeSpan);

        Assert.Equal(["parse", project, "motifa", "--trace", "--trace-format", "json", "--trace-details"],
            Argv(project));
        Assert.DoesNotContain("--step-cap", Argv(project));
        Assert.DoesNotContain("--word-timeout-ms", Argv(project));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task Batch_RejectsNegativeStepBudgetsBeforeInvocation(int stepLimit)
    {
        var project = Project("batch-invalid-budget");
        using var invoker = Invoker();

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => invoker.RunAsync(
            new PanGlossRequest.Batch(project, ["motifa"], TimeSpan.FromSeconds(1), PerWordStepLimit: stepLimit),
            "test:batch-invalid-budget", CancellationToken.None));

        Assert.Equal("steps", exception.ParamName);
        Assert.False(File.Exists(Path.Combine(_root, "_pangloss-argv.json")));
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
        using var invoker = new PanGlossInvoker(Path.Combine(_root, "no-such-" + FakeParser.ExecutableFileName), queue);

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
            wallClockCap: TimeSpan.FromSeconds(3));

        var timedOut = Assert.IsType<PanGlossOutcome.TimedOut>(outcome);
        Assert.Equal(TimeSpan.FromSeconds(3), timedOut.Cap);
        await AssertStoppedTicking(heartbeat);
    }

    [Fact]
    public async Task CancellationStopsTheParser_AndReportsCancelled()
    {
        var project = Project("cancel");
        var heartbeat = Path.Combine(_root, "heartbeat.txt");
        FakeParser.Behave(_root, new { heartbeatPath = heartbeat });
        using var invoker = Invoker();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var run = invoker.RunAsync(
            new PanGlossRequest.Import(project, Path.Combine(_root, "g.json")), "test:cancel", cts.Token);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(heartbeat) && !run.IsCompleted && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        await cts.CancelAsync();
        var outcome = await run;

        Assert.IsType<PanGlossOutcome.Cancelled>(outcome);
        await AssertStoppedTicking(heartbeat);
    }

    [Fact]
    public async Task CancellingBatchWaitsForArtifactHandlesToCloseBeforeCleanup()
    {
        var project = Project("cancel-batch");
        var heartbeat = Path.Combine(_root, "heartbeat.txt");
        FakeParser.Behave(_root, new { heartbeatPath = heartbeat });
        using var invoker = Invoker();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var run = invoker.RunAsync(new PanGlossRequest.Batch(project, ["motifa"], TimeSpan.FromSeconds(1)),
            "test:cancel-batch", cancellation.Token);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(heartbeat) && !run.IsCompleted && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(File.Exists(heartbeat));
        var wordsPath = Argv(project)[2];
        Assert.Throws<IOException>(() => File.Open(wordsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
        await cancellation.CancelAsync();
        Assert.IsType<PanGlossOutcome.Cancelled>(await run);
        Assert.False(Directory.Exists(Path.GetDirectoryName(wordsPath)));
    }

    [Fact]
    public async Task ABatchWithoutAWordTimeLimitHasNoWallClockDeadlineAndStillCancels()
    {
        var project = Project("batch-without-time-limit");
        var heartbeat = Path.Combine(_root, "unlimited-batch-heartbeat.txt");
        FakeParser.Behave(_root, new { heartbeatPath = heartbeat });
        var request = new PanGlossRequest.Batch(project, ["motifa"], PerWordLimit: null);
        using var invoker = Invoker();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        Assert.Equal(Timeout.InfiniteTimeSpan, PanGlossInvoker.ResolveWallClockCap(request, null));
        Assert.Equal(PanGlossInvoker.DefaultWallClockCap,
            PanGlossInvoker.ResolveWallClockCap(new PanGlossRequest.Batch(project, ["motifa"], TimeSpan.FromSeconds(1)), null));
        var run = invoker.RunAsync(request, "test:unlimited-batch", cancellation.Token);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(heartbeat) && !run.IsCompleted && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(File.Exists(heartbeat));

        await cancellation.CancelAsync();

        Assert.IsType<PanGlossOutcome.Cancelled>(await run);
        await AssertStoppedTicking(heartbeat);
    }

    [Fact]
    public async Task EveryLaunchRunsInsideTheAdmittedJobObject()
    {
        var project = Project("contained");
        var heartbeat = Path.Combine(_root, "heartbeat.txt");
        FakeParser.Behave(_root, new { heartbeatPath = heartbeat });
        using var queue = NewQueue();
        PanGlossContainmentJob? admitted = null;
        queue.JobAdmitted = job => admitted = job;
        using var invoker = new PanGlossInvoker(FakeParser.ExecutablePath, queue);
        using var cts = new CancellationTokenSource();

        var run = invoker.RunAsync(
            new PanGlossRequest.Import(project, Path.Combine(_root, "g.json")), "test:contained", cts.Token);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && (admitted is null || !File.Exists(heartbeat)))
            await Task.Delay(20);

        Assert.NotNull(admitted);
        Assert.True(File.Exists(heartbeat), "The parser did not start under its admitted containment job.");
        Assert.NotNull(admitted!.Report);
        if (OperatingSystem.IsWindows())
            Assert.True(Assert.IsType<WindowsCpuJob>(admitted).QueryTotalProcessCount() >= 1,
                "No process was assigned to the admitted job.");
        else
            Assert.Contains("process group", admitted.Report.ProcessTree, StringComparison.OrdinalIgnoreCase);
        cts.Cancel();
        Assert.IsType<PanGlossOutcome.Cancelled>(await run);
    }

    private PanGlossInvoker Invoker() => new(FakeParser.ExecutablePath, NewQueue());

    private static void AppendBytes(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        stream.Write(bytes);
    }

    [Theory]
    [InlineData("source.fwdata")]
    [InlineData("words.txt")]
    public async Task CapturedBatchRefusesChangedStagedInputAndRemovesUnpublishedArtifacts(string changedFile)
    {
        var source = Project("changed");
        var artifacts = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        using var containment = PanGlossContainment.CreateJob();
        var outcome = await PanGlossInvoker.LaunchAsync(FakeParser.ExecutablePath,
            new PanGlossRequest.Batch(source, ["motifa"], TimeSpan.FromSeconds(1), ArtifactDirectory: artifacts),
            containment,
            TimeSpan.FromSeconds(10), CancellationToken.None,
            _ => File.WriteAllText(Path.Combine(artifacts, changedFile), "changed while parsing"));

        var refused = Assert.IsType<PanGlossOutcome.Incomplete>(outcome);
        Assert.Contains("changed during", refused.Detail, StringComparison.Ordinal);
        Assert.False(Directory.Exists(artifacts));
        Assert.Equal("the fake parser never reads this.", File.ReadAllText(source));
    }

    [Fact]
    public async Task CapturedBatchRetainsExactInputsAndArtifactsAfterTheInvokerReturns()
    {
        var source = Project("captured");
        var artifacts = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(
            source, ["motifa"], TimeSpan.FromMilliseconds(700), PerWordStepLimit: 123,
            ArtifactDirectory: artifacts), "test:capture", CancellationToken.None);

        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        var evidence = Assert.IsType<BatchInvocationEvidence>(completed.BatchEvidence);
        Assert.NotEqual(source, evidence.SourcePath);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(evidence.SourcePath));
        Assert.Equal(BatchInvocationEvidence.DigestFile(source), evidence.SourceBytesSha256);
        Assert.Equal(BatchInvocationEvidence.DigestFile(FakeParser.ExecutablePath), evidence.ExecutableBytesSha256);
        Assert.Equal(BatchInvocationEvidence.DigestFile(evidence.WordsPath), evidence.WordsSha256);
        Assert.Equal(BatchInvocationEvidence.DigestFile(evidence.TsvPath), evidence.TsvSha256);
        Assert.Equal(completed.Output, File.ReadAllText(evidence.TsvPath));
        Assert.Equal(completed.StandardError, File.ReadAllText(evidence.StandardErrorPath));
        Assert.Equal(700, evidence.PerWordTimeoutMs);
        Assert.Equal(new SIL.Motif.Contract.Assess.StepCap(123), evidence.PerWordStepLimit);
        Assert.Equal(1, evidence.Threads);
        Assert.False(evidence.CollectStatistics);
        File.WriteAllText(source, "changed after invocation");
        Assert.Equal(evidence.SourceBytesSha256, BatchInvocationEvidence.DigestFile(evidence.SourcePath));
    }

    [Fact]
    public async Task CapturedBatchRefusesToOverwriteAnExistingArtifactDirectory()
    {
        var source = Project("existing");
        var artifacts = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifacts);
        var sentinel = Path.Combine(artifacts, "keep.txt");
        File.WriteAllText(sentinel, "existing evidence");
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(
            source, ["motifa"], TimeSpan.FromSeconds(1), ArtifactDirectory: artifacts),
            "test:existing", CancellationToken.None);

        Assert.IsType<PanGlossOutcome.Unavailable>(outcome);
        Assert.Equal("existing evidence", File.ReadAllText(sentinel));
    }

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

    private static string[] EnvironmentNames(string besidePath) =>
        JsonSerializer.Deserialize<string[]>(
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(besidePath)!, "_pangloss-environment.json")))!;

    private static async Task AssertStoppedTicking(string heartbeat)
    {
        Assert.True(File.Exists(heartbeat), "The fake parser never started ticking.");
        await Task.Delay(200);
        var before = File.ReadAllText(heartbeat);
        await Task.Delay(250);
        Assert.Equal(before, File.ReadAllText(heartbeat));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The parser condition was not met.");
            await Task.Delay(20);
        }
    }
}
