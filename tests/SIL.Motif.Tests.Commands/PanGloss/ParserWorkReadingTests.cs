using System.Globalization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>Per-word work is read wherever PanGloss provides it, and its absence is never an error.</summary>
public sealed class ParserWorkReadingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-parser work-" + Guid.NewGuid().ToString("N"));

    public ParserWorkReadingTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void TheTraceFixtureReportsItsParserWorkAlongsideItsSteps()
    {
        var raw = SIL.Motif.Tests.TestFixtures.TestTraceFixture.Read(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-kapita.json"));

        var details = PanGlossTraceDiagnosticReader.Read(raw).Details;

        Assert.Equal(237L, details.ParserWork);
    }

    [Fact]
    public void ATraceWithoutParserWorkLeavesItNull()
    {
        var raw = SIL.Motif.Tests.TestFixtures.TestTraceFixture.Read(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-kapita.json"))
            .Replace(",\"workSteps\":237", string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("workSteps\":237", raw, StringComparison.Ordinal);

        Assert.Null(PanGlossTraceDiagnosticReader.Read(raw).Details.ParserWork);
    }

    [Fact]
    public void ABatchTsvThatDeclaresAWorkColumnHasItRead()
    {
        var first = new BatchRow(0, NewWord(), 3, "ok", "sig", 345);
        var capped = new BatchRow(1, NewWord(), 900, "CAP", "-", 20_000_000);
        var tsv = SerializeRows(
            ["index", "word", "elapsed_ms", "status", "signature", "work_steps"],
            [first, capped], row =>
                [row.Index.ToString(CultureInfo.InvariantCulture), row.Word,
                    row.ElapsedMs.ToString(CultureInfo.InvariantCulture), row.Status, row.Signature,
                    row.ParserWork!.Value.ToString(CultureInfo.InvariantCulture)]);

        var rows = BatchTsvParser.Parse(tsv);

        Assert.Equal(2, rows.Count);
        Assert.Equal(345L, rows[0].ParserWork);
        Assert.Equal(first.Word, rows[0].Word);
        Assert.Equal(WordOutcome.Capped, rows[1].Outcome);
        Assert.Equal(20_000_000L, rows[1].ParserWork);
        Assert.Equal(capped.Word, rows[1].Word);
    }

    [Fact]
    public void MemoryCappedBatchRowsAreIncompleteAndRetainTheirReason()
    {
        var row = Assert.Single(BatchTsvParser.Parse("0\tword\t8\tMEMCAP\t-\n"));
        Assert.Equal(WordOutcome.MemoryCapped, row.Outcome);
        Assert.Equal("memory-capped", row.Outcome.ToStoredOutcome());
        Assert.True(StoredWordOutcomeNames.TryParseStoredOutcome("memory-capped", out var restored));
        Assert.Equal(row.Outcome, restored);
        var batch = new BatchAnalysis([row], null, "project", []);
        Assert.Equal(1, batch.Capped);
        Assert.Equal(1, batch.Incomplete);
        Assert.Equal(0, batch.Adjudicated);
        Assert.Equal(StopLimit.Memory, StopLimit.From("memory-capped", null));
    }

    [Fact]
    public void HeaderlessWorkColumnsAndWatchdogRowsAreRead()
    {
        var rows = BatchTsvParser.Parse("0\tword\t3\tok\tsig\t345\n1\tother\t8\tTIMEOUT\t-\n");
        Assert.Equal(345L, rows[0].ParserWork);
        Assert.Null(rows[1].ParserWork);
        var guessed = Assert.Single(BatchTsvParser.Parse("0\tword\t3\tok\tsig\ttrue\n"));
        Assert.Null(guessed.ParserWork);
    }

    [Fact]
    public void HeaderlessSixColumnRowsPreserveWorkAndOutcomes()
    {
        var rows = BatchTsvParser.Parse("0\tquick\t1\tok\tsig\t501\n" +
            "1\tstopped\t3\tCAP\t-\t20000\n" +
            "2\tunknown\t4\tok\t-\t-\n");
        Assert.Equal(("quick", WordOutcome.Analysed, 501L), (rows[0].Word, rows[0].Outcome, rows[0].ParserWork));
        Assert.Equal((WordOutcome.Capped, 20000L), (rows[1].Outcome, rows[1].ParserWork));
        Assert.Equal(WordOutcome.NoAnalysis, rows[2].Outcome);
        Assert.Null(rows[2].ParserWork);
    }

    [Fact]
    public void TheCurrentBatchTsvHasNoStepColumnsAndStillParses()
    {
        var analysed = new BatchRow(0, NewWord(), 3, "ok", "sig");
        var timedOut = new BatchRow(1, NewWord(), 2000, "TIMEOUT", "-");
        var rows = BatchTsvParser.Parse(SerializeRows(
            ["index", "word", "elapsed_ms", "status", "signature"], [analysed, timedOut], row =>
                [row.Index.ToString(CultureInfo.InvariantCulture), row.Word,
                    row.ElapsedMs.ToString(CultureInfo.InvariantCulture), row.Status, row.Signature]));

        Assert.Equal([WordOutcome.Analysed, WordOutcome.TimedOut], rows.Select(row => row.Outcome));
        Assert.Equal([analysed.Word, timedOut.Word], rows.Select(row => row.Word));
        Assert.All(rows, row => Assert.Null(row.ParserWork));
    }

    [Fact]
    public void AStatsCacheWithAWorkColumnYieldsParserWorkPerWord()
    {
        var path = Path.Combine(_root, "with-work.sqlite");
        WriteCache(path, withWork: true);

        var words = PanGlossBatchStatisticsReader.Read(path, ["word"]).Words;

        Assert.Equal(777L, words["word"].ParserWork);
    }

    [Fact]
    public void AStatsCacheWithoutAWorkColumnStillReadsAndLeavesParserWorkNull()
    {
        var path = Path.Combine(_root, "without-work.sqlite");
        WriteCache(path, withWork: false);

        var words = PanGlossBatchStatisticsReader.Read(path, ["word"]).Words;

        Assert.Null(words["word"].ParserWork);
    }

    [Theory]
    [InlineData("capped", StopLimit.Work)]
    [InlineData("memory-capped", StopLimit.Memory)]
    [InlineData("timed-out", StopLimit.Time)]
    [InlineData("analysed", null)]
    [InlineData("no-analysis", null)]
    [InlineData("skipped", null)]
    public void AStoppedWordNamesTheLimitThatStoppedItInItsJson(string outcome, string? expected)
    {
        var row = new AssessmentWordResult(outcome, outcome, outcome is "capped" or "memory-capped" or "timed-out", "x", 5, "sig")
            { ParserWork = 42 };

        using var json = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(row, SIL.Motif.Contract.MotifJson.CreateOptions()));

        var stoppedBy = json.RootElement.TryGetProperty("StoppedBy", out var value) && value.ValueKind != System.Text.Json.JsonValueKind.Null
            ? value.GetString() : null;
        Assert.Equal(expected, stoppedBy);
        Assert.Equal(42L, json.RootElement.GetProperty("ParserWork").GetInt64());
    }

    private static void WriteCache(string path, bool withWork)
    {
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
                word_id INTEGER PRIMARY KEY,
                form TEXT NOT NULL,
                elapsed_ns INTEGER NOT NULL,
                attempts INTEGER NOT NULL,
                passes INTEGER NOT NULL, work INTEGER);
            CREATE TABLE object (
                object_id INTEGER PRIMARY KEY, key TEXT NOT NULL, kind TEXT NOT NULL, label TEXT NOT NULL,
                identity_quality TEXT NOT NULL);
            CREATE TABLE fact (
                word_id INTEGER NOT NULL, object_id INTEGER NOT NULL, direction TEXT NOT NULL,
                attempts INTEGER NOT NULL, self_time_ns INTEGER, work_steps INTEGER);
            INSERT INTO cache_identity VALUES (1, {{PanGlossInterfaceVersions.StatsCacheVersion}});
            INSERT INTO word VALUES (1, 'word', 725001, 11, 2{{(withWork ? ", 777" : ", NULL")}});
            """;
        command.ExecuteNonQuery();
    }

    private static string SerializeRows<T>(IReadOnlyList<string> columns, IEnumerable<T> rows,
        Func<T, IReadOnlyList<string>> values)
    {
        var serialized = new List<string[]> { columns.ToArray() };
        foreach (var row in rows)
        {
            var fields = values(row).ToArray();
            if (fields.Length != columns.Count || fields.Any(field => field.Contains('\t') || field.Contains('\n')))
                throw new ArgumentException("Each TSV row must provide one plain value per declared column.");
            serialized.Add(fields);
        }

        return string.Join("\n", serialized.Select(fields => string.Join("\t", fields))) + "\n";
    }

    private static string NewWord() => "word-" + Guid.NewGuid().ToString("N");

    private sealed record BatchRow(int Index, string Word, int ElapsedMs, string Status, string Signature,
        long? ParserWork = null);
}
