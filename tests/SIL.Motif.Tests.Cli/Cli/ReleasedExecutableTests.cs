using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group2)]
[Trait("MotifTestLevel", "Integration")]
public sealed class ReleasedExecutableTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-released-executable-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReleasedOpenReadsTheSeededProjectSummaryAndReleasesItWithoutSaving()
    {
        var project = pristine.CopyProjectFile();
        var originalBytes = File.ReadAllBytes(project);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await CliProcess.RunAsync(Path.Combine(_root, "worker"), null, false,
                "open", project, "--json");
            Assert.True(result.ExitCode == 0, result.FailureDetails);
            Assert.Empty(result.Error);
            var summary = Json<ProjectSummaryProjection>(result);
            Assert.Equal(Path.GetFileNameWithoutExtension(project), summary.ProjectName);
            Assert.Equal(2, summary.LexicalEntryCount);
            Assert.Equal(originalBytes, File.ReadAllBytes(project));
        }
    }

    [Theory]
    [InlineData("report")]
    [InlineData("report --list-kinds")]
    [InlineData("compare")]
    public async Task ReleasedReportAndComparisonReturnTypedJsonAndPersistTheirResults(string command)
    {
        var project = pristine.CopyProjectFile();
        var fromId = RecordAssessment(project, firstWordParsed: true);
        var toId = RecordAssessment(project, firstWordParsed: false);
        string[] arguments = command switch
        {
            "report" => ["report", "--project", project, "--assessment", toId, "--kind", "coverage", "--json"],
            "report --list-kinds" => ["report", "--list-kinds", "--json"],
            "compare" => ["compare", "--project", project, "--from", fromId, "--to", toId, "--json"],
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };

        var result = await CliProcess.RunAsync(Path.Combine(_root, "worker"), null, false, arguments);
        Assert.True(result.ExitCode == 0, result.FailureDetails);
        Assert.Empty(result.Error);

        using var database = OpenDatabase(project);
        switch (command)
        {
            case "report":
                var report = Json<ReportResponse>(result);
                Assert.Equal(toId, report.AssessmentId);
                Assert.Equal("coverage", report.Kind);
                Assert.Equal(["motifa", "motifb"], report.SelectionWords);
                Assert.Contains("2 searches completed; 0 incomplete", report.Text, StringComparison.Ordinal);
                Assert.Contains("1 of 2 completed searches", report.Text, StringComparison.Ordinal);
                var storedReport = new ReportRepository(database).Get(report.ReportId);
                Assert.NotNull(storedReport);
                Assert.Equal(report.AssessmentId, storedReport.AssessmentId);
                Assert.Equal(report.Kind, storedReport.Kind);
                Assert.Equal(report.Text, storedReport.RenderedText);
                break;
            case "report --list-kinds":
                var kinds = Json<ReportKindListResponse>(result);
                Assert.Contains(kinds.Kinds, kind => kind.Kind == "coverage" && kind.Description.Length > 0);
                Assert.Contains(kinds.Kinds, kind => kind.Kind == "correctness" && kind.Description.Length > 0);
                break;
            case "compare":
                var comparison = Json<CompareResponse>(result);
                Assert.Equal(fromId, comparison.FromAssessmentId);
                Assert.Equal(toId, comparison.ToAssessmentId);
                Assert.Equal("pangloss", comparison.Assessor);
                Assert.Equal(2, comparison.FromWordCount);
                Assert.Equal(2, comparison.ToWordCount);
                Assert.Equal(2, comparison.SharedWordCount);
                Assert.False(comparison.TokeniserMismatch);
                var storedDifference = new AssessmentRepository(database).Get(comparison.AssessmentId);
                Assert.Equal("Difference", storedDifference.Kind);
                Assert.Equal("pangloss", storedDifference.Assessor);
                Assert.Equal("motifa", Assert.Single(storedDifference.Words!).Word);
                break;
        }
    }

    [Theory]
    [InlineData("add-document")]
    [InlineData("add-corpus-bundle")]
    public async Task ReleasedCorpusImportsReturnTypedJsonAndPersistLocalDocumentBytes(string command)
    {
        Directory.CreateDirectory(_root);
        var project = pristine.CopyProjectFile();
        const string text = "motifa motifb.\n";
        const string corpusId = "local-corpus";
        const string documentId = "local-document";
        const string title = "Local document";
        var source = Path.Combine(_root, "source with apostrophe's.txt");
        File.WriteAllText(source, text, new UTF8Encoding(false));
        string[] arguments;
        if (command == "add-document")
        {
            using (var database = OpenDatabase(project))
            {
                var store = new SqliteCorpusStore(database);
                new CorpusIngestion(store).AddCorpus(corpusId, new CorpusProvenance(
                    new CorpusOrigin("Local corpus", null, DateTimeOffset.UtcNow, "CC0", LicenceCapabilities.Unknown()),
                    new TokenisationRecord("whitespace", "1", ""), null));
            }
            arguments = [command, "--project", project, "--corpus", corpusId, "--doc", documentId,
                "--source", source, "--title", title, "--licence", "CC0", "--json"];
        }
        else
        {
            var bundle = Path.Combine(_root, "bundle.json");
            File.WriteAllText(bundle, JsonSerializer.Serialize(new
            {
                corpusId,
                origin = new { description = "Local corpus", retrievedUtc = "2026-10-01T00:00:00Z", licence = "CC0" },
                tokenisation = new { method = "whitespace", version = "1" },
                documents = new[] { new { documentId, title, source = Path.GetFileName(source), licence = "CC0" } },
            }));
            arguments = [command, "--project", project, "--bundle", bundle, "--json"];
        }

        var result = await CliProcess.RunAsync(Path.Combine(_root, "worker"), null, false, arguments);
        Assert.True(result.ExitCode == 0, result.FailureDetails);
        Assert.Empty(result.Error);
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        if (command == "add-document")
        {
            var added = Json<CorpusDocumentAddedResponse>(result);
            Assert.Equal(corpusId, added.CorpusId);
            Assert.Equal(documentId, added.DocumentId);
            Assert.Equal(title, added.Title);
            Assert.Equal(text.Length, added.CharacterCount);
            Assert.Equal(digest, added.ContentSha256);
        }
        else
        {
            var added = Json<CorpusBundleAddedResponse>(result);
            Assert.Equal(corpusId, added.CorpusId);
            Assert.Equal(1, added.DocumentCount);
            Assert.Equal("Local corpus", added.OriginDescription);
        }

        using (var database = OpenDatabase(project))
        {
            var corpus = Assert.IsType<StoredCorpus>(new SqliteCorpusStore(database).Load(corpusId));
            var stored = Assert.Single(corpus.Documents);
            Assert.Equal(documentId, stored.DocumentId);
            Assert.Equal(title, stored.Title);
            Assert.Equal(text, stored.Text);
            Assert.Equal(digest, stored.ContentSha256);
            Assert.Equal("CC0", stored.Licence);
        }
        var read = await CliProcess.RunAsync(Path.Combine(_root, "worker"), null, false,
            "show-corpus", corpusId, "--project", project, "--json");
        Assert.True(read.ExitCode == 0, read.FailureDetails);
        var detail = Json<CorpusDetailProjection>(read);
        Assert.Equal(corpusId, detail.CorpusId);
        Assert.Equal(digest, Assert.Single(detail.Documents).ContentSha256);
    }

    [Fact]
    public async Task ReleasedAddCorpusReturnsTypedJsonAndStoresItsProvenance()
    {
        var project = pristine.CopyProjectFile();
        var result = await CliProcess.RunAsync(Path.Combine(_root, "worker"), null, false,
            "add-corpus", "--project", project, "--id", "empty-corpus", "--description", "Local source",
            "--tokeniser", "whitespace", "--tokeniser-version", "1", "--licence", "CC0", "--json");
        Assert.True(result.ExitCode == 0, result.FailureDetails);
        Assert.Empty(result.Error);
        var added = Json<CorpusAddedResponse>(result);
        Assert.Equal("empty-corpus", added.CorpusId);
        Assert.Equal("Local source", added.Description);
        Assert.Equal("CC0", added.Licence);
        Assert.Equal("whitespace", added.Tokeniser);
        using var database = OpenDatabase(project);
        var stored = Assert.IsType<StoredCorpus>(new SqliteCorpusStore(database).Load(added.CorpusId));
        Assert.Empty(stored.Documents);
        Assert.Equal(added.Description, stored.Provenance.Origin.Description);
        Assert.Equal(added.Licence, stored.Provenance.Origin.Licence);
        Assert.Equal(added.Tokeniser, stored.Provenance.Tokenisation.Method);
        Assert.Equal(added.TokeniserVersion, stored.Provenance.Tokenisation.Version);
    }

    private static T Json<T>(CliProcessResult result) where T : class =>
        Assert.IsType<T>(ProjectionJson.Deserialize<T>(result.Output));

    private static string RecordAssessment(string project, bool firstWordParsed)
    {
        var id = CanonicalId.Mint("assessment/").Value;
        var selection = Selection.Create("executable smoke", ["motifa", "motifb"]);
        using var database = OpenDatabase(project);
        var words = selection.Words.Select((word, index) =>
        {
            var parsed = index != 0 || firstWordParsed;
            return new AssessedWord(word, parsed ? "analysed" : "no-analysis",
                parsed ? [new ParsedAnalysis(null, [], 0, "digest")] : []);
        }).ToArray();
        new AssessmentRepository(database).Record(new NewAssessmentRecord(
            id, null, null, "pangloss", "ParseTime", """{"words":[],"collect":[],"perWordLimitMs":1000,"perWordStepLimit":{"steps":200000,"isUnbounded":false}}""", "sha256:" + new string('a', 64),
            "none", "1", "{}", selection, "sha256:" + new string('b', 64), "sha256:" + new string('c', 64),
            "sha256:" + new string('d', 64), "model", "pipeline", 0, words));
        return id;
    }

    private static MotifDatabase OpenDatabase(string projectPath)
    {
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        return MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
