using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
public sealed class TimingRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task ARerunOfChosenWordsReplacesOnlyThoseWords()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        project.Behave(new { words = new[]
        {
            new { word = "motifa", outcome = "capped" },
            new { word = "motifb", outcome = "timed-out" },
        } });
        var initial = await Assess(project, ["motifa", "motifb"], 1_000);
        var initialId = initial.Measurements.Single(row => row.Kind == "ParseTime").AssessmentId;
        string untouchedOutcome;
        int? untouchedElapsedMs;
        using (var initialDatabase = ProjectMotifDatabase.Open(project.FwDataPath))
        {
            var stored = new AssessmentRepository(initialDatabase).Get(initialId);
            var untouched = stored.Words!.Single(row => row.Word == "motifb");
            untouchedOutcome = untouched.Outcome;
            untouchedElapsedMs = untouched.ElapsedMs;
            Assert.Equal(1_000, stored.Invocation!.PerWordTimeoutMs);
        }
        var before = await project.Client.TimingAsync(
            new TimingRequest(project.FwDataPath, initialId), CancellationToken.None);
        Assert.True(before.Succeeded, before.Refusal?.Message);
        Assert.Equal(2, before.Value!.WordCount);

        project.Behave(new { words = new[] { new { word = "motifa", outcome = "complete" } } });
        var rerun = await Assess(project, ["motifa"], 3_000);
        var rerunId = rerun.Measurements.Single(row => row.Kind == "ParseTime").AssessmentId;
        var after = await project.Client.TimingAsync(new TimingRequest(project.FwDataPath, initialId,
            OverrideAssessmentIds: [rerunId]), CancellationToken.None);

        Assert.True(after.Succeeded, after.Refusal?.Message);
        Assert.Equal(2, after.Value!.WordCount);
        Assert.NotEqual(before.Value.Words.Single(row => row.Word == "motifa").Completion,
            after.Value.Words.Single(row => row.Word == "motifa").Completion);
        Assert.Equal(before.Value.Words.Single(row => row.Word == "motifb"),
            after.Value.Words.Single(row => row.Word == "motifb"));
        Assert.Equal("capped", initial.Words.Single(row => row.Word == "motifa").Outcome);
        Assert.Equal("analysed", Assert.Single(rerun.Words).Outcome);
        Assert.Equal("timed-out", initial.Words.Single(row => row.Word == "motifb").Outcome);
        using var database = ProjectMotifDatabase.Open(project.FwDataPath);
        var assessments = new AssessmentRepository(database);
        var original = assessments.Get(initialId);
        var changed = assessments.Get(rerunId);
        Assert.Equal(1_000, original.Invocation!.PerWordTimeoutMs);
        Assert.Equal(3_000, changed.Invocation!.PerWordTimeoutMs);
        var originalWords = original.Words ?? throw new InvalidOperationException("The original Assessment has no words.");
        var changedWords = changed.Words ?? throw new InvalidOperationException("The rerun Assessment has no words.");
        Assert.Equal(["motifa", "motifb"], originalWords.Select(row => row.Word));
        Assert.Equal("motifa", Assert.Single(changedWords).Word);
        var stillUntouched = originalWords.Single(row => row.Word == "motifb");
        Assert.Equal(untouchedOutcome, stillUntouched.Outcome);
        Assert.Equal(untouchedElapsedMs, stillUntouched.ElapsedMs);
    }

    private static async Task<AssessCommandResponse> Assess(GrammarClientProject project, string[] words,
        int perWordLimitMs)
    {
        var assessed = await project.Client.AssessAsync(new AssessRequest(project.FwDataPath,
            new SelectionRequest(false, [], words, false, null),
            PerWordLimitMs: perWordLimitMs, PerWordStepLimit: new StepCap(3_100)),
            new Progress<AssessmentProgress>(), CancellationToken.None);
        Assert.True(assessed.Succeeded, assessed.Refusal?.Message);
        return assessed.Value!;
    }
}
