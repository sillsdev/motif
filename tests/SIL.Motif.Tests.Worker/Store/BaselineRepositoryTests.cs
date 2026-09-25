using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Baselines;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class BaselineRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.BaselineRepositoryTests",
        Guid.NewGuid().ToString("N"));

    public BaselineRepositoryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Record_RoundTripsThroughTheOwnedMotifDatabase()
    {
        var project = Project("roundtrip");
        var path = Path.Combine(_root, "roundtrip.motif.db");
        var sourceLastWriteUtc = DateTimeOffset.Parse("2026-08-23T11:30:00Z");
        using (var database = MotifDatabase.OpenOwned(
                   path, project, MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            var repository = new BaselineRepository(database);
            repository.Record("workspace", Publication("a", "sha256:" + new string('a', 64)),
                DateTimeOffset.Parse("2026-08-23T12:00:00Z"), sourceLastWriteUtc);
        }

        using var reopened = MotifDatabase.OpenOwned(
            path, project, MotifSchema.CurrentSchema, new Version(1, 0));
        var record = new BaselineRepository(reopened).GetCurrent("workspace");

        Assert.NotNull(record);
        Assert.Equal("workspace", record.ProjectKey);
        Assert.Equal("sha256:" + new string('a', 64), record.Token.BundleDigest);
        Assert.Equal(Path.Combine(_root, "a", "project.fwdata"), record.FwDataPath);
        Assert.Equal(DateTimeOffset.Parse("2026-08-23T12:00:00Z"), record.PublishedUtc);
        Assert.Equal(sourceLastWriteUtc, record.SourceLastWriteUtc);
        Assert.Equal(TimeSpan.Zero, record.SourceLastWriteUtc.Offset);
        // The source last-write and capture-digest times measure different events, so pin they diverge.
        Assert.NotEqual(DateTimeOffset.Parse(record.Token.CapturedUtc), record.SourceLastWriteUtc);
        Assert.NotEqual(record.PublishedUtc, record.SourceLastWriteUtc);
    }

    [Fact]
    public void Record_ReplacesCurrentRowButSameDigestIsIdempotent()
    {
        using var database = MotifDatabase.OpenOwned(Path.Combine(_root, "replace.motif.db"), Project("replace"),
            MotifSchema.CurrentSchema, new Version(1, 0));
        var repository = new BaselineRepository(database);
        var first = Publication("first", "sha256:" + new string('a', 64));
        var second = Publication("second", "sha256:" + new string('b', 64));
        repository.Record("workspace", first, DateTimeOffset.Parse("2026-08-23T12:00:00Z"),
            DateTimeOffset.Parse("2026-08-23T11:00:00Z"));
        repository.Record("workspace", first, DateTimeOffset.Parse("2026-08-23T13:00:00Z"),
            DateTimeOffset.Parse("2026-08-23T11:00:00Z"));

        Assert.Equal(DateTimeOffset.Parse("2026-08-23T12:00:00Z"),
            repository.GetCurrent("workspace")!.PublishedUtc);

        repository.Record("workspace", second, DateTimeOffset.Parse("2026-08-23T14:00:00Z"),
            DateTimeOffset.Parse("2026-08-23T13:30:00Z"));

        var replaced = repository.GetCurrent("workspace")!;
        Assert.Equal(second.Token, replaced.Token);
        Assert.Equal(DateTimeOffset.Parse("2026-08-23T13:30:00Z"), replaced.SourceLastWriteUtc);
    }

    [Fact]
    public void GetCurrentEvidenceReturnsTheCurrentBaselineAndStoredSummary()
    {
        using var database = OpenDatabase("evidence");
        var repository = new BaselineRepository(database);
        repository.Record("workspace", Publication("evidence", "sha256:" + new string('a', 64)),
            DateTimeOffset.Parse("2026-08-23T12:00:00Z"),
            DateTimeOffset.Parse("2026-08-23T11:00:00Z"), Summary());

        var evidence = repository.GetCurrentEvidence("workspace");

        Assert.NotNull(evidence);
        Assert.Equal("workspace", evidence.Baseline.ProjectKey);
        Assert.Equal(2, evidence.Summary.WordCount);
        Assert.Equal(3, evidence.Summary.OccurrenceCount);
        Assert.Equal(["motifa", "motifb"], evidence.Summary.Wordforms);
        Assert.Equal(2, evidence.Summary.Texts.Count);
    }

    [Fact]
    public void GetCurrentEvidenceReturnsNullWithoutACurrentBaseline()
    {
        using var database = OpenDatabase("empty-evidence");

        var evidence = new BaselineRepository(database).GetCurrentEvidence("workspace");

        Assert.Null(evidence);
    }

    [Fact]
    public void GetCurrentEvidenceRejectsAMissingProjectSummary()
    {
        using var database = OpenDatabase("missing-summary");
        var repository = new BaselineRepository(database);
        repository.Record("workspace", Publication("missing-summary", "sha256:" + new string('a', 64)),
            DateTimeOffset.Parse("2026-08-23T12:00:00Z"),
            DateTimeOffset.Parse("2026-08-23T11:00:00Z"), Summary());
        Execute(database, "DELETE FROM BaselineSummaries WHERE ProjectKey = $project;", "workspace", null);

        var exception = Assert.Throws<InvalidDataException>(() => repository.GetCurrentEvidence("workspace"));

        Assert.Contains("no stored project summary", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"WordCount\":-1,\"OccurrenceCount\":0,\"WordformCount\":0,\"RuleCount\":0,\"LexemeCount\":0,\"Wordforms\":[],\"Texts\":[]}")]
    public void GetCurrentEvidenceRejectsMalformedOrInvalidSummaryJson(string summaryJson)
    {
        using var database = OpenDatabase("malformed-summary");
        var repository = new BaselineRepository(database);
        repository.Record("workspace", Publication("malformed-summary", "sha256:" + new string('a', 64)),
            DateTimeOffset.Parse("2026-08-23T12:00:00Z"),
            DateTimeOffset.Parse("2026-08-23T11:00:00Z"), Summary());
        Execute(database, "UPDATE BaselineSummaries SET SummaryJson = $summary WHERE ProjectKey = $project;",
            "workspace", summaryJson);

        var exception = Assert.Throws<InvalidDataException>(() => repository.GetCurrentEvidence("workspace"));

        Assert.Contains("project summary is malformed", exception.Message, StringComparison.Ordinal);
        Assert.IsType<System.Text.Json.JsonException>(exception.InnerException);
    }

    [Fact]
    public void Record_RejectsANonUtcSourceLastWriteOffset()
    {
        using var database = MotifDatabase.OpenOwned(Path.Combine(_root, "offset.motif.db"), Project("offset"),
            MotifSchema.CurrentSchema, new Version(1, 0));
        var repository = new BaselineRepository(database);

        Assert.Throws<ArgumentException>(() => repository.Record("workspace",
            Publication("offset", "sha256:" + new string('a', 64)),
            DateTimeOffset.Parse("2026-08-23T12:00:00Z"),
            new DateTimeOffset(2026, 8, 23, 11, 0, 0, TimeSpan.FromHours(1))));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private ProjectLocator Project(string name) => new(Path.Combine(_root, name + ".fwdata"), name);

    private BaselinePublication Publication(string folder, string digest)
    {
        var root = Path.Combine(_root, folder);
        return new BaselinePublication(root, Path.Combine(root, "project.fwdata"),
            new BaselineToken("project-id", "sha256:" + new string('1', 64), "projection-v1",
                "2026-08-23T00:00:00Z", digest));
    }

    private MotifDatabase OpenDatabase(string name) => MotifDatabase.OpenOwned(
        Path.Combine(_root, name + ".motif.db"), Project(name), MotifSchema.CurrentSchema, new Version(1, 0));

    private static ProjectSummarySnapshot Summary() => new(2, 3, 4, 5, 6, ["motifa", "motifb"],
        [new ProjectTextSummary(Guid.NewGuid(), "Genesis", 2, 3,
            new Dictionary<string, int>(StringComparer.Ordinal) { ["motifa"] = 2, ["motifb"] = 1 }),
         new ProjectTextSummary(Guid.NewGuid(), "Exodus", 1, 0, new Dictionary<string, int>())]);

    private static void Execute(MotifDatabase database, string sql, string project, string? summary)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$project", project);
        if (summary is not null) command.Parameters.AddWithValue("$summary", summary);
        command.ExecuteNonQuery();
    }
}
