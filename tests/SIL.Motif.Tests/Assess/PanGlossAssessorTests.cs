using System.Security.Cryptography;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Assess;

/// <summary>
/// PanGloss's own Assessor, composed from existing seams. These tests never touch a real or fake
/// executable — the process boundary belongs to <see cref="IPanGlossInvoker"/> and <see cref="IPanGlossAssessor"/>
/// individually; this seam's job is what it declares and what it records once those run, which is what is
/// under test here.
/// </summary>
public sealed class PanGlossAssessorTests : IDisposable
{
    private const string GrammarSha256 = "sha256:" + "cc33cc33cc33cc33cc33cc33cc33cc33cc33cc33cc33cc33cc33cc33cc33cc";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-pangloss-assessor-" + Guid.NewGuid().ToString("N"));
    private readonly string _exportedCandidate;
    private readonly StatsCacheStore _cachePaths;

    public PanGlossAssessorTests()
    {
        Directory.CreateDirectory(_root);
        _exportedCandidate = Path.Combine(_root, "candidate");
        Directory.CreateDirectory(_exportedCandidate);
        File.WriteAllText(Path.Combine(_exportedCandidate, "candidate.fwdata"), "the fake runners never read this.");
        _cachePaths = new StatsCacheStore(WorkspaceOwnership.Bootstrap(Path.Combine(_root, "worker")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }

    [Fact]
    public async Task TheRecordedAssessmentCarriesTheCachePathAndDigest()
    {
        var cacheBytes = "pretend sqlite bytes"u8.ToArray();
        var invoker = Invoker(cacheBytes: cacheBytes);
        var assessor = new PanGlossAssessor(_cachePaths, invoker, new FakeReportRunner(Report()));
        var scope = Scope(AssessmentKind.ObjectTiming);

        var produced = Assert.Single(await assessor.ProduceAsync(scope, _exportedCandidate, CancellationToken.None));

        var expectedPath = _cachePaths.PathFor(GrammarSha256, PanGlossAssessor.AssessorName, "fast");
        Assert.Equal(AssessmentKind.ObjectTiming, produced.Kind);
        Assert.Equal(GrammarSha256, produced.GrammarSourceSha256);
        AssertCarriesReportHeader(produced, Report());
        var raw = Assert.IsType<AssessmentRaw.FileCache>(produced.Raw);
        Assert.Equal(expectedPath, raw.Path);
        Assert.Equal(ExpectedDigest(cacheBytes), raw.Digest);
        Assert.True(File.Exists(expectedPath));
        Assert.DoesNotContain(invoker.Requests, r => r.Request is PanGlossRequest.Batch { StatsCachePath: null });
    }

    [Fact]
    public async Task DifferentGrammarDigests_RecordDifferentCachePaths()
    {
        var invoker = Invoker(cacheBytes: "bytes"u8.ToArray());
        var assessor = new PanGlossAssessor(_cachePaths, invoker, new FakeReportRunner(Report()));

        var first = Assert.Single(await assessor.ProduceAsync(
            Scope(AssessmentKind.ObjectTiming), _exportedCandidate, CancellationToken.None));

        var otherReportRunner = new FakeReportRunner(Report(grammarSha256:
            "sha256:" + "dd44dd44dd44dd44dd44dd44dd44dd44dd44dd44dd44dd44dd44dd44dd44dd"));
        var otherInvoker = Invoker(cacheBytes: "bytes"u8.ToArray());
        var otherAssessor = new PanGlossAssessor(_cachePaths, otherInvoker, otherReportRunner);
        var second = Assert.Single(await otherAssessor.ProduceAsync(
            Scope(AssessmentKind.ObjectTiming), _exportedCandidate, CancellationToken.None));

        Assert.NotEqual(
            ((AssessmentRaw.FileCache)first.Raw).Path,
            ((AssessmentRaw.FileCache)second.Raw).Path);
    }

    [Fact]
    public async Task ProduceAsync_ReturnsWordMeasurementsForCorrectness_WithoutDiscardingTheReport()
    {
        var report = Report();
        var invoker = Invoker();
        var assessor = new PanGlossAssessor(_cachePaths, invoker, new FakeReportRunner(report));

        var produced = Assert.Single(await assessor.ProduceAsync(
            Scope(AssessmentKind.Correctness), _exportedCandidate, CancellationToken.None));

        Assert.Equal(GrammarSha256, produced.GrammarSourceSha256);
        AssertCarriesReportHeader(produced, report);
        var raw = Assert.IsType<AssessmentRaw.WordMeasurements>(produced.Raw);
        Assert.Same(report.Words, raw.Words);
        Assert.Empty(invoker.Requests);
    }

    [Fact]
    public async Task ProduceAsync_ReturnsTheBatchForParseTime_WithoutDiscardingIt()
    {
        var invoker = Invoker(tsvRows: "0\tmotifa\t12\tok\tsig\n");
        var assessor = new PanGlossAssessor(_cachePaths, invoker, new FakeReportRunner(Report()));

        var produced = Assert.Single(await assessor.ProduceAsync(
            Scope(AssessmentKind.ParseTime), _exportedCandidate, CancellationToken.None));

        Assert.Equal(GrammarSha256, produced.GrammarSourceSha256);
        AssertCarriesReportHeader(produced, Report());
        var raw = Assert.IsType<AssessmentRaw.Batch>(produced.Raw);
        Assert.Single(raw.Analysis.Words);
        Assert.Equal("motifa", raw.Analysis.Words[0].Word);
        Assert.Equal(12, raw.Analysis.Words[0].ElapsedMs);
    }

    [Fact]
    public async Task ProduceAsync_DefaultsToParseTimeAndCorrectness_WhenTheScopeCollectsNothingSpecific()
    {
        var invoker = Invoker(tsvRows: "0\tmotifa\t12\tok\tsig\n");
        var assessor = new PanGlossAssessor(_cachePaths, invoker, new FakeReportRunner(Report()));

        var produced = await assessor.ProduceAsync(Scope(), _exportedCandidate, CancellationToken.None);

        Assert.Equal(
            new HashSet<AssessmentKind> { AssessmentKind.ParseTime, AssessmentKind.Correctness },
            produced.Select(p => p.Kind).ToHashSet());
    }

    [Fact]
    public void SupportedKinds_IsExactlyParseTimeCorrectnessAndObjectTiming()
    {
        var assessor = NeverRunningAssessor();

        Assert.Equal(
            new[] { AssessmentKind.ParseTime, AssessmentKind.Correctness, AssessmentKind.ObjectTiming },
            assessor.SupportedKinds);
    }

    [Fact]
    public void SupportedKinds_NeverContainsEngineSizeDifferenceOrCompletion()
    {
        var assessor = NeverRunningAssessor();

        Assert.DoesNotContain(AssessmentKind.EngineSize, assessor.SupportedKinds);
        Assert.DoesNotContain(AssessmentKind.Difference, assessor.SupportedKinds);
        Assert.DoesNotContain(AssessmentKind.Completion, assessor.SupportedKinds);
    }

    [Fact]
    public async Task AskingForEngineSize_RefusesNamingTheKind()
    {
        var assessor = NeverRunningAssessor();

        var failure = await Assert.ThrowsAsync<AssessorRefusalException>(() => assessor.ProduceAsync(
            Scope(AssessmentKind.EngineSize), _exportedCandidate, CancellationToken.None));

        Assert.Equal(AssessmentKind.EngineSize, failure.Kind);
    }

    // A declaration-only assessor: nothing here should ever reach the invoker or report runner.
    private PanGlossAssessor NeverRunningAssessor() => new(_cachePaths, Invoker(), new FakeReportRunner(Report()));

    [Fact]
    public async Task AnUnrecognizedEngineName_IsRefused()
    {
        var assessor = new PanGlossAssessor(_cachePaths, Invoker(), new FakeReportRunner(Report()));
        var scope = new AssessmentScope(
            words: ["motifa"], engine: "turbo", collect: [AssessmentKind.Correctness], perWordLimit: TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            assessor.ProduceAsync(scope, _exportedCandidate, CancellationToken.None));
    }

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

    private static AssessmentScope Scope(params AssessmentKind[] collect) => new(
        words: ["motifa"], engine: "fast", collect: collect, perWordLimit: TimeSpan.FromSeconds(1));

    private static AssessReport Report(string? grammarSha256 = null) => new(
        Words: [],
        OutcomeDigest: "sha256:" + new string('a', 64),
        SemanticDigest: "sha256:" + new string('b', 64),
        GrammarSourceSha256: grammarSha256 ?? GrammarSha256,
        ModelFingerprint: "fp-1",
        Pipeline: "foma-confirm",
        DiagnosticCount: 0);

    // Pinned by these three call sites: the report's header must survive onto every kind, not just Correctness.
    private static void AssertCarriesReportHeader(ProducedAssessment produced, AssessReport report)
    {
        Assert.Equal(report.OutcomeDigest, produced.OutcomeDigest);
        Assert.Equal(report.SemanticDigest, produced.SemanticDigest);
        Assert.Equal(report.ModelFingerprint, produced.ModelFingerprint);
        Assert.Equal(report.Pipeline, produced.Pipeline);
        Assert.Equal(report.DiagnosticCount, produced.DiagnosticCount);
    }

    private static string ExpectedDigest(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return "sha256:" + Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
    }

    private sealed class FakeReportRunner(AssessReport report) : IPanGlossAssessor
    {
        public Task<AssessReport> RunAsync(string exportedCandidate, CancellationToken cancellationToken) =>
            Task.FromResult(report);
    }
}
