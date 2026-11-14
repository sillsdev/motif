using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>The Source verbs that stage into the pending changes, as the command line runs them.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class SourceStageArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-source-stage-argv-" + Guid.NewGuid().ToString("N"));

    private string ProjectWithBaseline()
    {
        Directory.CreateDirectory(_root);
        string path;
        using (var cache = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root).Succeeded);
        return path;
    }

    private static int Invalid => FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument);

    [Fact]
    public async Task AddAndTheEditVerbsStageRowsInThePendingChangesAndWriteNothingToTheProject()
    {
        var path = ProjectWithBaseline();

        var add = await CliProcess.RunAsync(_root, null, true, "source", "add", "--project", path, "WEB",
            "--name", "World English Bible", "--versification", "English", "--context", "MAT-JHN", "--json");
        Assert.True(add.ExitCode == 0, add.Error);
        var staged = ProjectionJson.Deserialize<SourceStageResponse>(add.Output)!;
        Assert.Equal("WEB", staged.Source);
        Assert.Single(staged.Operations);

        foreach (var arguments in new[]
                 {
                     new[] { "set-name", "WEB", "WEB two" }, ["set-context", "WEB", "MAT-ACT"],
                     ["set-versification", "WEB", "Original"], ["remove", "WEB"], ["restore", "WEB"],
                 })
        {
            var run = await CliProcess.RunAsync(_root, null, true, ["source", arguments[0], "--project", path, .. arguments[1..]]);
            Assert.True(run.ExitCode == 0, arguments[0] + ": " + run.Error);
            Assert.Contains("Staged 1 change(s) for Source WEB", run.Output);
        }

        var pending = PendingChanges.Load(new PendingChangesRequest(path, "1.0")).Value!;
        Assert.Equal(["source-add", "source-set-name", "source-set-context", "source-set-versification", "source-restore"],
            pending.Changes.Select(change => change.Kind));
        var list = await CliProcess.RunAsync(_root, null, true, "source", "list", "--project", path, "--all", "--json");
        Assert.Empty(ProjectionJson.Deserialize<SourceListResponse>(list.Output)!.Sources);
    }

    [Fact]
    public async Task MissingFlagsAndArgumentsAreUsageErrorsAndABadValueIsRefusedByItsCode()
    {
        var path = ProjectWithBaseline();

        Assert.Equal(Invalid, (await CliProcess.RunAsync(_root, null, true, "source", "add", "--project", path, "WEB")).ExitCode);
        Assert.Equal(Invalid, (await CliProcess.RunAsync(_root, null, true, "source", "set-name", "--project", path, "WEB")).ExitCode);
        Assert.Equal(Invalid, (await CliProcess.RunAsync(_root, null, true, "source", "remove", "--project", path)).ExitCode);

        var badVersification = await CliProcess.RunAsync(_root, null, true, "source", "add", "--project", path, "WEB",
            "--name", "x", "--versification", "No such", "--context", "MAT", "--json");
        Assert.Equal(RefusalCodes.SourceVersificationUnknown, ProjectionJson.Deserialize<FailureEnvelope>(badVersification.Error)!.Code);
        var absentFolder = await CliProcess.RunAsync(_root, null, true, "source", "add", "--project", path, "WEB",
            "--name", "x", "--versification", "English", "--context", "MAT", "--folder", Path.Combine(_root, "absent"), "--json");
        Assert.Equal(RefusalCodes.SourceFolderMissing, ProjectionJson.Deserialize<FailureEnvelope>(absentFolder.Error)!.Code);
    }

    [Fact]
    public async Task ApplyIsABooleanFlagSoItDoesNotSwallowTheIdThatFollowsIt()
    {
        var path = ProjectWithBaseline();

        var run = await CliProcess.RunAsync(_root, null, true, "source", "remove", "--project", path, "--apply", "WEB",
            "--autonomous", "--json");

        Assert.Equal(RefusalCodes.ApplyMessageRequired, ProjectionJson.Deserialize<FailureEnvelope>(run.Error)!.Code);
        Assert.Empty(PendingChanges.Load(new PendingChangesRequest(path, "1.0")).Value!.Changes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
