using System.Text.Json;
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
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Model.AppliedLog;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group3)]
[Trait("MotifTestLevel", "Integration")]
public sealed class SameProjectApplyConcurrencyTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task ConcurrentApplyProcessesWriteOneReceiptAndOneAppliedProjectChange()
    {
        for (var iteration = 0; iteration < 10; iteration++)
            await ApplySamePendingChangeTwice(iteration);
    }

    private async Task ApplySamePendingChangeTwice(int iteration)
    {
        var word = $"concurrent-apply-{iteration}";
        var (path, wordformId) = ReleasedProjectWithWord(word);
        var root = Path.Combine(Path.GetDirectoryName(path)!, "same-project-apply-worker");
        Directory.CreateDirectory(root);
        var parser = FakeParser.Copy(root);
        var runner = IsolatedRunner.Process(root, parser);
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);

        var initial = PendingChanges.Load(new PendingChangesRequest(path, "1.0"));
        Assert.True(initial.Succeeded, initial.Refusal?.Message);
        var added = PendingChanges.Put(new PutPendingChangeRequest(path, "1.0", initial.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, word, OriginPage: "Texts")));
        Assert.True(added.Succeeded, added.Refusal?.Message);

        var measured = await PendingChangesWorkflow.Measure(new MeasurePendingRequest(
                path, added.Value!.DraftId!, added.Value.Revision, [word]),
            new Progress<MeasureProgress>(), CancellationToken.None, runnerLauncher: runner);
        Assert.True(measured.Succeeded, measured.Refusal?.Message);
        Assert.True(measured.Value!.EvidenceComplete);

        var first = CliProcess.Start(runner.Options, "apply", "--all-pending", "--project", path, "--json");
        var second = CliProcess.Start(runner.Options, "apply", "--all-pending", "--project", path, "--json");
        var firstRun = CliProcess.RunAsync(first);
        var secondRun = CliProcess.RunAsync(second);
        var outcomes = await Task.WhenAll(firstRun, secondRun);

        var appliedResponses = 0;
        foreach (var outcome in outcomes)
            appliedResponses += AssertDocumentedOutcome(outcome.ExitCode, outcome.Output, outcome.Error,
                iteration, added.Value.DraftId!);

        // A request past preflight can receive the first Apply's idempotent Receipt.
        Assert.InRange(appliedResponses, 1, 2);
        Assert.Equal(1L, ReceiptCount(path, added.Value.DraftId!));

        using var reopened = new FwDataProjectLoader().LoadScratchCache(path);
        var savedWordform = reopened.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(wordformId);
        Assert.Equal(2, savedWordform.SpellingStatus);
        var appliedLog = Assert.Single(ProjectAppliedLog.ReadAll(reopened));
        Assert.Equal(CanonicalId.Parse(added.Value.DraftId!).ToGuid(), appliedLog.ProposalId);
    }

    private static int AssertDocumentedOutcome(
        int exitCode, string output, string error, int iteration, string proposalId)
    {
        if (exitCode == 0)
        {
            Assert.Empty(error);
            using var response = JsonDocument.Parse(output);
            var result = response.RootElement;
            Assert.True(result.GetProperty("ok").GetBoolean());
            if (!result.GetProperty("applied").GetBoolean())
            {
                Assert.Equal("Nothing to apply.", result.GetProperty("summary").GetString());
                return 0;
            }

            Assert.Equal(proposalId, result.GetProperty("receipt").GetProperty("proposalId").GetString());
            return 1;
        }

        Assert.True(exitCode == 3,
            $"Iteration {iteration}: expected Busy exit code 3, got {exitCode}.\n" +
            $"Child stdout:\n{output}\nChild stderr:\n{error}");
        Assert.Empty(output);
        using var failure = JsonDocument.Parse(error);
        Assert.Equal("Busy", failure.RootElement.GetProperty("reason").GetString());
        var code = failure.RootElement.GetProperty("code").GetString();
        Assert.True(code is "project.in-use" or "apply.project-in-use",
            $"Unexpected Busy refusal code: {code}");
        return 0;
    }

    private static long ReceiptCount(string projectPath, string proposalId)
    {
        using var database = ProjectMotifDatabase.Open(projectPath);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $proposal;";
        command.Parameters.AddWithValue("$proposal", proposalId);
        return (long)command.ExecuteScalar()!;
    }

    private (string Path, Guid WordformId) ReleasedProjectWithWord(string word)
    {
        using var cache = pristine.NewScratch();
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs));
            wordform.SpellingStatus = 0;
            wordformId = wordform.Guid;
        });
        new FwDataProjectLoader().Save(cache);
        return (cache.ProjectId.Path, wordformId);
    }
}
