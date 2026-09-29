using System.Diagnostics;
using SIL.Motif.App.ViewModels;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ConformanceFixtureTests
{
    [Fact]
    public void CopiedConformanceProjectLoadsWithThirteenLexicalEntries()
    {
        using var project = new ConformanceProject();
        using var cache = new FwDataProjectLoader().LoadScratchCache(project.FwDataPath);

        var entries = cache.ServiceLocator.GetInstance<ILexEntryRepository>();
        Assert.Equal(13, entries.Count);
    }

    [RealParserFact]
    public void DisposingWithAnAssessmentInFlightCancelsItWithoutBlockingTheDispatcher()
    {
        using var project = new ConformanceProject();
        var deadline = Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency;
        RunState? assessStateAfterDispose = null;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath);
            var workspace = walkthrough.Workspace;
            try
            {
                WalkthroughSteps.ChooseConformanceProjectAndCaptureBaseline(walkthrough, deadline);
                WalkthroughSteps.StartSlowAssessment(walkthrough, deadline);
            }
            finally
            {
                walkthrough.Dispose();
                assessStateAfterDispose = workspace.Assess.State;
            }

            Assert.Equal(RunState.Cancelled, assessStateAfterDispose);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }
}
