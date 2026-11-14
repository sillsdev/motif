using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Scripture;
using SIL.Motif.Tests.Scripture;
using SIL.Motif.Tests.Scripture.Fixtures;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class SourceArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-source-argv-" + Guid.NewGuid().ToString("N"));

    private string ProjectWithBaseline()
    {
        Directory.CreateDirectory(_root);
        string path;
        using (var cache = pristine.NewScratch())
        {
            SeededSourceList.AddList(cache,
                new("WEB", "World English Bible", "GEN-ROM; 1CO 13-15", "Paratext", "English"),
                new("OLD", "Old", "GEN", "Paratext", "English", "Removed"));
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        return path;
    }

    [Fact]
    public async Task ListShowAndWhereAgreeWithTheQueriesInJsonAndReadAsTextForAPerson()
    {
        var path = ProjectWithBaseline();

        var list = await CliProcess.RunAsync(_root, null, true, "source", "list", "--project", path, "--json");
        Assert.True(list.ExitCode == 0, list.Error);
        Assert.Equal(ProjectionJson.Serialize(SourceQuery.List(new SourceListRequest(path)).Value),
            ProjectionJson.Serialize(ProjectionJson.Deserialize<SourceListResponse>(list.Output)));
        Assert.Equal(["WEB"], ProjectionJson.Deserialize<SourceListResponse>(list.Output)!.Sources.Select(source => source.Id));

        var all = await CliProcess.RunAsync(_root, null, true, "source", "list", "--project", path, "--all", "--json");
        Assert.Equal(["WEB", "OLD"], ProjectionJson.Deserialize<SourceListResponse>(all.Output)!.Sources.Select(source => source.Id));

        var show = await CliProcess.RunAsync(_root, null, true, "source", "show", "--project", path, "WEB", "--json");
        Assert.True(show.ExitCode == 0, show.Error);
        Assert.Equal(ProjectionJson.Serialize(SourceQuery.Show(new SourceShowRequest(path, "WEB")).Value),
            ProjectionJson.Serialize(ProjectionJson.Deserialize<SourceShowResponse>(show.Output)));

        var where = await CliProcess.RunAsync(_root, null, true, "source", "where", "--project", path, "MAT 5", "--source", "WEB", "--json");
        Assert.True(where.ExitCode == 0, where.Error);
        Assert.Equal("InContext", Assert.Single(ProjectionJson.Deserialize<SourceWhereResponse>(where.Output)!.Places).Place);

        var text = await CliProcess.RunAsync(_root, null, true, "source", "list", "--project", path);
        Assert.True(text.ExitCode == 0, text.Error);
        Assert.Contains("WEB  World English Bible  [Active]  GEN-ROM; 1CO 13-15", text.Output);
        Assert.DoesNotContain("OLD", text.Output);
    }

    [Fact]
    public async Task CurrentGetReadsTheLocalFileThisProcessIsPointedAt()
    {
        var path = ProjectWithBaseline();
        var none = await CliProcess.RunAsync(_root, null, true, "current", "get", "--project", path);
        Assert.True(none.ExitCode == 0, none.Error);
        Assert.Contains("No Current chapter", none.Output);

        var key = ProjectStoreCommand.Run<string>(path, MotifProductVersion.CurrentText,
            (_, project) => CommandOutcome<string>.Success(ProjectWorkspaceKey.Compute(project))).Value!;
        new ScriptureLocalStore(ProcessScriptureLocalPathInitializer.Path).SetCurrent(key, "WEB", new ChapterRef("MAT", 5));

        var set = await CliProcess.RunAsync(_root, null, true, "current", "get", "--project", path, "--json");
        var current = Assert.Single(ProjectionJson.Deserialize<CurrentGetResponse>(set.Output)!.Current);
        Assert.Equal(("WEB", "MAT 5", "InContext"), (current.Source, current.Chapter, current.Place));
    }

    [Fact]
    public async Task MissingArgumentsAreUsageErrorsAndAnUnknownSourceOrChapterIsRefused()
    {
        var path = ProjectWithBaseline();

        var noId = await CliProcess.RunAsync(_root, null, true, "source", "show", "--project", path);
        Assert.True(noId.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), noId.Error);
        var noVerb = await CliProcess.RunAsync(_root, null, true, "source", "--project", path);
        Assert.True(noVerb.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), noVerb.Error);

        var unknown = await CliProcess.RunAsync(_root, null, true, "source", "show", "--project", path, "NOPE");
        Assert.True(unknown.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.NotFound), unknown.Error);
        var badRef = await CliProcess.RunAsync(_root, null, true, "source", "where", "--project", path, "MAT");
        Assert.True(badRef.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), badRef.Error);
    }

    [Fact]
    public async Task SetFolderAndCurrentSetThenClearRoundTripThroughTheLocalFileInJsonAndText()
    {
        var path = ProjectWithBaseline();
        using var tiny = TinyProject.Create();
        var key = ProjectStoreCommand.Run<string>(path, MotifProductVersion.CurrentText,
            (_, project) => CommandOutcome<string>.Success(ProjectWorkspaceKey.Compute(project))).Value!;
        var store = new ScriptureLocalStore(ProcessScriptureLocalPathInitializer.Path);

        var folder = await CliProcess.RunAsync(_root, null, true, "source", "set-folder", "--project", path, "web", tiny.Folder, "--json");
        Assert.True(folder.ExitCode == 0, folder.Error);
        Assert.Equal(("WEB", Path.GetFullPath(tiny.Folder)), (ProjectionJson.Deserialize<SourceLocalResponse>(folder.Output)!.Source,
            ProjectionJson.Deserialize<SourceLocalResponse>(folder.Output)!.Folder));
        Assert.Equal(Path.GetFullPath(tiny.Folder), store.Read(key).SourcePaths["WEB"]);

        var set = await CliProcess.RunAsync(_root, null, true, "current", "set", "--project", path, "WEB", "GEN 2");
        Assert.True(set.ExitCode == 0, set.Error);
        Assert.Contains("current: GEN 2", set.Output);
        Assert.Equal(new ChapterRef("GEN", 2), store.Read(key).Current["WEB"]);

        var tooFar = await CliProcess.RunAsync(_root, null, true, "current", "set", "--project", path, "WEB", "MAT 99");
        Assert.True(tooFar.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), tooFar.Error);

        var clearCurrent = await CliProcess.RunAsync(_root, null, true, "current", "set", "--project", path, "WEB", "--clear");
        Assert.True(clearCurrent.ExitCode == 0, clearCurrent.Error);
        Assert.Empty(store.Read(key).Current);
        var clearFolder = await CliProcess.RunAsync(_root, null, true, "source", "clear-folder", "--project", path, "WEB");
        Assert.True(clearFolder.ExitCode == 0, clearFolder.Error);
        Assert.Empty(store.Read(key).SourcePaths);
    }

    [Fact]
    public async Task LocalSettersRefuseAnUnknownSourceAMissingFolderAndIncompleteArguments()
    {
        var path = ProjectWithBaseline();

        var unknown = await CliProcess.RunAsync(_root, null, true, "source", "set-folder", "--project", path, "NOPE", _root);
        Assert.True(unknown.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.NotFound), unknown.Error);
        var absent = await CliProcess.RunAsync(_root, null, true, "source", "set-folder", "--project", path, "WEB", Path.Combine(_root, "absent"));
        Assert.True(absent.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.NotFound), absent.Error);
        var noPath = await CliProcess.RunAsync(_root, null, true, "source", "set-folder", "--project", path, "WEB");
        Assert.True(noPath.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), noPath.Error);
        var noRef = await CliProcess.RunAsync(_root, null, true, "current", "set", "--project", path, "WEB");
        Assert.True(noRef.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), noRef.Error);
        var badRef = await CliProcess.RunAsync(_root, null, true, "current", "set", "--project", path, "WEB", "MAT");
        Assert.True(badRef.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), badRef.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
