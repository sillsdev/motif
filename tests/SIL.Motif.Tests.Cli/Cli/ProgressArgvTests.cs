using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class ProgressArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-progress-argv", Guid.NewGuid().ToString("N"));

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
