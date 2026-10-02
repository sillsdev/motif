using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class OverviewRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task OpeningAProjectShowsItsStoredNumbersAndStartsNoParser()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        var selected = await project.Client.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
            project.FwDataPath, "First words", [], [SeededProject.FirstForm]), CancellationToken.None);
        Assert.True(selected.Succeeded, selected.Refusal?.Message);
        var assessed = await project.Client.AssessAsync(new AssessRequest(project.FwDataPath,
            new SelectionRequest(false, [], [SeededProject.FirstForm], false, null),
            PerWordLimitMs: 1_000, PerWordStepLimit: new StepCap(3_100)),
            new Progress<AssessmentProgress>(), CancellationToken.None);
        Assert.True(assessed.Succeeded, assessed.Refusal?.Message);
        var callsBeforeRead = project.Invocations().ToArray();

        var overview = await project.Client.OverviewAsync(
            new OverviewRequest(project.FwDataPath), CancellationToken.None);
        Assert.True(overview.Succeeded, overview.Refusal?.Message);
        Assert.True(overview.Value!.SelectionResolved);
        Assert.Equal(1, overview.Value.SelectionWordCount);
        Assert.NotNull(overview.Value.AssessmentId);
        Assert.True(overview.Value.WordformCount > 0);
        Assert.Equal(callsBeforeRead, project.Invocations());
    }
}
