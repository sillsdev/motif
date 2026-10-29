using System.Text.Json;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class GrammarCheckStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.CommandScratch", Guid.NewGuid().ToString("N"));

    [Fact]
    public void TrialChecksAreCompressedAndOnlyTheNewestFivePerProposalRemain()
    {
        Directory.CreateDirectory(_root);
        var project = new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project");
        using var database = MotifDatabase.OpenOwned(Path.Combine(_root, "motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var token = new BaselineToken("project", "sha256:" + new string('a', 64), "1",
            "2026-08-29T00:00:00Z", "sha256:" + new string('b', 64));
        var id = CanonicalId.Mint("proposal/");
        var other = CanonicalId.Mint("proposal/");
        var response = new GrammarCheckResponse([], true) { Locale = new string('x', 10000) };
        var writer = new TrialGrammarCheckRepository(database);
        writer.Save(token, other, "other", "selection", response);
        for (var i = 0; i < 8; i++) writer.Save(token, id, "digest-" + i, "selection", response);

        var reader = new GrammarCheckRepository(database);
        var tokenJson = JsonSerializer.Serialize(token, MotifJson.CreateOptions());
        Assert.Null(reader.GetLatest(tokenJson, id.Value, "digest-2"));
        Assert.Equal(response.Locale, reader.GetLatest(tokenJson, id.Value, "digest-7")!.Locale);
        Assert.NotNull(reader.GetLatest(tokenJson, other.Value, "other"));
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MAX(length(ResponseGzip)) FROM GrammarChecks WHERE ProposalId = $id;";
        command.Parameters.AddWithValue("$id", id.Value);
        using var rows = command.ExecuteReader();
        Assert.True(rows.Read());
        Assert.Equal(5, rows.GetInt32(0));
        Assert.True(rows.GetInt32(1) < 2000);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
