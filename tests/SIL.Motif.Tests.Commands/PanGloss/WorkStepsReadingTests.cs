using System.Globalization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>Per-word work is read wherever PanGloss provides it, and its absence is never an error.</summary>
public sealed class WorkStepsReadingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-work-steps-" + Guid.NewGuid().ToString("N"));

    public WorkStepsReadingTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void TheTraceFixtureReportsItsWorkStepsAlongsideItsSteps()
    {
        var raw = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-kapita.json"));

        var details = PanGlossTraceDiagnosticReader.Read(raw).Details;

        Assert.Equal(237L, details.WorkSteps);
        Assert.Equal(3L, details.Steps);
    }

    [Fact]
    public void ATraceWithoutWorkStepsLeavesItNull()
    {
        var raw = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-kapita.json"))
            .Replace(",\"workSteps\":237", string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("workSteps\":237", raw, StringComparison.Ordinal);

        Assert.Null(PanGlossTraceDiagnosticReader.Read(raw).Details.WorkSteps);
    }

    [Fact]
    public void ABatchTsvThatDeclaresStepColumnsHasThemRead()
    {
        var first = new BatchRow(0, NewWord(), 3, "ok", "sig", 12, 345);
        var capped = new BatchRow(1, NewWord(), 900, "CAP", "-", 200_000, 20_000_000);
        var tsv = SerializeRows(
            ["index", "word", "elapsed_ms", "status", "signature", "steps", "work_steps"],
            [first, capped], row =>
                [row.Index.ToString(CultureInfo.InvariantCulture), row.Word,
                    row.ElapsedMs.ToString(CultureInfo.InvariantCulture), row.Status, row.Signature,
                    row.Steps!.Value.ToString(CultureInfo.InvariantCulture),
                    row.WorkSteps!.Value.ToString(CultureInfo.InvariantCulture)]);

        var rows = BatchTsvParser.Parse(tsv);

        Assert.Equal(2, rows.Count);
        Assert.Equal((12L, 345L), (rows[0].Steps, rows[0].WorkSteps));
        Assert.Equal(first.Word, rows[0].Word);
        Assert.Equal(WordOutcome.Capped, rows[1].Outcome);
        Assert.Equal((200_000L, 20_000_000L), (rows[1].Steps, rows[1].WorkSteps));
        Assert.Equal(capped.Word, rows[1].Word);
    }

    [Fact]
    public void HeaderlessWorkColumnsAndWatchdogRowsAreRead()
    {
        var rows = BatchTsvParser.Parse("0\tword\t3\tok\tsig\t12\t345\n1\tother\t8\tTIMEOUT\t-\n");
        Assert.Equal((12L, 345L), (rows[0].Steps, rows[0].WorkSteps));
        Assert.Null(rows[1].WorkSteps);
        var guessed = Assert.Single(BatchTsvParser.Parse("0\tword\t3\tok\tsig\ttrue\t12\t345\n"));
        Assert.Equal((12L, 345L), (guessed.Steps, guessed.WorkSteps));
    }

    [Fact]
    public void HeaderlessSevenAndEightColumnRowsPreserveWorkAndOutcomes()
    {
        var rows = BatchTsvParser.Parse("0\tquick\t1\tok\tsig\t4\t501\n" +
            "1\tguessed\t2\tok\tsig\ttrue\t7\t900\n" +
            "2\tstopped\t3\tCAP\t-\tfalse\t200\t20000\n" +
            "3\tunknown\t4\tok\t-\t-\t-\n");
        Assert.Equal(("quick", WordOutcome.Analysed, 4L, 501L),
            (rows[0].Word, rows[0].Outcome, rows[0].Steps, rows[0].WorkSteps));
        Assert.Equal(("guessed", WordOutcome.Analysed, 7L, 900L),
            (rows[1].Word, rows[1].Outcome, rows[1].Steps, rows[1].WorkSteps));
        Assert.Equal((WordOutcome.Capped, 200L, 20000L), (rows[2].Outcome, rows[2].Steps, rows[2].WorkSteps));
        Assert.Equal(WordOutcome.NoAnalysis, rows[3].Outcome);
        Assert.Null(rows[3].Steps);
        Assert.Null(rows[3].WorkSteps);
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
        Assert.All(rows, row => Assert.Null(row.Steps));
        Assert.All(rows, row => Assert.Null(row.WorkSteps));
    }

    [Fact]
    public void AStatsCacheWithAWorkColumnYieldsWorkStepsPerWord()
    {
        var path = Path.Combine(_root, "with-work.sqlite");
        WriteCache(path, withWork: true);

        var words = PanGlossBatchStatisticsReader.Read(path, ["word"]).Words;

        Assert.Equal(777L, words["word"].WorkSteps);
        Assert.Equal(11, words["word"].Attempts);
    }

    [Fact]
    public void AStatsCacheWithoutAWorkColumnStillReadsAndLeavesWorkStepsNull()
    {
        var path = Path.Combine(_root, "without-work.sqlite");
        WriteCache(path, withWork: false);

        var words = PanGlossBatchStatisticsReader.Read(path, ["word"]).Words;

        Assert.Null(words["word"].WorkSteps);
        Assert.Equal(11, words["word"].Attempts);
    }

    [Theory]
    [InlineData("capped", StopLimit.Work)]
    [InlineData("timed-out", StopLimit.Time)]
    [InlineData("analysed", null)]
    [InlineData("no-analysis", null)]
    [InlineData("skipped", null)]
    public void AStoppedWordNamesTheLimitThatStoppedItInItsJson(string outcome, string? expected)
    {
        var row = new AssessmentWordResult(outcome, outcome, outcome is "capped" or "timed-out", "x", 5, "sig")
            { WorkSteps = 42 };

        using var json = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(row, SIL.Motif.Contract.MotifJson.CreateOptions()));

        var stoppedBy = json.RootElement.TryGetProperty("StoppedBy", out var value) && value.ValueKind != System.Text.Json.JsonValueKind.Null
            ? value.GetString() : null;
        Assert.Equal(expected, stoppedBy);
        Assert.Equal(42L, json.RootElement.GetProperty("WorkSteps").GetInt64());
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
            CREATE TABLE word (
                word_id INTEGER PRIMARY KEY,
                form TEXT NOT NULL,
                elapsed_ns INTEGER NOT NULL,
                attempts INTEGER NOT NULL,
                passes INTEGER NOT NULL{{(withWork ? ", work INTEGER" : string.Empty)}});
            CREATE TABLE object (
                object_id INTEGER PRIMARY KEY, key TEXT NOT NULL, kind TEXT NOT NULL, label TEXT NOT NULL,
                identity_quality TEXT NOT NULL);
            CREATE TABLE fact (
                word_id INTEGER NOT NULL, object_id INTEGER NOT NULL, direction TEXT NOT NULL,
                attempts INTEGER NOT NULL, self_time_ns INTEGER);
            INSERT INTO cache_identity VALUES (1, {{PanGlossInterfaceVersions.StatsCacheVersion}});
            INSERT INTO word VALUES (1, 'word', 725001, 11, 2{{(withWork ? ", 777" : string.Empty)}});
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
        long? Steps = null, long? WorkSteps = null);
}
