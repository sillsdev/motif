using System.Security.Cryptography;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Model.AppliedLog;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ReadOnlyProjectOwnershipTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-read-ownership-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LogReadsSavedReceiptsWhileTheOriginalIsHeld(bool captureBeforeReceipt)
    {
        var path = pristine.CopyProjectFile();
        if (captureBeforeReceipt)
            Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root).Succeeded);
        new FieldWorksSimulator(path).SaveEdit(cache =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                ProjectAppliedLog.WriteEntry(cache, new AppliedLogEntry(
                    Guid.NewGuid(), 1, "20261002T000000Z", "receipt-writer", new string('a', 64), "Saved Apply receipt"))));
        var expected = ProposalCommands.Log(new LogRequest(path));
        Assert.True(expected.Succeeded, expected.Refusal?.Message);
        Assert.Equal("receipt-writer", Assert.Single(expected.Value!.Entries).User);
        var bytes = File.ReadAllBytes(path);
        var saved = File.GetLastWriteTimeUtc(path);
        using var owner = new FieldWorksSimulator(path).Hold();
        var actual = ProposalCommands.Log(new LogRequest(path));
        Assert.True(actual.Succeeded, actual.Refusal?.Message);
        Assert.Equal(ProjectionJson.Serialize(expected.Value), ProjectionJson.Serialize(actual.Value));
        var cli = await RunReadOnlyCli("log", "--project", path, "--json");
        Assert.Equal(ProjectionJson.Serialize(expected.Value),
            ProjectionJson.Serialize(ProjectionJson.Deserialize<AppliedLogProjection>(cli)));
        AssertOriginalUnchanged(path, bytes, saved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreflightChecksCollectedChangesWhileTheOriginalAndBaselineAreHeld(bool laterEdit)
    {
        var path = pristine.CopyProjectFile();
        Guid wordId = Guid.Empty;
        new FieldWorksSimulator(path).SaveEdit(cache =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("held-word", cache.DefaultVernWs)).Guid));
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var pending = PendingChanges.Load(new PendingChangesRequest(path, "1.0"));
        var added = PendingChanges.Put(new PutPendingChangeRequest(path, "1.0", pending.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling", CanonicalId.FromGuid(wordId).Value, "held-word")));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, "1.0", PendingChanges.DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        if (laterEdit)
            new FieldWorksSimulator(path).SaveEdit(cache =>
                NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                    cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(wordId).SpellingStatus = 1));
        var request = new PreflightRequest(path, "1.0", finalized.Value!.ProposalId);
        var expected = ProposalCommands.Preflight(request);
        Assert.True(expected.Succeeded, expected.Refusal?.Message);
        var fit = Assert.Single(expected.Value!.Changes);
        Assert.Equal(!laterEdit, fit.StillFits);
        if (laterEdit) Assert.Contains("spelling status", fit.Reason, StringComparison.OrdinalIgnoreCase);
        var bytes = File.ReadAllBytes(path);
        var saved = File.GetLastWriteTimeUtc(path);
        using var baselineOwner = new FieldWorksSimulator(captured.Value!.FwDataPath).Hold();
        using var owner = new FieldWorksSimulator(path).Hold();
        var actual = ProposalCommands.Preflight(request);
        Assert.True(actual.Succeeded, actual.Refusal?.Message);
        Assert.Equal(ProjectionJson.Serialize(expected.Value), ProjectionJson.Serialize(actual.Value));
        var cli = await RunReadOnlyCli("preflight", "--project", path, finalized.Value.ProposalId, "--json");
        Assert.Equal(ProjectionJson.Serialize(expected.Value),
            ProjectionJson.Serialize(ProjectionJson.Deserialize<PreflightResponse>(cli)));
        AssertOriginalUnchanged(path, bytes, saved);
    }

    private async Task<string> RunReadOnlyCli(params string[] arguments)
    {
        var childStore = Path.Combine(_root, "writing-systems-" + Guid.NewGuid().ToString("N"));
        var start = CliProcess.CreateStartInfo(_root, null, true, arguments);
        start.Environment[ProcessWritingSystemRepository.RepositoryPathVariable] = childStore;
        var processStore = Snapshot(ProcessWritingSystemRepository.BasePath);
        var machineStore = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SIL", "WritingSystemRepository");
        var machineBefore = Snapshot(machineStore);
        var result = await CliProcess.RunAsync(start);
        Assert.True(result.ExitCode == 0, result.Error + Environment.NewLine + result.Output);
        Assert.Equal(processStore, Snapshot(ProcessWritingSystemRepository.BasePath));
        Assert.Equal(machineBefore, Snapshot(machineStore));
        Assert.Empty(Snapshot(childStore));
        return result.Output;
    }

    private static string[] Snapshot(string root) => !Directory.Exists(root) ? [] :
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
            .ToArray();

    private static void AssertOriginalUnchanged(string path, byte[] bytes, DateTime saved)
    {
        Assert.True(File.Exists(path + ".lock"));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(saved, File.GetLastWriteTimeUtc(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
