using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
public sealed class RealParserLimitTests
{
    [RealParserFact]
    public async Task AStepCappedWordFinishesUnderAHigherStepLimit()
    {
        using var project = new ConformanceProject();
        var client = RealCommandClient.Create(project.ManagedRoot);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var selection = new SelectionRequest(false, [], [ConformanceProject.NineHundredTwentyFour],
            false, null);

        var capped = await client.AssessAsync(new AssessRequest(project.FwDataPath, selection,
            PerWordLimitMs: 120_000, PerWordStepLimit: new StepCap(1)),
            new Progress<AssessmentProgress>(), timeout.Token);

        Assert.True(capped.Succeeded, capped.Refusal?.Message);
        var cappedWord = Assert.Single(capped.Value!.Words);
        Assert.Equal(ConformanceProject.NineHundredTwentyFour, cappedWord.Word);
        Assert.True(cappedWord.IsIncomplete);
        Assert.True(cappedWord.Morphology?.Capped);
        Assert.Contains("limit", cappedWord.CompletionStatus, StringComparison.OrdinalIgnoreCase);

        var finished = await client.AssessAsync(new AssessRequest(project.FwDataPath, selection,
            PerWordLimitMs: 120_000, PerWordStepLimit: new StepCap(100_000_000)),
            new Progress<AssessmentProgress>(), timeout.Token);

        Assert.True(finished.Succeeded, finished.Refusal?.Message);
        var finishedWord = Assert.Single(finished.Value!.Words);
        Assert.Equal(ConformanceProject.NineHundredTwentyFour, finishedWord.Word);
        Assert.False(finishedWord.IsIncomplete, finishedWord.CompletionStatus);
        Assert.Equal("analysed", finishedWord.Outcome);
        Assert.NotNull(finishedWord.Morphology);
        Assert.False(finishedWord.Morphology!.Capped);
        Assert.Equal(924, finishedWord.Morphology!.Analyses.Count);
        Assert.NotEqual(capped.Value.InvocationId, finished.Value.InvocationId);
    }
}
