using SIL.LCModel;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Projects;
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
        RecordParseTimeRun(repository, "run-older", "2020-01-01T00:00:00Z",
            ("stale-failure", WordOutcome.NoAnalysis));
        RecordParseTimeRun(repository, "run-newest", "2020-01-02T00:00:00Z",
            ("ok", WordOutcome.Analysed), ("bad", WordOutcome.NoAnalysis),
            ("gone", WordOutcome.Skipped), ("slow", WordOutcome.TimedOut));

        var outcome = SelectionComposer.Compose(_cache, NoSources with { RetryFailed = true }, repository);

        Assert.True(outcome.Succeeded);
        Assert.Equal(["bad", "gone"], outcome.Value!.Selection.Words);
        var entry = Assert.Single(outcome.Value.Projection.Provenance);
        Assert.Equal(new SelectionProvenanceEntry("retry-failed", 2), entry);
    }

    [Fact]
    public void RetrySlowerThanSourceContributesTheNewestRunsTimedOutWords()
    {
        var repository = NewRepository();
        RecordParseTimeRun(repository, "run-1", "2020-01-01T00:00:00Z",
            ("ok", WordOutcome.Analysed), ("bad", WordOutcome.NoAnalysis), ("slow", WordOutcome.TimedOut));

        var outcome = SelectionComposer.Compose(
            _cache, NoSources with { RetrySlowerThan = TimeSpan.FromMilliseconds(500) }, repository);

        Assert.True(outcome.Succeeded);
        Assert.Equal(["slow"], outcome.Value!.Selection.Words);
        var entry = Assert.Single(outcome.Value.Projection.Provenance);
        Assert.Equal(new SelectionProvenanceEntry("retry-slower-than", 1), entry);
    }

    [Fact]
    public void EmptySelectionIsRefused()
    {
        var outcome = SelectionComposer.Compose(_cache, NoSources, NewRepository());

        Assert.False(outcome.Succeeded);
        Assert.Equal("selection.empty", outcome.Refusal!.Code);
    }

    private static string[] SeededWordsOrdinal =>
        new[] { SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm }
            .OrderBy(word => word, StringComparer.Ordinal)
            .ToArray();

    private IAssessmentRepository NewRepository()
    {
        var project = new ProjectLocator(
            Path.Combine(_root, "project.fwdata"), "project");
        var database = MotifDatabase.OpenOwned(
            Path.Combine(_root, "project.motif.db"), project, MotifSchema.CurrentSchema, new Version(1, 0));
        return new AssessmentRepository(database);
    }

    private static void RecordParseTimeRun(
        IAssessmentRepository repository, string assessmentId, string savedUtc,
        params (string Word, WordOutcome Outcome)[] words)
    {
        var assessedWords = words
            .Select(w => new AssessedWord(w.Word, w.Outcome.ToStoredOutcome(), Array.Empty<ParsedAnalysis>()))
            .ToList();

        repository.Record(new NewAssessmentRecord(
            AssessmentId: assessmentId,
            ProposalId: null,
            ProposalIntentDigest: null,
            Assessor: "test",
            Kind: "ParseTime",
            ScopeJson: "{}",
            ScopeDigest: "sha256:scope",
            TokeniserName: "whitespace",
            TokeniserVersion: "1",
            BaselineToken: "{}",
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
