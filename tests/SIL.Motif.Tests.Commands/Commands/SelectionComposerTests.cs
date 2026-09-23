using SIL.LCModel;
using System.Text.Json;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="SelectionComposer"/> against each of the four agreed sources (design decision 4) in
/// isolation, their union, and both of its Refusal codes.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class SelectionComposerTests : IDisposable
{
    private static readonly SelectionRequest NoSources = new(false, [], [], false, null);

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-selection-composer-" + Guid.NewGuid().ToString("N"));
    private readonly LcmCache _cache;
    private readonly SeededText _text;

    public SelectionComposerTests(PristineProjectFixture pristine)
    {
        Directory.CreateDirectory(_root);
        _cache = pristine.NewScratch();
        _text = SeededProject.SeedText(_cache, pristine.Seed);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    [Fact]
    public void AllWordformsSourceContributesEveryProjectWordform()
    {
        var outcome = SelectionComposer.Compose(_cache, NoSources with { AllWordforms = true }, NewRepository());

        Assert.True(outcome.Succeeded);
        Assert.Equal(SeededWordsOrdinal, outcome.Value!.Selection.Words);
        var entry = Assert.Single(outcome.Value.Projection.Provenance);
        Assert.Equal(new SelectionProvenanceEntry("all-wordforms", 2), entry);
    }

    [Fact]
    public void TextsSourceContributesOnlyTheChosenTextsWordforms()
    {
        var outcome = SelectionComposer.Compose(
            _cache, NoSources with { TextIds = [_text.TextId] }, NewRepository());

        Assert.True(outcome.Succeeded);
        Assert.Equal(SeededWordsOrdinal, outcome.Value!.Selection.Words);
        var entry = Assert.Single(outcome.Value.Projection.Provenance);
        Assert.Equal(new SelectionProvenanceEntry("texts", 2), entry);
    }

    [Fact]
    public void UnknownTextIdIsRefused()
    {
        var missing = Guid.NewGuid();
        var outcome = SelectionComposer.Compose(_cache, NoSources with { TextIds = [missing] }, NewRepository());

        Assert.False(outcome.Succeeded);
        Assert.Equal("selection.text-not-found", outcome.Refusal!.Code);
        Assert.Equal(missing.ToString("D"), outcome.Refusal.Facts["textId"]);
    }

    [Fact]
    public void PastedWordsSourceTrimsDropsBlankLinesAndNormalizesToNfd()
    {
        const string precomposed = "café"; // one precomposed code point (U+00E9)
        const string decomposed = "café"; // "e" + COMBINING ACUTE ACCENT: the NFD form
        var request = NoSources with { Words = ["  " + precomposed + "  ", "", "   ", "word"] };

        var outcome = SelectionComposer.Compose(_cache, request, NewRepository());

        Assert.True(outcome.Succeeded);
        Assert.Contains(decomposed, outcome.Value!.Selection.Words);
        Assert.DoesNotContain(precomposed, outcome.Value.Selection.Words);
        var entry = Assert.Single(outcome.Value.Projection.Provenance);
        Assert.Equal(new SelectionProvenanceEntry("pasted-words", 2), entry);
    }

    [Fact]
    public void CombiningAllWordformsAndTextsDeduplicatesTheOverlapOrdinally()
    {
        var request = NoSources with { AllWordforms = true, TextIds = [_text.TextId] };

        var outcome = SelectionComposer.Compose(_cache, request, NewRepository());

        Assert.True(outcome.Succeeded);
        // Both sources see the same two project wordforms; the final list still has only two entries.
        Assert.Equal(SeededWordsOrdinal, outcome.Value!.Selection.Words);
        Assert.Equal(2, outcome.Value.Projection.Provenance.Count);
        Assert.All(outcome.Value.Projection.Provenance, entry => Assert.Equal(2, entry.Count));
    }

    [Fact]
    public void RetryFailedSourceContributesTheNewestRunsNoAnalysisAndSkippedWords()
    {
        var repository = NewRepository();
        RecordParseTimeRunWithBaseline(repository, "run-older", "2020-01-01T00:00:00Z", CurrentBaselineToken,
            ("stale-failure", WordOutcome.NoAnalysis, 0));
        RecordParseTimeRunWithBaseline(repository, "run-newest", "2020-01-02T00:00:00Z", CurrentBaselineToken,
            ("ok", WordOutcome.Analysed, 0), ("bad", WordOutcome.NoAnalysis, 0),
            ("gone", WordOutcome.Skipped, 0), ("slow", WordOutcome.TimedOut, 5000));

        var outcome = SelectionComposer.Compose(_cache,
            NoSources with { RetryFailed = true, RetrySourceAssessmentId = "run-newest" }, repository,
            CurrentBaselineToken);

        Assert.True(outcome.Succeeded);
        Assert.Equal(["bad", "gone"], outcome.Value!.Selection.Words);
        var entry = Assert.Single(outcome.Value.Projection.Provenance);
        Assert.Equal(new SelectionProvenanceEntry("retry-failed", 2), entry);
    }

    [Fact]
    public void RetrySlowerThanSourceContributesWordsStrictlyAboveTheThreshold()
    {
        var repository = NewRepository();
        RecordParseTimeRunWithBaseline(repository, "run-1", "2020-01-01T00:00:00Z", CurrentBaselineToken,
            ("ok", WordOutcome.Analysed, 50), ("bad", WordOutcome.NoAnalysis, 0),
            ("slow", WordOutcome.TimedOut, 5000));

        var outcome = SelectionComposer.Compose(
            _cache, NoSources with { RetrySlowerThan = TimeSpan.FromMilliseconds(500), RetrySourceAssessmentId = "run-1" },
            repository, CurrentBaselineToken);

        Assert.True(outcome.Succeeded);
        Assert.Equal(["slow"], outcome.Value!.Selection.Words);
        var entry = Assert.Single(outcome.Value.Projection.Provenance);
        Assert.Equal(new SelectionProvenanceEntry("retry-slower-than", 1), entry);
    }

    [Fact]
    public void RetrySourceMustMatchTheBaselineBeingMeasured()
    {
        var repository = NewRepository();
        RecordParseTimeRunWithBaseline(repository, "source", "2020-01-01T00:00:00Z", OtherBaselineToken,
            ("retry", WordOutcome.NoAnalysis, 0));

        var outcome = SelectionComposer.Compose(_cache,
            NoSources with { RetryFailed = true, RetrySourceAssessmentId = "source" }, repository, CurrentBaselineToken);

        Assert.False(outcome.Succeeded);
        Assert.Equal("selection.retry-source-mismatch", outcome.Refusal!.Code);
    }

    [Fact]
    public void RetrySourceMustBeBaselineParseTimeWithoutProposal()
    {
        using var database = NewDatabase();
        var repository = new AssessmentRepository(database);
        var proposalId = CanonicalId.Mint("proposal/");
        new ProposalRepository(database).SaveRevision(new ProposalRevisionRecord(
            proposalId, "sha256:" + new string('2', 64), "{}", "proposed", null, null, null));
        RecordRetryCandidate(repository, "wrong-kind", "2020-01-01T00:00:00Z", CurrentBaselineToken,
            "ObjectTiming", null, ("word", WordOutcome.NoAnalysis, 0));
        RecordRetryCandidate(repository, "proposal", "2020-01-02T00:00:00Z", CurrentBaselineToken,
            "ParseTime", proposalId, ("word", WordOutcome.NoAnalysis, 0));

        var wrongKind = SelectionComposer.Compose(_cache,
            NoSources with { RetryFailed = true, RetrySourceAssessmentId = "wrong-kind" }, repository,
            CurrentBaselineToken);
        var proposal = SelectionComposer.Compose(_cache,
            NoSources with { RetryFailed = true, RetrySourceAssessmentId = "proposal" }, repository,
            CurrentBaselineToken);

        Assert.False(wrongKind.Succeeded);
        Assert.Equal("selection.retry-source-invalid", wrongKind.Refusal!.Code);
        Assert.False(proposal.Succeeded);
        Assert.Equal("selection.retry-source-invalid", proposal.Refusal!.Code);
    }

    [Fact]
    public void RetrySourceProjectMustMatchTheLoadedProject()
    {
        var repository = NewRepository();
        RecordParseTimeRunWithBaseline(repository, "foreign-project", "2020-01-01T00:00:00Z",
            ForeignProjectBaselineToken, ("word", WordOutcome.NoAnalysis, 0));

        var outcome = SelectionComposer.Compose(_cache,
            NoSources with { RetryFailed = true, RetrySourceAssessmentId = "foreign-project" }, repository,
            ForeignProjectBaselineToken);

        Assert.False(outcome.Succeeded);
        Assert.Equal("selection.retry-source-project-mismatch", outcome.Refusal!.Code);
    }

    [Fact]
    public void RetryRequiresAnExplicitSourceAssessment()
    {
        var repository = NewRepository();
        RecordParseTimeRunWithBaseline(repository, "matching", "2020-01-01T00:00:00Z", CurrentBaselineToken,
            ("matching-word", WordOutcome.NoAnalysis, 0));
        RecordParseTimeRunWithBaseline(repository, "stale", "2020-01-02T00:00:00Z", OtherBaselineToken,
            ("stale-word", WordOutcome.NoAnalysis, 0));

        var outcome = SelectionComposer.Compose(_cache, NoSources with { RetryFailed = true }, repository,
            CurrentBaselineToken);

        Assert.False(outcome.Succeeded);
        Assert.Equal("selection.retry-source-required", outcome.Refusal!.Code);
    }

    // The point: the number, not just its presence, decides which words come back (see the plan's problem 1).
    [Fact]
    public void TwoDifferentThresholdsSelectDifferentWords()
    {
        var repository = NewRepository();
        RecordParseTimeRunWithBaseline(repository, "run-1", "2020-01-01T00:00:00Z", CurrentBaselineToken,
            ("fast", WordOutcome.Analysed, 50), ("medium", WordOutcome.Analysed, 600),
            ("slow", WordOutcome.TimedOut, 5000));

        var lowThreshold = SelectionComposer.Compose(
            _cache, NoSources with { RetrySlowerThan = TimeSpan.FromMilliseconds(500), RetrySourceAssessmentId = "run-1" },
            repository, CurrentBaselineToken);
        var highThreshold = SelectionComposer.Compose(
            _cache, NoSources with { RetrySlowerThan = TimeSpan.FromMilliseconds(1000), RetrySourceAssessmentId = "run-1" },
            repository, CurrentBaselineToken);

        Assert.True(lowThreshold.Succeeded);
        Assert.True(highThreshold.Succeeded);
        Assert.Equal(["medium", "slow"], lowThreshold.Value!.Selection.Words);
        Assert.Equal(["slow"], highThreshold.Value!.Selection.Words);
    }

    [Fact]
    public void EmptySelectionIsRefused()
    {
        var outcome = SelectionComposer.Compose(_cache, NoSources, NewRepository());

        Assert.False(outcome.Succeeded);
        Assert.Equal("selection.empty", outcome.Refusal!.Code);
    }

    [Fact]
    public void ExtraRetrySourceWithoutRetryIsRefused()
    {
        var outcome = SelectionComposer.Compose(_cache,
            NoSources with { Words = ["word"], RetrySourceAssessmentId = "source" }, NewRepository(),
            CurrentBaselineToken);

        Assert.False(outcome.Succeeded);
        Assert.Equal("selection.retry-source-without-retry", outcome.Refusal!.Code);
    }

    [Fact]
    public void CanonicalEquivalentRequestsShareDescriptorDigest()
    {
        var first = SelectionComposer.Compose(_cache,
            NoSources with { TextIds = [_text.TextId, _text.TextId], Words = [" café ", "word"] },
            NewRepository());
        var second = SelectionComposer.Compose(_cache,
            NoSources with { TextIds = [_text.TextId], Words = ["café", " word "] },
            NewRepository());

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(first.Value!.Descriptor.DescriptorSha256, second.Value!.Descriptor.DescriptorSha256);
        Assert.Equal(first.Value.Descriptor.PastedWords, second.Value.Descriptor.PastedWords);
    }

    private static string[] SeededWordsOrdinal =>
        new[] { SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm }
            .OrderBy(word => word, StringComparer.Ordinal)
            .ToArray();

    private IAssessmentRepository NewRepository()
    {
        return new AssessmentRepository(NewDatabase());
    }

    private MotifDatabase NewDatabase()
    {
        var project = new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project");
        return MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
    }

    private string CurrentBaselineToken => JsonSerializer.Serialize(
        new BaselineToken(_cache.LangProject.Guid.ToString("D"),
            "sha256:" + new string('a', 64), "projection-1", "2020-01-01T00:00:00Z",
            "sha256:" + new string('b', 64)), MotifJson.CreateOptions());

    private string OtherBaselineToken => JsonSerializer.Serialize(
        new BaselineToken(Guid.NewGuid().ToString("D"),
            "sha256:" + new string('a', 64), "projection-1", "2020-01-01T00:00:00Z",
            "sha256:" + new string('c', 64)), MotifJson.CreateOptions());

    private string ForeignProjectBaselineToken => JsonSerializer.Serialize(
        new BaselineToken("00000000-0000-0000-0000-000000000001",
            "sha256:" + new string('a', 64), "projection-1", "2020-01-01T00:00:00Z",
            "sha256:" + new string('b', 64)), MotifJson.CreateOptions());

    private static void RecordParseTimeRunWithBaseline(
        IAssessmentRepository repository, string assessmentId, string savedUtc, string baselineToken,
        params (string Word, WordOutcome Outcome, int ElapsedMs)[] words)
        => RecordRetryCandidate(repository, assessmentId, savedUtc, baselineToken, "ParseTime", null, words);

    private static void RecordRetryCandidate(
        IAssessmentRepository repository, string assessmentId, string savedUtc, string baselineToken,
        string kind, CanonicalId? proposalId, params (string Word, WordOutcome Outcome, int ElapsedMs)[] words)
    {
        var assessedWords = words
            .Select(w => new AssessedWord(w.Word, w.Outcome.ToStoredOutcome(), Array.Empty<ParsedAnalysis>(), w.ElapsedMs))
            .ToList();

        repository.Record(new NewAssessmentRecord(
            AssessmentId: assessmentId,
            ProposalId: proposalId,
            ProposalIntentDigest: null,
            Assessor: "test",
            Kind: kind,
            ScopeJson: "{}",
            ScopeDigest: "sha256:scope",
            TokeniserName: "whitespace-and-punctuation",
            TokeniserVersion: "1",
            BaselineToken: baselineToken,
            Selection: Selection.Create("run", words.Select(w => w.Word)),
            OutcomeDigest: "sha256:outcome",
            SemanticDigest: "sha256:semantic",
            GrammarSourceSha256: "sha256:grammar",
            ModelFingerprint: "fingerprint",
            Pipeline: "pipeline",
            DiagnosticCount: 0,
            Words: assessedWords,
            SavedUtc: savedUtc));
    }
}
