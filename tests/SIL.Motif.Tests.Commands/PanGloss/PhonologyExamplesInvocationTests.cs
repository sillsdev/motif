using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

public sealed class PhonologyExamplesInvocationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-phonology-" + Guid.NewGuid().ToString("N"));
    private readonly string _executable;
    private readonly string _grammar;
    private static readonly PhonologyLimits Limits = new(101, 1001, 5000);
    private static readonly PhonologyGenerationContext Context = new PhonologyGenerationContext.FwdataEntry(
        "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", PhonologyMsaClass.MoStemMsa);

    public PhonologyExamplesInvocationTests()
    {
        Directory.CreateDirectory(_root);
        _executable = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser café 空"));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(_executable)!, "_fake-pangloss-phonology-v1"), string.Empty);
        var source = Path.Combine(_root, "source café 空");
        Directory.CreateDirectory(source);
        _grammar = Path.Combine(source, "grammar café 空.fwdata");
        File.WriteAllText(_grammar, "{\"surfaces\":{\"m+baya\":\"mbaya\",\"café\":\"café\"}}");
        Directory.CreateDirectory(Path.Combine(source, "WritingSystemStore"));
        File.WriteAllText(Path.Combine(source, "WritingSystemStore", "swh.ldml"), "<ldml/>");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private PanGlossRequest.PhonologyExamples Request(IReadOnlyList<PhonologyExample>? examples = null) =>
        new(_grammar, examples ?? [new PhonologyExample("nasal", "m+baya", ["mbaya"], Context,
            new CitationView("Published grammar", "P2 §4"))], Limits);

    private void Configure(object settings) => File.WriteAllText(
        Path.Combine(Path.GetDirectoryName(_executable)!, "_phonology-behaviour.json"), JsonSerializer.Serialize(settings));

    private async Task<PanGlossOutcome> Run(PanGlossRequest.PhonologyExamples? request = null)
    {
        using var invoker = new PanGlossInvoker(_executable);
        return await invoker.RunAsync(request ?? Request(), "test:phonology", CancellationToken.None, TimeSpan.FromSeconds(30));
    }

    private string Captured(string name) => File.ReadAllText(Path.Combine(Path.GetDirectoryName(_executable)!, "_phonology-" + name + ".json"));
    private static PhonologyMeasurement Measurement(PanGlossOutcome outcome)
    {
        Assert.True(outcome is PanGlossOutcome.Completed, outcome.Message);
        return Assert.IsType<PhonologyMeasurement>(((PanGlossOutcome.Completed)outcome).Phonology);
    }

    [Fact]
    public async Task TypedInvocationBindsPrivateBytesAndPreservesFileIdentityAndCitationOnlyInAuthorInput()
    {
        var grammarBytes = File.ReadAllBytes(_grammar);
        var executableHash = PhonologyInvocationInputs.DigestFile(_executable);
        var outcome = await Run();
        var measurement = Measurement(outcome);
        var argv = JsonSerializer.Deserialize<string[]>(Captured("argv"))!;
        Assert.Equal(4, argv.Length);
        Assert.Equal("phonology-examples", argv[0]);
        Assert.NotEqual(_grammar, argv[1]);
        Assert.Equal(Path.GetFileName(_grammar), Path.GetFileName(argv[1]));
        Assert.Equal("inputs", Path.GetFileName(Path.GetDirectoryName(argv[1])));
        Assert.Equal("request.json", Path.GetFileName(argv[2]));
        Assert.Equal("result.json", Path.GetFileName(argv[3]));
        Assert.False(Directory.Exists(Path.GetDirectoryName(argv[2])));
        var requestBytes = Encoding.UTF8.GetBytes(Captured("request"));
        using var request = JsonDocument.Parse(requestBytes);
        Assert.Equal(["version", "limits", "examples"], request.RootElement.EnumerateObject().Select(p => p.Name));
        var example = request.RootElement.GetProperty("examples")[0];
        Assert.False(example.TryGetProperty("citation", out _));
        Assert.Equal("m+baya", example.GetProperty("underlying").GetString());
        Assert.Equal("mbaya", example.GetProperty("expected")[0].GetString());
        Assert.Equal(PhonologyInvocationInputs.Digest(grammarBytes), measurement.GrammarSha256);
        Assert.Equal(PhonologyInvocationInputs.Digest(requestBytes), measurement.RequestFileSha256);
        Assert.Equal(executableHash, measurement.ExecutableSha256);
        Assert.Equal(PhonologyInvocationInputs.Digest(Encoding.UTF8.GetBytes(Captured("result"))), measurement.OutputSha256);
        Assert.Equal("fake-phonology/v1", measurement.ProducerBuild);
        Assert.Equal(new string('a', 64), measurement.ProducerRequestSha256);
        var dependency = Assert.Single(measurement.GrammarDependencies);
        Assert.Equal("WritingSystemStore/swh.ldml", dependency.Path);
        Assert.Equal(PhonologyInvocationInputs.Digest(Encoding.UTF8.GetBytes("<ldml/>")), dependency.Sha256);
        Assert.Equal(grammarBytes, File.ReadAllBytes(_grammar));
        Assert.Equal(["describe", "phonology-examples"], FakeParser.Invocations(_executable));
    }

    [Fact]
    public async Task OriginalChangesAfterStagingDoNotRebindProducerEvidence()
    {
        var before = PhonologyInvocationInputs.DigestFile(_grammar);
        Configure(new { mode = "mutateOriginal", originalPath = _grammar });
        var measurement = Measurement(await Run());
        Assert.Equal(before, measurement.GrammarSha256);
        Assert.NotEqual(before, PhonologyInvocationInputs.DigestFile(_grammar));
    }

    [Fact]
    public async Task ConcurrentInvocationsOfOneRequestHaveIndependentFrozenInputs()
    {
        var other = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "other parser"));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(other)!, "_fake-pangloss-phonology-v1"), string.Empty);
        var expected = new List<string> { "mbaya" };
        var request = Request([new PhonologyExample("shared", "m+baya", expected, Context)]);
        using var first = new PanGlossInvoker(_executable);
        using var second = new PanGlossInvoker(other);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var firstRun = first.RunAsync(request, "first", timeout.Token);
        var secondRun = second.RunAsync(request, "second", timeout.Token);
        expected[0] = "changed";
        var results = await Task.WhenAll(firstRun, secondRun);
        Assert.All(results, result => Assert.Equal(["mbaya"], Assert.Single(Measurement(result).Rows).Expected));
        var firstArgs = JsonSerializer.Deserialize<string[]>(Captured("argv"))!;
        var secondArgs = JsonSerializer.Deserialize<string[]>(File.ReadAllText(
            Path.Combine(Path.GetDirectoryName(other)!, "_phonology-argv.json")))!;
        Assert.NotEqual(firstArgs[1], secondArgs[1]);
        Assert.NotEqual(firstArgs[2], secondArgs[2]);
        Assert.NotEqual(firstArgs[3], secondArgs[3]);
    }

    [Fact]
    public async Task HcContextAndUppercaseFwdataKeepTheirExactSupportedWireShapes()
    {
        var hc = new PhonologyGenerationContext.HcXmlEntry("entry 空");
        var hcGrammar = Path.ChangeExtension(_grammar, ".xml");
        File.Copy(_grammar, hcGrammar);
        var request = Request([new PhonologyExample("hc", "m+baya", ["mbaya"], hc)]) with { GrammarPath = hcGrammar };
        var measurement = Measurement(await Run(request));
        Assert.Empty(measurement.GrammarDependencies);
        Assert.Equal(hc, Assert.Single(measurement.Rows).ResolvedContext);
        var upper = Path.Combine(Path.GetDirectoryName(_grammar)!, "Upper.FWDATA");
        File.Copy(_grammar, upper);
        Assert.Single(Measurement(await Run(Request() with { GrammarPath = upper })).GrammarDependencies);
    }

    [Fact]
    public async Task EveryExpectationMustBeObservedForAgreement()
    {
        var measurement = Measurement(await Run(Request([new PhonologyExample("two", "m+baya", ["mbaya", "other"], Context)])));
        var row = Assert.Single(measurement.Rows);
        Assert.Equal(PhonologyCompletion.Complete, row.Completion);
        Assert.Equal(PhonologyDisposition.Disagree, row.Disposition);
        Assert.Equal([true, false], row.ExpectedObserved);
        Assert.Equal("mbaya", row.Observed);
    }

    [Fact]
    public async Task IncompleteRowsRemainUnavailableEvenWithObservedExpectations()
    {
        Configure(new { mode = "incompleteTrueFlags" });
        var row = Assert.Single(Measurement(await Run()).Rows);
        Assert.Equal([true], row.ExpectedObserved);
        Assert.Equal("mbaya", row.Observed);
        Assert.Equal(PhonologyDisposition.Unavailable, row.Disposition);
        Assert.Equal(PhonologyCompletion.Incomplete, row.Completion);
        Assert.Equal(PhonologyReason.StepCap, row.Reason);
        Assert.True(row.Capped);
    }

    [Fact]
    public async Task DeclaredVisitedAndAppliedListsAndMeasuredContextCountRemainDistinct()
    {
        var measurement = Measurement(await Run());
        Assert.Equal(7UL, measurement.MeasuredEntryCount);
        var row = Assert.Single(measurement.Rows);
        Assert.Equal(["first", "unused"], row.OrderedRuleIds);
        Assert.Equal(["first"], row.VisitedRuleIds);
        Assert.Equal(["first"], row.AppliedRuleIds);
        Assert.Equal(1UL, row.Steps);
        Assert.Equal(3UL, row.WorkSteps);
        Assert.Equal(Context, row.ResolvedContext);
    }

    [Fact]
    public async Task ProducerNfdObservationDoesNotRewriteAuthoredStrings()
    {
        var row = Assert.Single(Measurement(await Run(Request([new PhonologyExample("unicode", "café", ["café"], Context)]))).Rows);
        Assert.Equal("café", row.Underlying);
        Assert.Equal(["café"], row.Expected);
        Assert.Equal("cafe\u0301", row.Observed);
        Assert.Equal(PhonologyDisposition.Agree, row.Disposition);
    }

    [Fact]
    public async Task ProducerFlagsCannotContradictDispositionOrCompletion()
    {
        foreach (var set in new[]
        {
            new Dictionary<string, object?> { ["rows/0/expectedObserved/0"] = false },
            new Dictionary<string, object?> { ["rows/0/capped"] = true },
            new Dictionary<string, object?> { ["rows/0/timedOut"] = true },
            new Dictionary<string, object?> { ["rows/0/completion"] = "incomplete", ["rows/0/reason"] = "stepCap" },
            new Dictionary<string, object?> { ["rows/0/completion"] = "notStarted", ["rows/0/reason"] = "invalidShape" },
            new Dictionary<string, object?> { ["rows/0/requestedContext"] = new { kind = "fwdataEntry", entryGuid = "entry", msaClass = "MoStemMsa" } }
        })
        {
            Configure(new { set });
            Assert.IsType<PanGlossOutcome.Incomplete>(await Run());
        }
    }

    [Fact]
    public async Task DependenciesUseProducerUtf8FilenameOrderAndExcludeUncopiedFiles()
    {
        var store = Path.Combine(Path.GetDirectoryName(_grammar)!, "WritingSystemStore");
        File.WriteAllText(Path.Combine(store, "𐀀.ldml"), "supplementary");
        File.WriteAllText(Path.Combine(store, ".ldml"), "private-use");
        File.WriteAllText(Path.Combine(store, "ignored.LDML"), "ignored");
        var measurement = Measurement(await Run());
        Assert.Equal(["WritingSystemStore/swh.ldml", "WritingSystemStore/.ldml", "WritingSystemStore/𐀀.ldml"],
            measurement.GrammarDependencies.Select(d => d.Path));
    }

    [Fact]
    public async Task BalancedRemaindersAndOneDecreasingDeadlineAreValidated()
    {
        var examples = Enumerable.Range(0, 3).Select(i => new PhonologyExample("row" + i, "m+baya", ["mbaya"], Context)).ToArray();
        var rows = Measurement(await Run(Request(examples))).Rows;
        Assert.Equal([34UL, 34UL, 33UL], rows.Select(r => r.EffectiveLimits.StepCap));
        Assert.Equal([334UL, 334UL, 333UL], rows.Select(r => r.EffectiveLimits.WorkCap));
        Assert.Equal([5000U, 4999U, 4998U], rows.Select(r => r.EffectiveLimits.TimeoutMs));
        Assert.Equal(PanGlossInvoker.DefaultWallClockCap + TimeSpan.FromMilliseconds(Limits.TimeoutMs),
            PanGlossInvoker.ResolveWallClockCap(Request(examples), null));
        Configure(new { mode = "zeroTimeout" });
        Assert.All(Measurement(await Run(Request(examples))).Rows, r =>
        {
            Assert.Equal(0U, r.EffectiveLimits.TimeoutMs);
            Assert.Equal(PhonologyReason.Timeout, r.Reason);
            Assert.Equal(PhonologyDisposition.Unavailable, r.Disposition);
        });
    }

    [Theory]
    [InlineData("grammarSha256", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("requestFileSha256", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("grammarDependencies/0/sha256", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("grammarDependencies/0/path", "WritingSystemStore/other.ldml")]
    [InlineData("requestSha256", "SHA256:bad")]
    [InlineData("build", "")]
    [InlineData("rows/0/id", "another")]
    [InlineData("rows/0/underlying", "other")]
    [InlineData("rows/0/expected/0", "other")]
    [InlineData("rows/0/requestedContext/msaGuid", "another")]
    [InlineData("rows/0/resolvedContext/entryGuid", "another")]
    [InlineData("rows/0/requestedContext/kind", "unknown")]
    [InlineData("rows/0/requestedContext/msaClass", "MoInflAffMsa")]
    [InlineData("rows/0/disposition", "unavailable")]
    [InlineData("rows/0/completion", "unknown")]
    [InlineData("rows/0/reason", "unknown")]
    [InlineData("rows/0/reason", "timeout")]
    [InlineData("limits/scope", "parse")]
    [InlineData("limits/timeAllocation", "perRow")]
    [InlineData("rows/0/unknown", "value")]
    [InlineData("rows/0/requestedContext/unknown", "value")]
    [InlineData("unknown", "value")]
    public async Task WrongBindingsAndUnknownWireValuesNeverPublishEvidence(string path, string value)
    {
        Configure(new { set = new Dictionary<string, object?> { [path] = value } });
        Assert.IsType<PanGlossOutcome.Incomplete>(await Run());
    }

    [Theory]
    [InlineData("version", 2)]
    [InlineData("limits/stepCap", 102)]
    [InlineData("limits/workCap", 1002)]
    [InlineData("limits/timeoutMs", 5001)]
    [InlineData("rows/0/effectiveLimits/stepCap", 102)]
    [InlineData("rows/0/effectiveLimits/workCap", 1002)]
    [InlineData("rows/0/effectiveLimits/timeoutMs", 5001)]
    [InlineData("rows/0/steps", 102)]
    [InlineData("rows/0/workSteps", 1002)]
    [InlineData("counts/agree", 2)]
    [InlineData("counts/total", 2)]
    [InlineData("completionCounts/complete", 2)]
    [InlineData("measuredEntryCount", 0)]
    public async Task WrongCountsLimitsAndCountersNeverPublishEvidence(string path, int value)
    {
        Configure(new { set = new Dictionary<string, object?> { [path] = value } });
        Assert.IsType<PanGlossOutcome.Incomplete>(await Run());
    }

    [Theory]
    [InlineData("id")]
    [InlineData("requestedContext")]
    [InlineData("resolvedContext")]
    [InlineData("observed")]
    [InlineData("reason")]
    [InlineData("effectiveLimits")]
    [InlineData("expectedObserved")]
    [InlineData("appliedRuleIds")]
    [InlineData("timedOut")]
    public async Task MissingRequiredFieldsIncludingNullableFactsAreRejected(string field)
    {
        Configure(new { remove = new[] { field } });
        Assert.IsType<PanGlossOutcome.Incomplete>(await Run());
    }

    [Theory]
    [InlineData("noResult")]
    [InlineData("oversized")]
    [InlineData("malformed")]
    [InlineData("duplicate")]
    [InlineData("mutateGrammar")]
    [InlineData("mutateDependency")]
    [InlineData("mutateRequest")]
    public async Task MissingMalformedOrChangedEvidenceIsIncomplete(string mode)
    {
        Configure(new { mode });
        Assert.IsType<PanGlossOutcome.Incomplete>(await Run());
    }

    [Theory]
    [InlineData("build")]
    [InlineData("grammarDependencies")]
    [InlineData("rows/0/requestedContext")]
    [InlineData("rows/0/resolvedContext")]
    [InlineData("rows/0/expected")]
    [InlineData("rows/0/expectedObserved")]
    [InlineData("rows/0/effectiveLimits")]
    [InlineData("rows/0/orderedRuleIds")]
    [InlineData("counts")]
    [InlineData("completionCounts")]
    public async Task NullRequiredValuesCannotBecomeCompletedEvidence(string field)
    {
        Configure(new { set = new Dictionary<string, object?> { [field] = null } });
        Assert.IsType<PanGlossOutcome.Incomplete>(await Run());
    }

    [Fact]
    public async Task MissingDuplicateOrReorderedRowsAreRejected()
    {
        foreach (var rows in new object[] { Array.Empty<object>(), new object[] { null! } })
        {
            Configure(new { set = new Dictionary<string, object?> { ["rows"] = rows } });
            Assert.IsType<PanGlossOutcome.Incomplete>(await Run());
        }
        var examples = new[] { new PhonologyExample("first", "m+baya", ["mbaya"], Context),
            new PhonologyExample("second", "m+baya", ["mbaya"], Context) };
        Configure(new { set = new Dictionary<string, object?> { ["rows/1/id"] = "first" } });
        Assert.IsType<PanGlossOutcome.Incomplete>(await Run(Request(examples)));
        Configure(new { set = new Dictionary<string, object?> { ["rows/0/id"] = "second", ["rows/1/id"] = "first" } });
        Assert.IsType<PanGlossOutcome.Incomplete>(await Run(Request(examples)));
    }

    [Fact]
    public async Task RuleAndObservedBoundsAreEnforced()
    {
        foreach (var set in new[]
        {
            new Dictionary<string, object?> { ["rows/0/orderedRuleIds"] = Enumerable.Repeat("rule", 1025).ToArray() },
            new Dictionary<string, object?> { ["rows/0/appliedRuleIds/0"] = new string('r', 257) },
            new Dictionary<string, object?> { ["rows/0/observed"] = string.Concat(Enumerable.Repeat("🙂", 513)) },
            new Dictionary<string, object?> { ["rows/0/expectedObserved"] = Array.Empty<bool>() }
        })
        {
            Configure(new { set });
            Assert.IsType<PanGlossOutcome.Incomplete>(await Run());
        }
        var examples = Enumerable.Range(0, 2).Select(i => new PhonologyExample("row" + i, "m+baya", ["mbaya"], Context)).ToArray();
        var aggregate = new Dictionary<string, object?>();
        foreach (var row in new[] { "0", "1" })
            foreach (var list in new[] { "orderedRuleIds", "visitedRuleIds", "appliedRuleIds" })
                aggregate[$"rows/{row}/{list}"] = Enumerable.Repeat("rule", 1024).ToArray();
        Configure(new { set = aggregate });
        Assert.IsType<PanGlossOutcome.Incomplete>(await Run(Request(examples)));
    }

    [Fact]
    public async Task MissingProducerAndOldCapabilityPreserveOrdinaryRequests()
    {
        using var absent = new PanGlossInvoker(null);
        Assert.IsType<PanGlossOutcome.Unavailable>(await absent.RunAsync(Request(), "absent", CancellationToken.None));
        FakeParser.OmitDescribeEntries(_executable, "phonology-examples");
        using var invoker = new PanGlossInvoker(_executable);
        Assert.IsType<PanGlossOutcome.Unavailable>(await invoker.RunAsync(Request(), "old", CancellationToken.None));
        var batch = await invoker.RunAsync(new PanGlossRequest.Batch(_grammar, ["motifa"], TimeSpan.FromSeconds(1)), "ordinary", CancellationToken.None);
        Assert.True(batch is PanGlossOutcome.Completed, batch.Message);
        Assert.Equal(["describe", "batch"], FakeParser.Invocations(_executable));
    }

    [Fact]
    public async Task PhonologyCancellationAndTimeoutUseSharedContainment()
    {
        var started = Path.Combine(_root, "started");
        Configure(new { mode = "hang", startedPath = started });
        using var invoker = new PanGlossInvoker(_executable);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var pending = invoker.RunAsync(Request(), "cancel", cancellation.Token);
        var clock = Stopwatch.StartNew();
        while (!File.Exists(started) && !pending.IsCompleted && clock.Elapsed < TimeSpan.FromSeconds(20))
            await Task.Delay(20);
        Assert.True(File.Exists(started), "The contained producer did not start.");
        cancellation.Cancel();
        var cancelled = await pending.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.IsType<PanGlossOutcome.Cancelled>(cancelled);
        Assert.NotNull(cancelled.Containment);
        Assert.IsType<PanGlossOutcome.TimedOut>(await invoker.RunAsync(Request(), "timeout", CancellationToken.None, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task ChangedExecutableBytesAreRefused()
    {
        if (OperatingSystem.IsWindows()) return;
        Configure(new { mode = "changeExecutable" });
        var outcome = await Run();
        Assert.IsType<PanGlossOutcome.Incomplete>(outcome);
        Assert.Contains("executable changed", outcome.Message);
    }

    [Fact]
    public void GenuineProducerFixturesRetainExactRawBindingsAndCounterfactualFacts()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var fixtures = Path.Combine(directory.FullName, "tests", "SIL.Motif.Tests.Commands", "PanGloss", "Fixtures", "Phonology");
        var bindings = JsonNode.Parse(File.ReadAllText(Path.Combine(fixtures, "bindings.json")))!.AsArray();
        foreach (var binding in bindings)
        {
            var name = binding!["name"]!.GetValue<string>();
            var bytes = File.ReadAllBytes(Path.Combine(fixtures, name + ".json"));
            Assert.Equal(binding["sha256"]!.GetValue<string>(), PhonologyInvocationInputs.Digest(bytes));
            Assert.Equal(binding["bytes"]!.GetValue<int>(), bytes.Length);
            var requestBytes = File.ReadAllBytes(Path.Combine(fixtures,
                name == "capped-result" ? "capped-request.json" : "request.json"));
            Assert.Equal(binding["requestFileSha256"]!.GetValue<string>(), PhonologyInvocationInputs.Digest(requestBytes));
            using var authored = JsonDocument.Parse(requestBytes);
            var examples = authored.RootElement.GetProperty("examples").Deserialize<PhonologyExample[]>(PhonologyJson.CreateOptions())!;
            var limits = authored.RootElement.GetProperty("limits").Deserialize<PhonologyLimits>(PhonologyJson.CreateOptions())!;
            var dependencies = binding["grammarDependencies"]!.AsArray().Select(d => new PhonologyGrammarDependency(
                d!["path"]!.GetValue<string>(), d["sha256"]!.GetValue<string>())).ToArray();
            var inputs = new PhonologyInvocationInputs("unused", binding["grammarSha256"]!.GetValue<string>(),
                Array.AsReadOnly(dependencies), "unused", binding["requestFileSha256"]!.GetValue<string>(), new string('e', 64));
            var measurement = PhonologyExamplesReader.Read(bytes, examples, limits, inputs);
            Assert.Equal(binding["build"]!.GetValue<string>(), measurement.ProducerBuild);
            Assert.Equal(binding["producerRequestSha256"]!.GetValue<string>(), measurement.ProducerRequestSha256);
            Assert.Equal(binding["counts"]!["agree"]!.GetValue<int>(), measurement.Rows.Count(r => r.Disposition == PhonologyDisposition.Agree));
            Assert.Equal(binding["counts"]!["disagree"]!.GetValue<int>(), measurement.Rows.Count(r => r.Disposition == PhonologyDisposition.Disagree));
            Assert.Equal(binding["counts"]!["unavailable"]!.GetValue<int>(), measurement.Rows.Count(r => r.Disposition == PhonologyDisposition.Unavailable));
            Assert.Equal(binding["completionCounts"]!["complete"]!.GetValue<int>(), measurement.Rows.Count(r => r.Completion == PhonologyCompletion.Complete));
            Assert.Equal(binding["completionCounts"]!["incomplete"]!.GetValue<int>(), measurement.Rows.Count(r => r.Completion == PhonologyCompletion.Incomplete));
            Assert.Equal(binding["completionCounts"]!["notStarted"]!.GetValue<int>(), measurement.Rows.Count(r => r.Completion == PhonologyCompletion.NotStarted));
            if (name == "capped-result") Assert.Equal([1UL, 0UL, 0UL, 0UL, 0UL], measurement.Rows.Select(r => r.EffectiveLimits.StepCap));
            if (name == "empty-lexicon-result") Assert.Equal(0UL, measurement.MeasuredEntryCount);
            else Assert.Equal(10UL, measurement.MeasuredEntryCount);
        }
    }

    [Fact]
    public void TypedInputsAreBoundedAndCopiedWithoutNormalization()
    {
        var expected = new List<string> { "café" };
        var examples = new List<PhonologyExample> { new("unicode", "café", expected, Context) };
        var frozen = PhonologyInvocationInputs.Freeze(Request(examples));
        expected[0] = "changed"; examples.Clear();
        Assert.Equal(["café"], Assert.Single(frozen.Examples).Expected);
        foreach (var limits in new[] { new PhonologyLimits(0, 1, 1), new(50_000_001, 1, 1), new(1, 5_000_000_001, 1), new(1, 1, 600_001) })
            Assert.Throws<ArgumentOutOfRangeException>(() => PhonologyInvocationInputs.Freeze(Request() with { Limits = limits }));
        foreach (var example in new[] { new PhonologyExample(new string('i', 129), "m+baya", ["mbaya"], Context),
            new("row", new string('x', 257), ["mbaya"], Context), new("row", "x", Enumerable.Repeat("x", 17).ToArray(), Context),
            new("row", "\uD800", ["mbaya"], Context) })
            Assert.Throws<ArgumentException>(() => PhonologyInvocationInputs.Freeze(Request([example])));
        Assert.Throws<ArgumentException>(() => PhonologyInvocationInputs.Freeze(Request([])));
        Assert.Throws<ArgumentException>(() => PhonologyInvocationInputs.Freeze(Request(Enumerable.Repeat(Request().Examples[0], 501).ToArray())));
        Assert.Throws<ArgumentException>(() => PhonologyInvocationInputs.Freeze(Request([Request().Examples[0], Request().Examples[0]])));
    }
}
