using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>A Trial records each word's attempts and work from the statistics its batch wrote.</summary>
public sealed class TrialWordStatisticsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-trial-stats-" + Guid.NewGuid().ToString("N"));

    public TrialWordStatisticsTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ATrialWordGetsItsAttemptsPassesAndWorkFromTheBatchStatistics()
    {
        var cache = WriteCache("stats.sqlite", withWork: true);
        var statistics = TrialWordStatistics.Read([Produced(cache)], ["word"]);

        var word = TrialWordStatistics.Apply(
            new AssessedWord("word", "analysed", []) { Morphology = Evidence() }, statistics);

        Assert.Equal(11, word.Attempts);
        Assert.Equal(2, word.Passes);
        Assert.Equal(777L, word.WorkSteps);
        Assert.Equal(11, word.Morphology!.Attempts);
    }

    [Fact]
    public void ACacheWithoutWorkStillGivesAttemptsAndLeavesWorkNull()
    {
        var cache = WriteCache("no-work.sqlite", withWork: false);

        var word = TrialWordStatistics.Apply(new AssessedWord("word", "analysed", []),
            TrialWordStatistics.Read([Produced(cache)], ["word"]));

        Assert.Equal(11, word.Attempts);
        Assert.Null(word.WorkSteps);
    }

    [Fact]
    public void AWordKeepsTheCountsItsTsvCarriedWhenNoStatisticsNameIt()
    {
        var word = new AssessedWord("word", "analysed", []) { Attempts = 5, WorkSteps = 50 };

        Assert.Equal(word, TrialWordStatistics.Apply(word, null));
    }

    [Fact]
    public void ARecoveredRetryUsesItsTsvCountersWhenItsStatisticsAreUnavailable()
    {
        var cache = WriteCache("first-pass.sqlite", withWork: true);
        var retried = new WordAnalysis(0, "word", 1200, WordOutcome.Analysed, "sig")
        {
            Steps = 29,
            WorkSteps = 2900,
            WasRetried = true,
        };
        var produced = new[] { Produced(cache), ProducedBatch(retried) };
        var word = Assert.Single(AssessmentMaterial.From(new AssessmentRaw.Batch(
            new BatchAnalysis([retried], 700, "project", []))).Words);

        var statistics = TrialWordStatistics.Read(produced, ["word"]);
        var recorded = TrialWordStatistics.Apply(word, statistics);

        Assert.Equal(29, recorded.Attempts);
        Assert.Null(recorded.Passes);
        Assert.Equal(2900L, recorded.WorkSteps);
    }

    [Fact]
    public void ARecoveredRetryUsesItsOwnStatisticsInsteadOfTheFirstPassCounts()
    {
        var cache = WriteCache("first-pass-with-retry-stats.sqlite", withWork: true);
        var retried = new WordAnalysis(0, "word", 1200, WordOutcome.Analysed, "sig")
        {
            Steps = 29,
            WorkSteps = 2900,
            WasRetried = true,
            RetryStatistics = new PanGlossWordStatistics(29, 8, 1_200_000) { WorkSteps = 2900 },
        };
        var statistics = TrialWordStatistics.Read([Produced(cache), ProducedBatch(retried)], ["word"]);
        var word = new AssessedWord("word", "analysed", []);

        var recorded = TrialWordStatistics.Apply(word, statistics);

        Assert.Equal(29, recorded.Attempts);
        Assert.Equal(8, recorded.Passes);
        Assert.Equal(2900L, recorded.WorkSteps);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARecoveredRetryNeverBorrowsFirstPassCountsWhenOnlyItsMorphologyHasAttempts(bool hasAttempts)
    {
        var cache = WriteCache("first-pass-morphology.sqlite", withWork: true);
        var retried = new WordAnalysis(0, "word", 1200, WordOutcome.Analysed, "sig")
        {
            WasRetried = true,
            Morphology = Evidence() with { Attempts = hasAttempts ? 29 : null },
        };
        var word = Assert.Single(AssessmentMaterial.From(new AssessmentRaw.Batch(
            new BatchAnalysis([retried], 700, "project", []))).Words);

        var recorded = TrialWordStatistics.Apply(word,
            TrialWordStatistics.Read([Produced(cache), ProducedBatch(retried)], ["word"]));

        Assert.Equal(hasAttempts ? 29 : (int?)null, recorded.Attempts);
        Assert.Equal(hasAttempts ? 29 : (int?)null, recorded.Morphology!.Attempts);
        Assert.Null(recorded.Passes);
        Assert.Null(recorded.WorkSteps);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARecoveredRetryKeepsItsStatisticsWhenTheInitialCacheIsUnavailable(bool malformed)
    {
        var path = Path.Combine(_root, "unreadable-first-pass.sqlite");
        if (malformed) File.WriteAllText(path, "not a database");
        var retried = new WordAnalysis(0, "word", 1200, WordOutcome.Analysed, "sig")
        {
            WasRetried = true,
            RetryStatistics = new PanGlossWordStatistics(29, 8, 1_200_000) { WorkSteps = 2900 },
        };
        var produced = malformed ? new[] { Produced(path), ProducedBatch(retried) } : [ProducedBatch(retried)];
        var recorded = TrialWordStatistics.Apply(new AssessedWord("word", "analysed", []),
            TrialWordStatistics.Read(produced, ["word"]));

        Assert.Equal(29, recorded.Attempts);
        Assert.Equal(8, recorded.Passes);
        Assert.Equal(2900L, recorded.WorkSteps);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FinalTrialCountersAndMorphologySurviveRecording(bool retryStatisticsAvailable)
    {
        var cache = WriteCache("persist-first-pass.sqlite", withWork: true);
        var retried = new WordAnalysis(0, "word", 1200, WordOutcome.Analysed, "sig")
        {
            WasRetried = true,
            Morphology = new ParseWordEvidence(ParseMorphEvidence.Schema, 0, "word", 1200,
                false, false, false, [], []) { Attempts = 29 },
            RetryStatistics = retryStatisticsAvailable
                ? new PanGlossWordStatistics(29, 8, 1_200_000) { WorkSteps = 2900 } : null,
        };
        var produced = ProducedBatch(retried);
        var statistics = TrialWordStatistics.Read([Produced(cache), produced], ["word"]);
        var record = AssessmentMaterial.ToWordRecord(produced, "retry", null, null, "pangloss",
            "{}", "sha256:scope", "{}", new AssessmentSubject.Words(Selection.Create("trial", ["word"]), "none", "1"));
        record = record with
        {
            Material = record.RequireWordMaterial() with
            {
                Rows = record.RequireWordMaterial().Rows.Select(word => TrialWordStatistics.Apply(word, statistics)).ToArray(),
            },
        };
        var project = new ProjectLocator(Path.Combine(_root, "trial.fwdata"), "trial");
        using var database = MotifDatabase.OpenOwned(Path.Combine(_root, "trial.motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var repository = new AssessmentRepository(database);

        repository.RecordBatch([record]);
        var stored = Assert.Single(repository.Get("retry").Words!);

        Assert.Equal(29, stored.Attempts);
        Assert.Equal(29, stored.Morphology!.Attempts);
        Assert.Equal(retryStatisticsAvailable ? 8 : (int?)null, stored.Passes);
        Assert.Equal(retryStatisticsAvailable ? 2900L : (long?)null, stored.WorkSteps);
    }

    [Fact]
    public void AnUnretriedWordKeepsItsInitialStatisticsBesideARecoveredRetry()
    {
        var cache = WriteCache("mixed.sqlite", withWork: true, includeQuick: true);
        var retried = new WordAnalysis(0, "word", 1200, WordOutcome.Analysed, "sig")
        {
            WasRetried = true,
            RetryStatistics = new PanGlossWordStatistics(29, 8, 1_200_000) { WorkSteps = 2900 },
        };
        var statistics = TrialWordStatistics.Read([Produced(cache), ProducedBatch(retried)], ["word", "quick"]);

        var recorded = TrialWordStatistics.Apply(new AssessedWord("quick", "analysed", []), statistics);

        Assert.Equal(5, recorded.Attempts);
        Assert.Equal(3, recorded.Passes);
        Assert.Equal(42L, recorded.WorkSteps);
    }

    [Fact]
    public void AnUnreadableCacheLeavesTheCountsUnknownInsteadOfFailingTheTrial()
    {
        var path = Path.Combine(_root, "broken.sqlite");
        File.WriteAllText(path, "not a database");

        Assert.Null(TrialWordStatistics.Read([Produced(path)], ["word"]));
    }

    private static ProducedAssessment Produced(string cache) => new(AssessmentKind.ObjectTiming, "sha256:x", null,
        null, null, null, null, new AssessmentRaw.FileCache(cache, "sha256:y"));

    private static ProducedAssessment ProducedBatch(WordAnalysis word) => new(AssessmentKind.ParseTime, "sha256:x", null,
        null, null, null, null, new AssessmentRaw.Batch(new BatchAnalysis([word], 700, "project", [])));

    private static ParseWordEvidence Evidence() =>
        new("schema", 0, "word", 3, false, false, false, [], []);

    private string WriteCache(string name, bool withWork, bool includeQuick = false)
    {
        var path = Path.Combine(_root, name);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $$"""
            CREATE TABLE cache_identity (cache_id INTEGER PRIMARY KEY, schema_version INTEGER NOT NULL);
            CREATE TABLE run (counter_semantics INTEGER NOT NULL);
            INSERT INTO run VALUES (8);
            CREATE TABLE word (
                word_id INTEGER PRIMARY KEY, form TEXT NOT NULL, elapsed_ns INTEGER NOT NULL,
                attempts INTEGER NOT NULL, passes INTEGER NOT NULL{{(withWork ? ", work INTEGER" : string.Empty)}});
            CREATE TABLE object (
                object_id INTEGER PRIMARY KEY, key TEXT NOT NULL, kind TEXT NOT NULL, label TEXT NOT NULL,
                identity_quality TEXT NOT NULL);
            CREATE TABLE fact (
                word_id INTEGER NOT NULL, object_id INTEGER NOT NULL, direction TEXT NOT NULL,
                attempts INTEGER NOT NULL, self_time_ns INTEGER, work_steps INTEGER);
            INSERT INTO cache_identity VALUES (1, {{PanGlossInterfaceVersions.StatsCacheVersion}});
            INSERT INTO word VALUES (1, 'word', 725001, 11, 2{{(withWork ? ", 777" : string.Empty)}});
            """;
        command.ExecuteNonQuery();
        if (includeQuick)
        {
            command.CommandText = $"INSERT INTO word VALUES (2, 'quick', 1000, 5, 3{(withWork ? ", 42" : string.Empty)});";
            command.ExecuteNonQuery();
        }
        return path;
    }
}
