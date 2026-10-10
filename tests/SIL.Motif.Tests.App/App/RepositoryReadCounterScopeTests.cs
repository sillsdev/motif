using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class RepositoryReadCounterScopeTests
{
    [Fact]
    public async Task ConcurrentObservationsCountOnlyTheirOwnRepositoryReads()
    {
        var root = Path.Combine(Path.GetTempPath(), "SIL.Motif.RepositoryReadCounterScopeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var first = Database(root, "first");
            using var second = Database(root, "second");
            using var barrier = new Barrier(2);

            var counts = await Task.WhenAll(Observe(first, barrier), Observe(second, barrier));

            Assert.All(counts, count => Assert.Equal(new ScaleRepositoryReadCount(1, 0), count));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReaderCountsUtf8PayloadAndPhysicalTuplesAcrossRepeatedAndDetailReads()
    {
        var textId = Guid.NewGuid();
        var wordformId = Guid.NewGuid();
        var forms = new[] { new WritingSystemText("λόγος", "grc"), new WritingSystemText("logos", "en") };
        using var fixture = new StoredSelectionFixture(new TextWordsProjection([
            new TextWordsProjectedText(textId, "λόγος", [new TextWordsProjectedLine(1, "λόγος, λόγος", [
                new TextWordsProjectedToken("λόγος", forms, wordformId, "unanalysed", null, null, null, null, 0, null),
                new TextWordsProjectedToken(",", [], null, null, null, null, null, null, 1, null),
                new TextWordsProjectedToken("λόγος", forms, wordformId, "unanalysed", null, null, null, null, 2, null),
            ], Guid.NewGuid(), Guid.NewGuid(), false)], []),
        ], [new TextWordsProjectedWordform(wordformId, [], [], 0, false, [])]));
        var project = new ProjectLocator(fixture.ProjectPath, "project");
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT sum(length(CAST(IndexJson AS BLOB))) FROM BaselineTextReadIndex) + " +
            "(SELECT sum(length(CAST(SummaryJson AS BLOB))) FROM BaselineSummaries);";
        var expectedBytes = Convert.ToInt64(command.ExecuteScalar());
        using var observation = ScaleCountHarness.ObserveRepositoryReads();
        var opened = await fixture.OpenAsync(new OpenSelectionReaderRequest(fixture.ProjectPath, [textId], []));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        await using var reader = opened.Value!;
        var first = observation.Snapshot();
        Assert.Equal(expectedBytes, first.BaselineJsonPayloadBytes);
        Assert.Equal(2, first.BaselineOccurrenceTuples);
        Assert.Equal(expectedBytes, reader.Diagnostics.BaselineJsonPayloadBytes);
        Assert.Equal(2, reader.Diagnostics.BaselineOccurrenceTuplesRead);
        Assert.Equal(2, reader.Diagnostics.PhysicalOccurrences);
        Assert.Equal(4, reader.Diagnostics.Memberships);
        var repeated = await fixture.OpenAsync(new OpenSelectionReaderRequest(fixture.ProjectPath, [textId], []));
        Assert.True(repeated.Succeeded, repeated.Refusal?.Message);
        await using var second = repeated.Value!;
        Assert.Equal(2 * expectedBytes, observation.Snapshot().BaselineJsonPayloadBytes);
        Assert.Equal(4, observation.Snapshot().BaselineOccurrenceTuples);
        var detail = await reader.ReadLinePagesAsync(textId, [new(1, 0)]);
        Assert.True(detail.Succeeded, detail.Refusal?.Message);
        using var lease = detail.Value!;
        Assert.Equal(6, observation.Snapshot().BaselineOccurrenceTuples);
        Assert.Equal(4, reader.Diagnostics.BaselineOccurrenceTuplesRead);
        Assert.True(observation.Snapshot().BaselineJsonPayloadBytes > 2 * expectedBytes);
    }

    private static async Task<ScaleRepositoryReadCount> Observe(MotifDatabase database, Barrier barrier)
    {
        using var observation = ScaleCountHarness.ObserveRepositoryReads();
        await Task.Run(() => barrier.SignalAndWait());
        await Task.Run(() => new BaselineRepository(database).GetCurrent("workspace"));
        return observation.Snapshot();
    }

    private static MotifDatabase Database(string root, string name)
    {
        var project = new ProjectLocator(Path.Combine(root, name + ".fwdata"), name);
        return MotifDatabase.OpenOwned(Path.Combine(root, name + ".motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
    }
}
