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
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>Checks Corpus handlers against the SQLite store paired with their project.</summary>
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

    private CommandOutcome<CorpusListProjection> ListCorpora(string fwDataPath, string productVersion) =>
        CorpusCommands.ListCorpora(new ListCorporaRequest(fwDataPath, productVersion));

    private CommandOutcome<CorpusDetailProjection> ShowCorpus(
        string fwDataPath, string productVersion, string corpusId) =>
        CorpusCommands.ShowCorpus(new ShowCorpusRequest(fwDataPath, productVersion, corpusId));
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"CorpusId\":\"c\",\"Provenance\":null,\"Documents\":[]}")]
    public void DamagedSidecarsRefuseOpenShowAndReindexWithoutChangingSources(string damaged)
    {
        Assert.True(AddCorpus(_fwDataPath, "1.0", "c", "Corpus", null, null,
            LicenceCapabilities.Unknown(), "whitespace-and-punctuation", "1", null).Succeeded);
        Assert.True(CorpusCommands.PasteDocument(new PasteCorpusDocumentRequest(_fwDataPath, "1.0", "c", "d",
            "Document", "original text", null, null)).Succeeded);
        var sidecar = Directory.GetFiles(_root, "corpus.json", SearchOption.AllDirectories).Single();
        var source = Directory.GetFiles(_root, "*.txt", SearchOption.AllDirectories).Single();
        File.WriteAllText(sidecar, damaged);
        Assert.False(CorpusCommands.OpenDocument(new OpenCorpusDocumentRequest(_fwDataPath, "1.0", "c", "d")).Succeeded);
        Assert.False(ShowCorpus(_fwDataPath, "1.0", "c").Succeeded);
        Assert.False(CorpusCommands.RebuildIndex(new RebuildCorpusIndexRequest(_fwDataPath, "1.0")).Succeeded);
        Assert.Equal(damaged, File.ReadAllText(sidecar));
        Assert.Equal("original text", File.ReadAllText(source));
    }

    [Theory]
    [InlineData("bad\0name")]
    [InlineData("../escape")]
    [InlineData("bad/name")]
    public void InvalidSidecarFilenameIsAControlledRefusal(string invalidName)
    {
        Assert.True(AddCorpus(_fwDataPath, "1.0", "c", "Corpus", null, null,
            LicenceCapabilities.Unknown(), "whitespace-and-punctuation", "1", null).Succeeded);
        Assert.True(CorpusCommands.PasteDocument(new PasteCorpusDocumentRequest(_fwDataPath, "1.0", "c", "d",
            "Document", "original text", null, null)).Succeeded);
        var path = Directory.GetFiles(_root, "corpus.json", SearchOption.AllDirectories).Single();
        var source = Directory.GetFiles(_root, "*.txt", SearchOption.AllDirectories).Single();
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        json["Documents"]![0]!["RelativePath"] = invalidName;
        var damaged = json.ToJsonString();
        File.WriteAllText(path, damaged);
        var open = CorpusCommands.OpenDocument(new OpenCorpusDocumentRequest(_fwDataPath, "1.0", "c", "d"));
        var show = ShowCorpus(_fwDataPath, "1.0", "c");
        var reindex = CorpusCommands.RebuildIndex(new RebuildCorpusIndexRequest(_fwDataPath, "1.0"));
        Assert.Equal("store.inconsistent", open.Refusal!.Code);
        Assert.Equal("store.inconsistent", show.Refusal!.Code);
        Assert.Equal("store.inconsistent", reindex.Refusal!.Code);
        Assert.Equal(damaged, File.ReadAllText(path));
        Assert.Equal("original text", File.ReadAllText(source));
    }

    [Fact]
    public void PastedDocumentCanBeViewedAndReadByHash()
    {
        var added = AddCorpus(_fwDataPath, "1.0", "c", "Corpus", null, null,
            LicenceCapabilities.Unknown(), "whitespace-and-punctuation", "1", null);
        Assert.True(added.Succeeded);
        var pasted = CorpusCommands.PasteDocument(new PasteCorpusDocumentRequest(_fwDataPath, "1.0", "c", "d",
            "Document", "one two one", null, null));
        Assert.True(pasted.Succeeded);
        var opened = CorpusCommands.OpenDocument(new OpenCorpusDocumentRequest(_fwDataPath, "1.0", "c", "d"));
        Assert.True(opened.Succeeded);
        Assert.Equal("one", opened.Value!.Wordforms[0].Wordform);
        Assert.Equal(2, opened.Value.Wordforms[0].Frequency);
        var range = CorpusCommands.ReadRange(new CorpusRangeRequest(_fwDataPath, "1.0", "c", "d",
            opened.Value.Sha256, 4, 3));
        Assert.True(range.Succeeded);
        Assert.Equal("two", System.Text.Encoding.UTF8.GetString(range.Value!.Bytes));
    }

    [Fact]
    public void StoreForReturnsASqliteBackedStore_AndTheHandlersRoundTripThroughIt()
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

        var listJson = ListCorpora(_fwDataPath, "1.0");
        var detailText = ShowCorpus(_fwDataPath, "1.0", "tst-corpus");
        var detailJson = ShowCorpus(_fwDataPath, "1.0", "tst-corpus");

        Assert.True(listJson.Succeeded);
        Assert.True(detailText.Succeeded);
        Assert.True(detailJson.Succeeded);
        var corpus = Assert.Single(listJson.Value!.Corpora);
        Assert.Equal("tst-corpus", corpus.CorpusId);
        Assert.Equal("Testlang corpus", corpus.Description);
        Assert.Equal("tst-corpus", detailText.Value!.CorpusId);
        Assert.Equal("tst-corpus", detailJson.Value!.CorpusId);
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
