using System.Diagnostics;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Drives the real <c>motif</c> apphost against a project whose writing system LibLCM cannot load. The refusal
/// must reach the caller as a setup problem naming the user's own project folder, whichever command meets it.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class WritingSystemMissingArgvTests(PristineProjectFixture pristine)
{
    private const string SeamVariable = "MOTIF_TEST_FAIL_WRITING_SYSTEM_FOR";

    // The second seam lets Preflight and the copies open and fails only Apply's own open of the project.
    [Theory]
    [InlineData(SeamVariable)]
    [InlineData("MOTIF_TEST_FAIL_LIVE_APPLY_WRITING_SYSTEM_FOR")]
    public async Task ApplyRefusesAsASetupProblemAndLeavesTheProjectAndReceiptsAlone(string seam)
    {
        var path = UniquelyNamedProject();
        const string draftName = "writing-system-missing-apply";
        var sense = CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value;
        Assert.True(ProposalCommands.New(new NewDraftRequest(path, "1.0", draftName, null)).Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            path, "1.0", draftName, sense, NewLangProjFixture.AnalysisTag, "setup gloss")).Succeeded);
        DraftRationale.Author(path, draftName, "Reword the gloss", "The refusal must not touch the project.");
        var proposalId = ProposalCommands.Finalize(new FinalizeRequest(path, "1.0", draftName)).Value!.ProposalId;
        Assert.True(DryRunJobRunner.Run(path, "1.0", proposalId).Succeeded);
        var bytesBefore = File.ReadAllBytes(path);
        var receiptsBefore = ReceiptCount(path);

        var root = Path.Combine(Path.GetDirectoryName(path)!, "writing-system-worker");
        var start = CliProcess.Start(IsolatedRunner.Options(root), "apply", proposalId, "--project", path, "--force", "--json");
        start.Environment[ProcessRunnerLauncher.SuppressVariable] = "1";
        start.Environment[CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable] = "1";
        start.Environment[seam] = seam == SeamVariable ? Path.GetFileName(path) : path;
        var (exitCode, output, error) = await CliProcess.RunAsync(start);

        Assert.True(exitCode == 2, $"exit {exitCode}; stderr: {error}; stdout: {output}");
        AssertSetupRefusal(error, path);
        Assert.DoesNotContain("roll", error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(output);
        Assert.Equal(bytesBefore, File.ReadAllBytes(path));
        Assert.Equal(receiptsBefore, ReceiptCount(path));
    }

    [Fact]
    public async Task BaselineCaptureRefusesAsASetupProblemNamingTheProjectsOwnStore()
    {
        var path = UniquelyNamedProject();
        var root = Path.Combine(Path.GetDirectoryName(path)!, "writing-system-capture-worker");

        var start = CliProcess.Start(IsolatedRunner.Options(root), "baseline", "capture", "--project", path, "--json");
        start.Environment[SeamVariable] = Path.GetFileName(path);
        var (exitCode, output, error) = await CliProcess.RunAsync(start);

        Assert.True(exitCode == 2, $"exit {exitCode}; stderr: {error}; stdout: {output}");
        AssertSetupRefusal(error, path);
        Assert.Empty(output);
        using var database = ProjectMotifDatabase.Open(path);
        var project = new ProjectLocator(Path.GetFullPath(path), Path.GetFileNameWithoutExtension(path));
        Assert.Null(new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project)));
    }

    [Fact]
    public async Task ApplyAllPendingRefusesAsASetupProblemAndRecordsNoReceipt()
    {
        var (path, wordformId) = UniquelyNamedProjectWithWord("setup-word");
        var root = Path.Combine(Path.GetDirectoryName(path)!, "writing-system-pending-worker");
        var runner = IsolatedRunner.Process(root, FakeParser.Copy(root));
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var initial = PendingChanges.Load(new PendingChangesRequest(path, "1.0"));
        var added = PendingChanges.Put(new PutPendingChangeRequest(path, "1.0", initial.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "setup-word", OriginPage: "Texts")));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        var measured = await PendingChangesWorkflow.Measure(new MeasurePendingRequest(
            path, added.Value!.DraftId!, added.Value.Revision, ["setup-word"]),
            new Progress<MeasureProgress>(), CancellationToken.None, runnerLauncher: runner);
        Assert.True(measured.Succeeded, measured.Refusal?.Message);
        var bytesBefore = File.ReadAllBytes(path);
        var receiptsBefore = ReceiptCount(path);

        var start = CliProcess.Start(runner.Options, "apply", "--all-pending", "--project", path, "--json");
        start.Environment[ProcessRunnerLauncher.SuppressVariable] = "1";
        start.Environment[SeamVariable] = Path.GetFileName(path);
        var (exitCode, output, error) = await CliProcess.RunAsync(start);

        Assert.True(exitCode == 2, $"exit {exitCode}; stderr: {error}; stdout: {output}");
        AssertSetupRefusal(error, path);
        Assert.Empty(output);
        Assert.Equal(bytesBefore, File.ReadAllBytes(path));
        Assert.Equal(receiptsBefore, ReceiptCount(path));
    }

    // These reads open a private copy that is deleted before the refusal reaches the caller.
    [Theory]
    [InlineData("open")]
    [InlineData("analyses")]
    [InlineData("log")]
    public async Task AReadNamesTheProjectsOwnStoreNeverItsDeletedCopy(string verb)
    {
        var path = UniquelyNamedProject();
        var root = Path.Combine(Path.GetDirectoryName(path)!, "writing-system-read-worker");
        var start = verb == "open"
            ? CliProcess.Start(IsolatedRunner.Options(root), "open", path, "--json")
            : CliProcess.Start(IsolatedRunner.Options(root), verb, "--project", path, "--json");
        start.Environment[ProcessRunnerLauncher.SuppressVariable] = "1";
        start.Environment[CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable] = "1";
        start.Environment[SeamVariable] = Path.GetFileName(path);
        var (exitCode, output, error) = await CliProcess.RunAsync(start);

        Assert.True(exitCode == 2, $"exit {exitCode}; stderr: {error}; stdout: {output}");
        AssertSetupRefusal(error, path);
        Assert.DoesNotContain("SIL.Motif.BaselineRead", error, StringComparison.Ordinal);
        Assert.DoesNotContain("SIL.Motif.ProjectRead", error, StringComparison.Ordinal);
    }

    private static void AssertSetupRefusal(string error, string path)
    {
        var failure = ProjectionJson.Deserialize<FailureEnvelope>(error)!;
        var store = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "WritingSystemStore");
        Assert.Equal("project.writing-system-missing", failure.Code);
        Assert.Equal(FailureReason.Refused, failure.Reason);
        Assert.Contains("WritingSystemStore", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(store, failure.Message, StringComparison.Ordinal);
        Assert.Equal(store, failure.Detail!["projectWritingSystemStore"]);
        Assert.DoesNotContain("rolls back", error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("damaged", error, StringComparison.OrdinalIgnoreCase);
    }

    private string UniquelyNamedProject()
    {
        var copy = pristine.CopyProjectFile();
        var unique = Path.Combine(Path.GetDirectoryName(copy)!, "Wsfail" + Guid.NewGuid().ToString("N") + ".fwdata");
        File.Move(copy, unique);
        return unique;
    }

    private (string Path, Guid WordformId) UniquelyNamedProjectWithWord(string word)
    {
        Guid wordformId = Guid.Empty;
        string scratchPath;
        using (var cache = pristine.NewScratch())
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs)).Guid);
            new FwDataProjectLoader().Save(cache);
            scratchPath = cache.ProjectId.Path;
        }
        var unique = Path.Combine(Path.GetDirectoryName(scratchPath)!, "Wsfail" + Guid.NewGuid().ToString("N") + ".fwdata");
        File.Move(scratchPath, unique);
        return (unique, wordformId);
    }

    private static long ReceiptCount(string path)
    {
        using var database = ProjectMotifDatabase.Open(path);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Receipts;";
        return (long)command.ExecuteScalar()!;
    }
}
