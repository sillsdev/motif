using System.Diagnostics;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class ProgressTrialCostTests(PristineProjectFixture pristine, ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-progress-trial-cost", Guid.NewGuid().ToString("N"));

    [Fact]
    public void TrialRunsWithAndWithoutAnAvailableProgressIndex()
    {
        Directory.CreateDirectory(_root);
        var without = Prepare(indexPresent: false);
        var with = Prepare(indexPresent: true);
        var watch = Stopwatch.StartNew();
        var first = DryRunJobRunner.Run(without.Path, "1.0", without.Proposal);
        watch.Stop();
        Assert.True(first.Succeeded, first.Refusal?.Message);
        var before = watch.Elapsed;
        watch.Restart();
        var second = DryRunJobRunner.Run(with.Path, "1.0", with.Proposal);
        watch.Stop();
        Assert.True(second.Succeeded, second.Refusal?.Message);
        output.WriteLine($"Trial without index: {before.TotalMilliseconds:F1} ms");
        output.WriteLine($"Trial with index: {watch.Elapsed.TotalMilliseconds:F1} ms");
    }

    private (string Path, string Proposal) Prepare(bool indexPresent)
    {
        var path = pristine.CopyProjectFile();
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        if (!indexPresent)
        {
            var removed = ProjectStoreCommand.Run(path, MotifProductVersion.CurrentText, (database, _) =>
            {
                using var connection = database.OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM ProgressTexts; DELETE FROM ProgressState;";
                command.ExecuteNonQuery();
                return CommandOutcome<string>.Success("removed");
            });
            Assert.True(removed.Succeeded, removed.Refusal?.Message);
        }
        var draft = indexPresent ? "with-progress" : "without-progress";
        Assert.True(ProposalCommands.New(new NewDraftRequest(path, "1.0", draft, null)).Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, "1.0", draft,
            CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value, NewLangProjFixture.AnalysisTag,
            SeededProject.FirstGloss + " comparison")).Succeeded);
        DraftRationale.Author(path, draft, "Compare a reading", "Measure the same Trial with each index state.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, "1.0", draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        return (path, finalized.Value!.ProposalId);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
