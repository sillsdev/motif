using System.Diagnostics;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheTestCollection.Name)]
public sealed class AgentChangesArgvTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-agent-changes-" + Guid.NewGuid().ToString("N"));
    private readonly string _workerRoot;
    private readonly PristineProjectFixture _pristine;

    public AgentChangesArgvTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        _workerRoot = Path.Combine(_root, "worker");
        Directory.CreateDirectory(_workerRoot);
    }

    [Fact]
    public async Task PutListRecheckAndRemoveRoundTripThroughTheExecutable()
    {
        var project = _pristine.CopyProjectFile();
        var wordform = AddWordform(project, "agent-roundtrip-word");
        await CaptureBaseline(project);
        var initial = await ReadPending(project);
        Assert.Equal("none", initial.Revision);
        Assert.Empty(initial.Changes);

        var changeId = "agent-change-" + Guid.NewGuid().ToString("N");
        var put = await RunAsync(true, "put-pending-change", "--project", project,
            "--expected-revision", initial.Revision, "--change-id", changeId,
            "--kind", "incorrect-spelling", "--word", "agent-roundtrip-word",
            "--wordform-id", wordform, "--json");
        var added = SuccessfulSnapshot(put);
        Assert.Contains(added.Changes, change => change.ChangeId == changeId && change.Word == "agent-roundtrip-word");

        var listed = await ReadPending(project);
        Assert.Equal(added.Revision, listed.Revision);
        Assert.Contains(listed.Changes, change => change.ChangeId == changeId);

        var recheckedResult = await RunAsync(true, "recheck-pending-changes", "--project", project,
            "--expected-revision", listed.Revision, "--json");
        var rechecked = SuccessfulSnapshot(recheckedResult);
        Assert.Contains(rechecked.Changes, change => change.ChangeId == changeId);

        var removedResult = await RunAsync(true, "remove-pending-change", "--project", project,
            "--expected-revision", rechecked.Revision, "--change-id", changeId, "--json");
        var removed = SuccessfulSnapshot(removedResult);
        Assert.Empty(removed.Changes);
        Assert.NotNull(removed.DraftId);
    }

    [Fact]
    public async Task AStaleRevisionIsRefusedAndARetryLandsOnce()
    {
        var project = _pristine.CopyProjectFile();
        var firstWordform = AddWordform(project, "agent-stale-word-a");
        var secondWordform = AddWordform(project, "agent-stale-word-b");
        await CaptureBaseline(project);
        var initial = await ReadPending(project);
        var firstId = "agent-change-a-" + Guid.NewGuid().ToString("N");
        var secondId = "agent-change-b-" + Guid.NewGuid().ToString("N");

        var first = SuccessfulSnapshot(await Put(project, initial.Revision, firstId,
            "agent-stale-word-a", firstWordform));
        var stale = await Put(project, initial.Revision, secondId, "agent-stale-word-b", secondWordform);
        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), stale.ExitCode);
        var refusal = ProjectionJson.Deserialize<FailureEnvelope>(stale.Error)!;
        Assert.Equal("change.revision-conflict", refusal.Code);

        var afterRefusal = await ReadPending(project);
        Assert.Single(afterRefusal.Changes);
        Assert.Contains(afterRefusal.Changes, change => change.ChangeId == firstId);

        var retried = SuccessfulSnapshot(await Put(project, afterRefusal.Revision, secondId,
            "agent-stale-word-b", secondWordform));
        Assert.Equal(2, retried.Changes.Count);
        Assert.Single(retried.Changes, change => change.ChangeId == secondId);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task CaptureBaseline(string project)
    {
        var result = await RunAsync(true, "baseline", "capture", project, "--json");
        Assert.Equal(0, result.ExitCode);
    }

    private async Task<PendingChangesSnapshot> ReadPending(string project)
    {
        var result = await RunAsync(true, "pending-changes", "--project", project, "--json");
        return SuccessfulSnapshot(result);
    }

    private Task<CliRun> Put(string project, string revision, string changeId, string word, string wordformId) =>
        RunAsync(true, "put-pending-change", "--project", project, "--expected-revision", revision,
            "--change-id", changeId, "--kind", "incorrect-spelling", "--word", word,
            "--wordform-id", wordformId, "--json");

    private static PendingChangesSnapshot SuccessfulSnapshot(CliRun result)
    {
        Assert.True(result.ExitCode == 0, $"CLI exited {result.ExitCode}: {result.Error}{result.Output}");
        return ProjectionJson.Deserialize<PendingChangesSnapshot>(result.Output)!;
    }

    private async Task<CliRun> RunAsync(bool developerCommands, params string[] arguments)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment[RunnerOptions.RootVariable] = _workerRoot;
        start.Environment.Remove("MOTIF_DEVELOPER_COMMANDS");
        if (developerCommands) start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        return new CliRun(process.ExitCode, await outputTask, await errorTask);
    }

    private static string AddWordform(string project, string word)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(project);
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(cache);
        return SIL.Motif.Contract.Ids.CanonicalId.FromGuid(wordformId).Value;
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);
}
