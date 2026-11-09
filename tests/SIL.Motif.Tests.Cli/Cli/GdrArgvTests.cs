using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class GdrArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-gdr-argv-" + Guid.NewGuid().ToString("N"));

    private string ProjectWithBaseline()
    {
        Directory.CreateDirectory(_root);
        string path;
        using (var cache = pristine.NewScratch())
        {
            SeededGdrs.Seed(cache);
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        return path;
    }

    [Fact]
    public async Task ListAndShowAgreeWithTheQueriesInJsonAndReadAsTextForAPerson()
    {
        var path = ProjectWithBaseline();

        var list = await CliProcess.RunAsync(_root, null, true, "gdr", "list", "--project", path, "--json");
        Assert.True(list.ExitCode == 0, list.Error);
        Assert.Equal(ProjectionJson.Serialize(GdrQuery.List(new GdrListRequest(path)).Value),
            ProjectionJson.Serialize(ProjectionJson.Deserialize<GdrListResponse>(list.Output)));

        var show = await CliProcess.RunAsync(_root, null, true, "gdr", "show", "--project", path, "GDR-1", "--json");
        Assert.True(show.ExitCode == 0, show.Error);
        Assert.Equal(ProjectionJson.Serialize(GdrQuery.Show(new GdrShowRequest(path, "GDR-1")).Value),
            ProjectionJson.Serialize(ProjectionJson.Deserialize<GdrShowResponse>(show.Output)));

        var text = await CliProcess.RunAsync(_root, null, true, "gdr", "show", "--project", path, "GDR-1");
        Assert.True(text.ExitCode == 0, text.Error);
        Assert.Contains("GDR-1  Word boundaries", text.Output);
        Assert.Contains("Words end at spaces", text.Output);
    }

    [Fact]
    public async Task ShowNeedsAnIdAndAnUnknownRecordIsRefused()
    {
        var path = ProjectWithBaseline();

        var missing = await CliProcess.RunAsync(_root, null, true, "gdr", "show", "--project", path);
        Assert.True(missing.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), missing.Error);

        var unknown = await CliProcess.RunAsync(_root, null, true, "gdr", "show", "--project", path, "GDR-99");
        Assert.True(unknown.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.NotFound), unknown.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
