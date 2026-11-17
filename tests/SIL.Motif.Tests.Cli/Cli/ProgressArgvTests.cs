using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class ProgressArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-progress-argv", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAndOlderStoresRemainUnchanged(bool older)
    {
        Directory.CreateDirectory(_root);
        var path = pristine.CopyProjectFile();
        var store = Path.ChangeExtension(path, ".motif.db");
        if (older)
        {
            using var database = ProjectMotifDatabase.Open(path);
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {MotifSchema.CurrentSchema - 1};";
            command.ExecuteNonQuery();
        }
        string[] StoreFiles() => Directory.GetFiles(Path.GetDirectoryName(store)!, Path.GetFileName(store) + "*")
            .Where(file => !file.EndsWith("-wal", StringComparison.Ordinal) && !file.EndsWith("-shm", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        var files = StoreFiles().ToDictionary(file => file, File.ReadAllBytes);
        var project = File.ReadAllBytes(path);
        var start = CliProcess.CreateStartInfo(_root, null, false, "progress", "--project", path, "--json");
        CliProcess.EnableAdvancedAiMode(start, _root);
        var result = await CliProcess.RunAsync(start);
        Assert.Equal(older ? RefusalCodes.StoreOtherVersion : RefusalCodes.ProgressIndexNotReady,
            ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!.Code);
        Assert.Equal(files.Keys.Order(StringComparer.Ordinal), StoreFiles());
        foreach (var (file, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(file));
        Assert.Equal(project, File.ReadAllBytes(path));
        Assert.Equal(older, File.Exists(store));
    }

    [Fact]
    public async Task SourceScopeReportsMissingIndexWithoutCreatingStore()
    {
        Directory.CreateDirectory(_root);
        var path = pristine.CopyProjectFile();
        var start = CliProcess.CreateStartInfo(_root, null, false, "progress", "--project", path, "--source", "WEB", "--json");
        CliProcess.EnableAdvancedAiMode(start, _root);
        var result = await CliProcess.RunAsync(start);
        Assert.Equal(RefusalCodes.SourceIndexMissing, ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!.Code);
        Assert.False(File.Exists(Path.ChangeExtension(path, ".motif.db")));
    }

    [Fact]
    public async Task SummaryAndPageMatchTheSharedHandlerAndUnknownOptionsFail()
    {
        Directory.CreateDirectory(_root);
        var path = pristine.CopyProjectFile();
        using (var cache = new FwDataProjectLoader().LoadScratchCache(path))
        {
            SeededProject.SeedText(cache, pristine.Seed);
            new FwDataProjectLoader().Save(cache);
        }
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        foreach (var detail in new[] { "summary", "passages" })
        {
            var start = CliProcess.CreateStartInfo(_root, null, false,
                "progress", "--project", path, "--detail", detail, "--json");
            CliProcess.EnableAdvancedAiMode(start, _root);
            var cli = await CliProcess.RunAsync(start);
            Assert.True(cli.ExitCode == 0, cli.Error + cli.Output);
            var expected = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts"), detail));
            Assert.True(expected.Succeeded, expected.Refusal?.Message);
            Assert.Equal(ProjectionJson.Serialize(expected.Value), ProjectionJson.Serialize(
                ProjectionJson.Deserialize<ProgressResponse>(cli.Output)));
        }
        var invalid = CliProcess.CreateStartInfo(_root, null, false,
            "progress", "--project", path, "--trial", "example", "--json");
        CliProcess.EnableAdvancedAiMode(invalid, _root);
        var rejected = await CliProcess.RunAsync(invalid);
        Assert.NotEqual(0, rejected.ExitCode);

        var page = CliProcess.CreateStartInfo(_root, null, false,
            "progress", "--project", path, "--detail", "passages");
        CliProcess.EnableAdvancedAiMode(page, _root);
        var rendered = await CliProcess.RunAsync(page);
        Assert.True(rendered.ExitCode == 0, rendered.Error + rendered.Output);
        Assert.Contains("tokens ", rendered.Output);
        Assert.Contains("; types ", rendered.Output);

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
        var stale = CliProcess.CreateStartInfo(_root, null, false, "progress", "--project", path);
        CliProcess.EnableAdvancedAiMode(stale, _root);
        var text = await CliProcess.RunAsync(stale);
        Assert.True(text.ExitCode == 0, text.Error + text.Output);
        Assert.Contains("Judgements may be stale", text.Output);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
