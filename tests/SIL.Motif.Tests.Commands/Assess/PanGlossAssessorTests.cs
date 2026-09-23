using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Assess;

public sealed class PanGlossAssessorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-assessor-" + Guid.NewGuid().ToString("N"));
    private readonly string _candidate;
    private readonly StatsCacheStore _paths;

    public PanGlossAssessorTests()
    {
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
        Assert.Equal(123, evidence.PerWordStepLimit);
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

    private static PanGlossInvoker RealFakeInvoker() => new(FakeParser.ExecutablePath,
        new MachinePanGlossQueue(["Local\\MotifAssessorTests-" + Guid.NewGuid().ToString("N")]));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }
}
