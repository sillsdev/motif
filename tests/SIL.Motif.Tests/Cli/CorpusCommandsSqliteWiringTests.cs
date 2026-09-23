using System;
using System.IO;
using System.Linq;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Projection.Usage;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// <see cref="CorpusCommands.StoreFor"/> points at <see cref="SqliteCorpusStore"/> over a project's
/// paired database; this proves the CLI verbs still work end to end against it, not only the store
/// in isolation.
/// </summary>
public sealed class CorpusCommandsSqliteWiringTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-cli-corpus-wiring-tests", Guid.NewGuid().ToString("N"));
    private readonly string _fwDataPath;

    public CorpusCommandsSqliteWiringTests()
    {
        Directory.CreateDirectory(_root);
        _fwDataPath = Path.Combine(_root, "Project.fwdata");
        File.WriteAllText(_fwDataPath, string.Empty);
    }

    private CommandOutcome<CorpusAddedResponse> AddCorpus(
        string fwDataPath, string productVersion, string corpusId, string description, string? uri, string? licence,
        LicenceCapabilities capabilities, string tokeniser, string tokeniserVersion, string? tokeniserNotes) =>
        CorpusCommands.AddCorpus(new AddCorpusRequest(
            fwDataPath, productVersion, corpusId, description, uri, licence, capabilities, tokeniser,
            tokeniserVersion, tokeniserNotes));

    private CommandOutcome<CorpusListProjection> ListCorpora(string fwDataPath, string productVersion, UsageLog? usage = null) =>
        CorpusCommands.ListCorpora(new ListCorporaRequest(fwDataPath, productVersion), usage);

    private CommandOutcome<CorpusDetailProjection> ShowCorpus(
        string fwDataPath, string productVersion, string corpusId, UsageLog? usage = null) =>
        CorpusCommands.ShowCorpus(new ShowCorpusRequest(fwDataPath, productVersion, corpusId), usage);
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void StoreForReturnsASqliteBackedStore_AndTheCliVerbsRoundTripThroughIt()
    {
        var project = new ProjectLocator(_fwDataPath, "Project");
        using (var database = MotifDatabase.OpenOwned(
            ProjectDatabaseCatalog.DatabasePathFor(project), project, MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            Assert.IsType<SqliteCorpusStore>(CorpusCommands.StoreFor(database));
        }

        var addResult = AddCorpus(
            _fwDataPath, "1.0", "tst-corpus", "Testlang corpus", uri: null, licence: "CC-BY-SA-4.0",
            capabilities: LicenceCapabilities.Unknown(), tokeniser: "whitespace-and-punctuation",
            tokeniserVersion: "1", tokeniserNotes: null);
        Assert.True(addResult.Succeeded);
        Assert.Equal("tst-corpus", addResult.Value!.CorpusId);

        var listResult = ListCorpora(_fwDataPath, "1.0");
        Assert.True(listResult.Succeeded);
        Assert.Contains(listResult.Value!.Corpora, corpus => corpus.CorpusId == "tst-corpus");

        var usage = new UsageLog();
        var listJson = ListCorpora(_fwDataPath, "1.0", usage);
        var detailText = ShowCorpus(_fwDataPath, "1.0", "tst-corpus", usage);
        var detailJson = ShowCorpus(_fwDataPath, "1.0", "tst-corpus", usage);

        Assert.True(listJson.Succeeded);
        Assert.True(detailText.Succeeded);
        Assert.True(detailJson.Succeeded);
        var corpus = Assert.Single(listJson.Value!.Corpora);
        Assert.Equal("tst-corpus", corpus.CorpusId);
        Assert.Equal("Testlang corpus", corpus.Description);
        Assert.Equal("tst-corpus", detailText.Value!.CorpusId);
        Assert.Equal("tst-corpus", detailJson.Value!.CorpusId);
        Assert.Equal(new[] { "corpora", "show-corpus", "show-corpus" }, usage.Entries.Select(e => e.Command));
        Assert.All(usage.Entries, entry => Assert.DoesNotContain("tst-corpus", entry.ArgumentShape));

        // The human text is unchanged (no "error: " prefix); the JSON failure now carries a stable code too.
        var missingText = ShowCorpus(_fwDataPath, "1.0", "missing");
        var missingJson = ShowCorpus(_fwDataPath, "1.0", "missing");
        Assert.False(missingText.Succeeded);
        Assert.False(missingJson.Succeeded);
        Assert.Equal(FailureReason.NotFound, missingText.Refusal!.Reason);
        Assert.Equal("corpus.not-found", missingText.Refusal.Code);
        Assert.Equal("No corpus 'missing' in store.", missingText.Refusal.Message);
        Assert.Equal("corpus.not-found", missingJson.Refusal!.Code);
        Assert.Equal("No corpus 'missing' in store.", missingJson.Refusal.Message);
        Assert.Equal(
            "No corpus 'missing' in store." + Environment.NewLine,
            CommandTextRenderer.Render(missingText, asJson: false).Output);

        // The database lives beside the project, not in a directory the caller happened to run from.
        Assert.True(File.Exists(ProjectDatabaseCatalog.DatabasePathFor(project)));
    }
}
