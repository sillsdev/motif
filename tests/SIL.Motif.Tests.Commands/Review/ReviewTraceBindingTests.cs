using System.Text.Json;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class ReviewTraceBindingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-review-trace-" + Guid.NewGuid().ToString("N"));
    private readonly MotifDatabase _database;
    private const string Id = "review-report/test";
    private static readonly string TraceId = new('a', 64);

    public ReviewTraceBindingTests()
    {
        Directory.CreateDirectory(_root);
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "project.db"),
            new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project"), MotifSchema.CurrentSchema, new Version(1, 0));
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Proposals (ProposalId, CurrentIntentDigest, Status) VALUES ('proposal/test','intent','proposed');
            INSERT INTO ReviewReports (ReportId,ProjectKey,ProposalId,TrialJobId,ManifestDigest,SummaryJson,SummaryDigest,
                InputReferencesJson,DetailAvailable,DetailBytes,CreatedUtc)
            VALUES ('review-report/test','project','proposal/test','job/test','manifest','{}','hash','[]',1,0,'now');
            """;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void ReadResourceCannotStartDiagnosticAndCursorIsBoundToTrace()
    {
        var nodes = Enumerable.Range(0, 3).Select(i => new TraceNode(i.ToString(), null, "rule", "label", null, [], null)).ToArray();
        ReviewTraceArtifacts.Store(_database, new RetainedReviewTrace(TraceId, "project", Id, "case", Side.After,
            "parser", "grammar", "scope", "complete", true, nodes));
        var saved = ReviewTraceArtifacts.Find(_database, "project", Id, "case", Side.After);
        Assert.Equal(TraceId, saved!.TraceId);
        Assert.Null(ReviewTraceArtifacts.Find(_database, "project", Id, "case", Side.Before));
        var command = new ReviewTraceCommands(_database);
        var first = command.Read("project", new ReviewReportId(Id), TraceId, limit: 1);
        Assert.True(first.Succeeded);
        Assert.Equal(TraceOrigin.Stored, first.Value!.Origin);
        var next = command.Read("project", new ReviewReportId(Id), TraceId, first.Value.NextCursor, 1);
        Assert.True(next.Succeeded);
        Assert.Equal("1", Assert.Single(next.Value!.Nodes).Id);
        Assert.False(command.Read("other-project", new ReviewReportId(Id), TraceId).Succeeded);
        Assert.False(command.Read("project", new ReviewReportId(Id), TraceId, Convert.ToBase64String("wrong:1"u8.ToArray())).Succeeded);
    }

    [Fact]
    public void TraceProjectionHasNoRawWorkOrElapsedCostAndBoundsCapture()
    {
        var leaf = new TraceStep("rule", "source", "input", "output", null, []);
        var root = new TraceStep("root", null, null, null, null, Enumerable.Repeat(leaf, 10_001).ToArray());
        var (nodes, complete) = ReviewTraceProjection.Capture(root);
        Assert.False(complete);
        Assert.Equal(10_000, nodes.Count);
        var json = JsonSerializer.Serialize(nodes, ReviewJson.Options);
        Assert.DoesNotContain("elapsed", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("parserWork", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("input", json, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _database.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}

[Collection(SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
[Trait("MotifTestLevel", "System")]
public sealed class ReviewTraceRealParserTests(SIL.Motif.Tests.TestFixtures.PristineProjectFixture pristine)
{
    [SIL.Motif.Tests.Parser.RealParserFact]
    public void ExactDiagnosticUsesTheNamedGrammar()
    {
        string project;
        using (var cache = pristine.NewScratch())
        {
            SIL.Motif.Tests.TestFixtures.RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b");
            project = cache.ProjectId.Path;
        }
        var parser = SIL.Motif.Host.Parser.PanGlossExecutable.TryLocate();
        Assert.NotNull(parser);
        var outcome = SIL.Motif.Commands.Queries.WordTraceQuery.QueryExact(project,
            SIL.Motif.Tests.TestFixtures.SeededProject.AnalysedWordForm, parser);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var (nodes, _) = ReviewTraceProjection.Capture(outcome.Value!.Reading.Root);
        Assert.NotEmpty(nodes);
    }
}
