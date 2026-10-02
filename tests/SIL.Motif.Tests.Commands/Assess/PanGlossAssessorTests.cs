using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Assess;

[Collection(LcmCacheTestCollection.Name)]
public sealed class PanGlossAssessorTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-assessor-" + Guid.NewGuid().ToString("N"));
    private readonly string _candidate;
    private readonly StatsCacheStore _paths;

    public PanGlossAssessorTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        _candidate = Path.Combine(_root, "candidate");
        Directory.CreateDirectory(_candidate);
        File.WriteAllText(Path.Combine(_candidate, "project.fwdata"), "fake grammar bytes");
        _paths = new StatsCacheStore(WorkspaceOwnership.Bootstrap(Path.Combine(_root, "worker")));
    }

    [Fact]
    public async Task DefaultCollectionSharesOneCapturedBatchWithoutInventedReportMetadata()
    {
        using var invoker = RealFakeInvoker();
        var assessor = new PanGlossAssessor(_paths, invoker);
        var produced = await assessor.ProduceAsync(Scope(), _candidate, CancellationToken.None);

        Assert.Equal(new[] { AssessmentKind.ParseTime, AssessmentKind.ObjectTiming }, produced.Select(p => p.Kind));
        var timing = produced[0];
        var statistics = produced[1];
        var evidence = Assert.IsType<BatchInvocationEvidence>(timing.Invocation);
        Assert.Equal(evidence, statistics.Invocation);
        Assert.Equal(BatchInvocationEvidence.DigestFile(evidence.SourcePath), timing.GrammarSourceSha256);
        Assert.Equal(new SIL.Motif.Contract.Assess.StepCap(123), evidence.PerWordStepLimit);
        Assert.Equal(700, evidence.PerWordTimeoutMs);
        Assert.True(evidence.CollectStatistics);
        Assert.All(produced, item =>
        {
            Assert.Null(item.OutcomeDigest);
            Assert.Null(item.SemanticDigest);
            Assert.Null(item.ModelFingerprint);
            Assert.Null(item.Pipeline);
            Assert.Null(item.DiagnosticCount);
        });
        var raw = Assert.IsType<AssessmentRaw.Batch>(timing.Raw);
        Assert.Equal("motifa", Assert.Single(raw.Analysis.Words).Word);
        var cache = Assert.IsType<AssessmentRaw.FileCache>(statistics.Raw);
        Assert.Equal(BatchInvocationEvidence.DigestFile(cache.Path), cache.Digest);
        Assert.Equal(Path.GetDirectoryName(evidence.SourcePath), Path.GetDirectoryName(cache.Path));
    }

    [Fact]
    public async Task LazyAssessorForwardsTheStreamingCallback()
    {
        var expected = new TrialWordProgress(1, 2, "two")
        {
            StoppedWords = [new StoppedParseWord("one", "TIMEOUT", 700)],
            SlowestWord = new ParseWordTiming("one", 700),
        };
        var inner = new FakeAssessor("pangloss", [AssessmentKind.ParseTime])
        {
            EmitProgress = publish => publish(expected),
        };
        var lazy = new LazyPanGlossAssessor(() => inner);
        TrialWordProgress? observed = null;

        await lazy.ProduceAsync(Scope(AssessmentKind.ParseTime), _candidate,
            progress => observed = progress, CancellationToken.None);

        Assert.Equal(expected, observed);
    }

    [Fact]
    public async Task FlushedRowsReachTheAssessorBeforeTheNextWordIsReleased()
    {
        var release = Path.Combine(_root, "assessor-progress-release");
        var words = new[] { "fast", "capped", "timed-out", "held" };
        var parser = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "progress-parser"));
        FakeParser.BehaveBesideExecutable(parser, new
        {
            streamProgress = true,
            holdEachWordUntil = release,
            words = new[]
            {
                new { word = "fast", outcome = "complete", elapsedMs = 2500 },
                new { word = "capped", outcome = "capped", elapsedMs = 54000 },
                new { word = "timed-out", outcome = "timed-out", elapsedMs = 55000 },
                new { word = "held", outcome = "complete", elapsedMs = 12 },
            },
        });
        using var invoker = RealFakeInvoker(parser);
        var assessor = new PanGlossAssessor(_paths, invoker);
        var scope = new AssessmentScope(words, [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming],
            TimeSpan.FromMilliseconds(700), 123);
        var started = new TaskCompletionSource<TrialWordProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstFinished = new TaskCompletionSource<TrialWordProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedRows = new TaskCompletionSource<TrialWordProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = assessor.ProduceAsync(scope, _candidate, progress =>
        {
            if (progress is { Completed: 0, CurrentWord: "fast" }) started.TrySetResult(progress);
            if (progress is { Completed: 1, CurrentWord: "capped" }) firstFinished.TrySetResult(progress);
            if (progress is { Completed: 3, CurrentWord: "held" }) stoppedRows.TrySetResult(progress);
        }, CancellationToken.None);

        try
        {
            Assert.Equal(new TrialWordProgress(0, 4, "fast"), await started.Task.WaitAsync(TimeSpan.FromSeconds(15)));
            File.WriteAllText(release + ".0", string.Empty);
            var afterFirst = await firstFinished.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(1, afterFirst.Completed);
            Assert.Equal("capped", afterFirst.CurrentWord);
            File.WriteAllText(release + ".1", string.Empty);
            File.WriteAllText(release + ".2", string.Empty);
            var afterStopped = await stoppedRows.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("held", afterStopped.CurrentWord);
            Assert.Equal(new[]
            {
                new StoppedParseWord("capped", "CAP", 54000),
                new StoppedParseWord("timed-out", "TIMEOUT", 55000),
            }, afterStopped.StoppedWords);
            Assert.Equal(new ParseWordTiming("timed-out", 55000), afterStopped.SlowestWord);
            File.WriteAllText(release + ".3", string.Empty);
            await run.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Single(FakeParser.Invocations(parser), command => command == "batch");
        }
        finally
        {
            for (var index = 0; index < words.Length; index++) File.WriteAllText(release + "." + index, string.Empty);
            if (!run.IsCompleted)
                await run.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    [Fact]
    public async Task RepeatedRunsKeepEarlierArtifactsUnchanged()
    {
        using var invoker = RealFakeInvoker();
        var assessor = new PanGlossAssessor(_paths, invoker);
        var first = await assessor.ProduceAsync(Scope(), _candidate, CancellationToken.None);
        var firstCache = Assert.IsType<AssessmentRaw.FileCache>(first[1].Raw);
        var firstBytes = File.ReadAllBytes(firstCache.Path);
        var second = await assessor.ProduceAsync(Scope(), _candidate, CancellationToken.None);

        Assert.NotEqual(first[0].Invocation!.InvocationId, second[0].Invocation!.InvocationId);
        Assert.NotEqual(firstCache.Path, Assert.IsType<AssessmentRaw.FileCache>(second[1].Raw).Path);
        Assert.Equal(firstBytes, File.ReadAllBytes(firstCache.Path));
        Assert.Equal(firstCache.Digest, BatchInvocationEvidence.DigestFile(firstCache.Path));
    }

    [Fact]
    public async Task CorrectnessReleasesRetainedSourceBeforeHashingIt()
    {
        var project = _pristine.CopyProjectFile();
        using var invoker = RealFakeInvoker();
        var assessor = new PanGlossAssessor(_paths, invoker, path =>
        {
            Assert.False(File.Exists(path + ".lock"), "The source cache is still open while its bytes are hashed.");
            return BatchInvocationEvidence.DigestFile(path);
        });

        var produced = await assessor.ProduceAsync(
            Scope(AssessmentKind.Correctness), Path.GetDirectoryName(project)!, CancellationToken.None);

        var correctness = Assert.Single(produced);
        Assert.Equal(AssessmentKind.Correctness, correctness.Kind);
        Assert.Equal(BatchInvocationEvidence.DigestFile(correctness.Invocation!.SourcePath),
            correctness.GrammarSourceSha256);
    }

    [Fact]
    public async Task CorrectnessRequiresRetainedInvocationEvidence()
    {
        var invoker = new FakeInvoker();
        var assessor = new PanGlossAssessor(_paths, invoker);
        Assert.Contains(AssessmentKind.Correctness, assessor.SupportedKinds);
        var refusal = await Assert.ThrowsAsync<AssessorUnavailableException>(() =>
            assessor.ProduceAsync(Scope(AssessmentKind.Correctness), _candidate, CancellationToken.None));
        Assert.Contains("evidence", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(invoker.Requests);
    }

    [Fact]
    public async Task MalformedMorphologyIsARefusalAndUnpublishedArtifactsAreRemoved()
    {
        using var real = RealFakeInvoker();
        var invoker = new CorruptingInvoker(real);
        var assessor = new PanGlossAssessor(_paths, invoker);
        var refusal = await Assert.ThrowsAsync<AssessorUnavailableException>(() =>
            assessor.ProduceAsync(Scope(AssessmentKind.ParseTime), _candidate, CancellationToken.None));
        Assert.Contains("morphology", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(invoker.ArtifactDirectory));
    }

    [Fact]
    public async Task ChangedTsvArtifactIsRefusedAndUnpublishedArtifactsAreRemoved()
    {
        using var real = RealFakeInvoker();
        var invoker = new TsvMutatingInvoker(real);
        var assessor = new PanGlossAssessor(_paths, invoker);

        try
        {
            var refusal = await Assert.ThrowsAsync<AssessorUnavailableException>(() =>
                assessor.ProduceAsync(Scope(), _candidate, CancellationToken.None));

            Assert.Contains("TSV", refusal.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(invoker.ArtifactDirectory));
        }
        finally
        {
            if (Directory.Exists(invoker.ArtifactDirectory))
                Directory.Delete(invoker.ArtifactDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletedPayloadMustMatchItsRetainedArtifact(bool mutateTsv)
    {
        using var real = RealFakeInvoker();
        var invoker = new PayloadMutatingInvoker(real, mutateTsv);
        var assessor = new PanGlossAssessor(_paths, invoker);

        try
        {
            var refusal = await Assert.ThrowsAsync<AssessorUnavailableException>(() =>
                assessor.ProduceAsync(Scope(), _candidate, CancellationToken.None));

            Assert.Contains(mutateTsv ? "TSV" : "morphology", refusal.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(invoker.ArtifactsMatchEvidence);
            Assert.False(Directory.Exists(invoker.ArtifactDirectory));
        }
        finally
        {
            if (Directory.Exists(invoker.ArtifactDirectory))
                Directory.Delete(invoker.ArtifactDirectory, recursive: true);
        }
    }

    private sealed class CorruptingInvoker(IPanGlossInvoker inner) : IPanGlossInvoker
    {
        public string? ArtifactDirectory { get; private set; }
        public async Task<PanGlossOutcome> RunAsync(PanGlossRequest request, string label,
            CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
        {
            var result = Assert.IsType<PanGlossOutcome.Completed>(await inner.RunAsync(request, label, cancellationToken, wallClockCap));
            ArtifactDirectory = Path.GetDirectoryName(result.BatchEvidence!.AnalysesPath);
            const string malformed = "{\"schema\":\"obsolete\"}";
            File.WriteAllText(result.BatchEvidence.AnalysesPath!, malformed);
            return result with
            {
                MorphologyOutput = malformed,
                BatchEvidence = result.BatchEvidence with
                { AnalysesSha256 = BatchInvocationEvidence.DigestFile(result.BatchEvidence.AnalysesPath!) },
            };
        }
    }

    private sealed class TsvMutatingInvoker(IPanGlossInvoker inner) : IPanGlossInvoker
    {
        public string? ArtifactDirectory { get; private set; }

        public async Task<PanGlossOutcome> RunAsync(PanGlossRequest request, string label,
            CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
        {
            var completed = Assert.IsType<PanGlossOutcome.Completed>(
                await inner.RunAsync(request, label, cancellationToken, wallClockCap));
            var evidence = completed.BatchEvidence!;
            ArtifactDirectory = Path.GetDirectoryName(evidence.TsvPath)!;
            File.AppendAllText(evidence.TsvPath, "mutated after invocation");
            return completed;
        }
    }

    private sealed class PayloadMutatingInvoker(IPanGlossInvoker inner, bool mutateTsv) : IPanGlossInvoker
    {
        public string? ArtifactDirectory { get; private set; }
        public bool ArtifactsMatchEvidence { get; private set; }

        public async Task<PanGlossOutcome> RunAsync(PanGlossRequest request, string label,
            CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
        {
            var completed = Assert.IsType<PanGlossOutcome.Completed>(
                await inner.RunAsync(request, label, cancellationToken, wallClockCap));
            var evidence = completed.BatchEvidence!;
            ArtifactDirectory = Path.GetDirectoryName(evidence.TsvPath)!;
            ArtifactsMatchEvidence = BatchInvocationEvidence.DigestFile(evidence.TsvPath) == evidence.TsvSha256 &&
                BatchInvocationEvidence.DigestFile(evidence.AnalysesPath!) == evidence.AnalysesSha256;
            if (!mutateTsv)
            {
                var morphology = System.Text.Json.JsonSerializer.Deserialize<SIL.Motif.Contract.Responses.ParseWordEvidence>(
                    completed.MorphologyOutput!, SIL.Motif.Host.Parser.ParseMorphEvidence.JsonOptions)!;
                var changed = morphology with
                {
                    Analyses =
                    [
                        new SIL.Motif.Contract.Responses.ParseAnalysis(
                        [
                            new SIL.Motif.Contract.Responses.ParseMorph(
                                "00000000-0000-0000-0000-000000000001",
                                "00000000-0000-0000-0000-000000000002", null, null),
                        ]),
                    ],
                };
                return completed with
                {
                    MorphologyOutput = System.Text.Json.JsonSerializer.Serialize(changed,
                        SIL.Motif.Host.Parser.ParseMorphEvidence.JsonOptions),
                };
            }
            var lineEnd = completed.Output.IndexOf('\n');
            var cells = completed.Output[..lineEnd].Split('\t');
            cells[^1] = "forged-signature";
            return completed with
            {
                Output = string.Join('\t', cells) + completed.Output[lineEnd..],
            };
        }
    }

    [Fact]
    public async Task ACompletedBatchWithoutInvocationEvidenceIsRefused()
    {
        var assessor = new PanGlossAssessor(_paths, new FakeInvoker());
        var refusal = await Assert.ThrowsAsync<AssessorUnavailableException>(() =>
            assessor.ProduceAsync(Scope(AssessmentKind.ParseTime), _candidate, CancellationToken.None));
        Assert.Contains("evidence", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancellationAndUnavailabilityNeverReturnProducedRows()
    {
        var invoker = new FakeInvoker { Respond = _ => new PanGlossOutcome.Cancelled() };
        var assessor = new PanGlossAssessor(_paths, invoker);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            assessor.ProduceAsync(Scope(), _candidate, CancellationToken.None));
        invoker.Respond = _ => new PanGlossOutcome.Unavailable("no parser");
        var refusal = await Assert.ThrowsAsync<AssessorUnavailableException>(() =>
            assessor.ProduceAsync(Scope(), _candidate, CancellationToken.None));
        Assert.Contains("no parser", refusal.Message, StringComparison.Ordinal);
    }

    private static AssessmentScope Scope(params AssessmentKind[] collect) =>
        new(["motifa"], collect.Length == 0 ? [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming] : collect,
            TimeSpan.FromMilliseconds(700), 123);

    [Fact]
    public async Task OneBatchRetainsPartialCapAndLaterCompletedWordWithoutAFalseCompletedVerdict()
    {
        using var real = RealFakeInvoker();
        var invoker = new RewritingInvoker(real);
        var assessor = new PanGlossAssessor(_paths, invoker);
        var produced = await assessor.ProduceAsync(
            new(["motifa", "motifb"], [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming],
                TimeSpan.FromMilliseconds(700), 123), _candidate, CancellationToken.None);

        Assert.Equal(1, invoker.Calls);
        Assert.Equal(produced[0].Invocation, produced[1].Invocation);
        var batch = Assert.IsType<AssessmentRaw.Batch>(produced[0].Raw).Analysis;
        Assert.Equal(1, batch.Incomplete);
        Assert.Equal(1, batch.Adjudicated);
        Assert.Equal(SIL.Motif.Host.Parser.WordOutcome.Capped, batch.Words[0].Outcome);
        Assert.Equal("partial-match", batch.Words[0].Signature);
        Assert.Equal(SIL.Motif.Host.Parser.WordOutcome.Analysed, batch.Words[1].Outcome);
        Assert.Contains("warning: retained diagnostic", batch.Warnings);
        Assert.Null(produced[0].DiagnosticCount);
    }

    private sealed class RewritingInvoker(IPanGlossInvoker inner) : IPanGlossInvoker
    {
        public int Calls { get; private set; }

        public async Task<PanGlossOutcome> RunAsync(PanGlossRequest request, string label,
            CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
        {
            Calls++;
            var completed = Assert.IsType<PanGlossOutcome.Completed>(
                await inner.RunAsync(request, label, cancellationToken, wallClockCap));
            var evidence = completed.BatchEvidence!;
            const string rows = "0\tmotifa\t700\tCAP\tpartial-match\n1\tmotifb\t12\tok\tcomplete-match\n";
            const string warnings = "warning: retained diagnostic";
            File.WriteAllText(evidence.TsvPath, rows);
            File.WriteAllText(evidence.StandardErrorPath, warnings);
            var morphology = SIL.Motif.Host.Parser.ParseMorphEvidence.Read(completed.MorphologyOutput!, ["motifa", "motifb"])
                .Select((row, index) => row with
                {
                    ElapsedMs = index == 0 ? 700 : 12, Capped = index == 0,
                    Unavailable = ["Fixture partial identity unavailable"],
                });
            var jsonl = string.Join("\n", morphology.Select(row => System.Text.Json.JsonSerializer.Serialize(
                row, SIL.Motif.Host.Parser.ParseMorphEvidence.JsonOptions)));
            File.WriteAllText(evidence.AnalysesPath!, jsonl);
            return completed with
            {
                Output = rows,
                StandardError = warnings,
                MorphologyOutput = jsonl,
                BatchEvidence = evidence with
                {
                    AnalysesSha256 = BatchInvocationEvidence.DigestFile(evidence.AnalysesPath!),
                    TsvSha256 = BatchInvocationEvidence.DigestFile(evidence.TsvPath),
                    StandardErrorSha256 = BatchInvocationEvidence.DigestFile(evidence.StandardErrorPath),
                },
            };
        }
    }

    [Fact]
    public async Task RefusedPreExistingArtifactDirectoryIsNeverDeletedByTheAssessor()
    {
        var existing = Directory.CreateDirectory(Path.Combine(_root, "existing")).FullName;
        var sentinel = Path.Combine(existing, "keep.txt");
        File.WriteAllText(sentinel, "saved evidence");
        using var invoker = RealFakeInvoker();
        var assessor = new PanGlossAssessor(new FixedDirectory(existing), invoker);

        await Assert.ThrowsAsync<AssessorUnavailableException>(() =>
            assessor.ProduceAsync(Scope(), _candidate, CancellationToken.None));

        Assert.Equal("saved evidence", File.ReadAllText(sentinel));
    }

    private sealed class FixedDirectory(string directory) : IAssessorCachePathResolver
    {
        public string DirectoryFor(string invocationId) => directory;
    }

    private static PanGlossInvoker RealFakeInvoker(string? executable = null) => new(executable ?? FakeParser.ExecutablePath,
        new MachinePanGlossQueue(["Local\\MotifAssessorTests-" + Guid.NewGuid().ToString("N")]));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }
}
