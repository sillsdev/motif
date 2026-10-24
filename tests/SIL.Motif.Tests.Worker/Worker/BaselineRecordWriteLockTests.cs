using System.Collections;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class BaselineRecordWriteLockTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(BaselineRecordWriteLockTests),
        Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Captured = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);

    public BaselineRecordWriteLockTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void RecordingABaselineDoesNotHoldTheStoreWriteLockWhileItPreparesTheTextRows()
    {
        var project = Project();
        var path = Path.Combine(_root, "motif.db");
        using var database = MotifDatabase.OpenOwned(path, project, MotifSchema.CurrentSchema, new Version(1, 0));
        var projection = Projection();
        var outcomes = new List<(string Phase, string Result)>();
        void Probe(string phase) => outcomes.Add((phase, TryWriteElsewhere(path)));
        var text = Assert.Single(projection.Texts);
        var line = Assert.Single(text.Lines);
        var token = Assert.Single(line.Tokens);
        token = token with { Forms = new Observed<WritingSystemText>(token.Forms, () => Probe("forms")) };
        line = line with { Tokens = [token] };
        text = text with { Lines = new Observed<TextWordsProjectedLine>([line], () => Probe("lines")) };
        var observed = new TextWordsProjection(
            new Observed<TextWordsProjectedText>([text], () => Probe("texts")),
            new Observed<TextWordsProjectedWordform>(projection.Wordforms, () => Probe("wordforms")));
        var summary = Summary(projection) with
        {
            Wordforms = new Observed<string>(["original"], () => Probe("summary")),
        };

        new BaselineRepository(database).Record(ProjectWorkspaceKey.Compute(project), Publication("first", 'a'),
            Captured, Captured, observed, summary);

        foreach (var phase in new[] { "texts", "wordforms", "lines", "forms", "summary" })
            Assert.Contains(outcomes, outcome => outcome.Phase == phase);
        Assert.All(outcomes, outcome => Assert.True(outcome.Result == "free",
            $"{outcome.Phase} preparation held the store write lock ({outcome.Result})."));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("index")]
    public void FailedPreparationPreservesThePriorBaselineRowsEvidenceAndIndex(string failure)
    {
        var project = Project();
        using var database = MotifDatabase.OpenOwned(Path.Combine(_root, "motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var repository = new BaselineRepository(database);
        var key = ProjectWorkspaceKey.Compute(project);
        var original = Publication("original", 'a');
        var projection = Projection();
        var textId = Assert.Single(projection.Texts).TextId;
        var prior = repository.Record(key, original, Captured, Captured, projection, Summary(projection));
        var priorEvidence = Json(repository.GetCurrentEvidence(key));
        var priorWords = Json(repository.GetCurrentTextWords(key, [textId])!.Projection);
        var priorIndex = Json(repository.ReadCurrentSelectionIndexes(key, [textId])!.Texts);
        var oldFile = File.ReadAllBytes(original.FwDataPath);
        var replacement = Publication("replacement", 'b');
        var failed = failure == "source"
            ? projection with
            {
                Texts = new Observed<TextWordsProjectedText>(projection.Texts,
                    () => throw new InvalidDataException("Text preparation failed.")),
            }
            : projection with { Wordforms = [] };

        Assert.Throws<InvalidDataException>(() => repository.Record(key, replacement,
            Captured.AddMinutes(1), Captured.AddMinutes(1), failed, ProjectSummarySnapshot.Empty));

        Assert.Equal(prior, repository.GetCurrent(key));
        Assert.Equal(prior, repository.GetCurrentPublished(key));
        Assert.True(repository.IsCurrentEvidence(key, original.Token));
        Assert.False(repository.IsCurrentEvidence(key, replacement.Token));
        Assert.Equal(priorEvidence, Json(repository.GetCurrentEvidence(key)));
        Assert.Equal(priorWords, Json(repository.GetCurrentTextWords(key, [textId])!.Projection));
        Assert.Equal(priorIndex, Json(repository.ReadCurrentSelectionIndexes(key, [textId])!.Texts));
        Assert.Equal(oldFile, File.ReadAllBytes(original.FwDataPath));
    }

    private ProjectLocator Project() => new(Path.Combine(_root, "live", "project.fwdata"), "write-lock-project");

    private BaselinePublication Publication(string name, char digest)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "project.fwdata");
        File.WriteAllText(path, name);
        var token = new BaselineToken("write-lock-project", "sha256:" + new string(digest, 64),
            "projection-v1", Captured.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), "sha256:" + new string(digest, 64));
        return new BaselinePublication(directory, path, token);
    }

    private static TextWordsProjection Projection()
    {
        var wordform = Guid.NewGuid();
        var token = new TextWordsProjectedToken("original", [new WritingSystemText("original", "en")],
            wordform, "unanalysed", null, null, null, null, 0, null);
        var line = new TextWordsProjectedLine(1, "original", [token], Guid.NewGuid(), Guid.NewGuid(), true);
        return new TextWordsProjection([new TextWordsProjectedText(Guid.NewGuid(), "Original Text", [line], [])],
            [new TextWordsProjectedWordform(wordform, [], [], 0, false, [])]);
    }

    private static ProjectSummarySnapshot Summary(TextWordsProjection projection) => new(1, 1, 1, 0, 0,
        ["original"], [new ProjectTextSummary(projection.Texts[0].TextId, "Original Text", 1, 1,
            new Dictionary<string, int>(StringComparer.Ordinal) { ["original"] = 1 })]);

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, MotifJson.CreateOptions());

    private static string TryWriteElsewhere(string path)
    {
        using var other = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
            DefaultTimeout = 1,
        }.ToString());
        other.Open();
        using var command = other.CreateCommand();
        command.CommandText = "BEGIN IMMEDIATE; ROLLBACK;";
        try { command.ExecuteNonQuery(); return "free"; }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6) { return "busy"; }
    }

    private sealed class Observed<T>(IReadOnlyList<T> inner, Action onRead) : IReadOnlyList<T>
    {
        public T this[int index] { get { onRead(); return inner[index]; } }
        public int Count => inner.Count;
        public IEnumerator<T> GetEnumerator() { onRead(); return inner.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
