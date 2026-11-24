using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class OverviewProposalViewTests
{
    internal static readonly BaselineToken Token = new(
        "project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

    [UiThreadTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpeningAProposalDoesNotMixItsEvidenceIntoProjectHealth(bool hasTrial)
    {
        var fake = new FakeCommandClient();
        var today = SampleOverview.Populated();
        fake.OnOverview((request, _) => Task.FromResult(CommandOutcome<OverviewResponse>.Success(
            request.ProposalId is null ? today : today with { Warnings = today.Warnings! with { Count = 999 } })));
        fake.ReadTrialCompletesWith(hasTrial ? Trial() : null);
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProgressPageModel(context);
        await context.OpenProjectAsync("project.fwdata");

        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        await context.EvidencePublication;

        Assert.Same(today, page.Health.Overview);
        Assert.DoesNotContain(fake.OverviewRequests, request => request.ProposalId is not null);
        Assert.DoesNotContain("999", page.Health.WarningText);
        Assert.Empty(fake.AssessRequests);
        await context.StopProjectWorkAsync();
    }

    [Fact]
    public void OpeningAProposalDoesNotPutItsTrialOnTheCurrentTextReader()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(
                configure: (fake, _) => fake.ReadTrialCompletesWith(Trial()));
            try
            {
                workspace.CurrentPage = WorkspacePage.Texts;
                PageScreenshots.Settle(window);
                var token = workspace.PageModel<TextsPageModel>().ResultsInText.SelectedToken;
                Assert.NotNull(token);
                Assert.False(token.ShowsProposalBlock);

                await workspace.Context.OpenProposalAsync("proposal/one", CancellationToken.None);
                await workspace.Context.EvidencePublication;

                Assert.False(token.ShowsProposalBlock);
                Assert.Empty(token.BoundProposalChoices);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    internal static TrialRecord Trial() => new("job/trial", "proposal/one", "sha256:intent", Token, null,
        new Dictionary<string, string> { [AssessmentKinds.ParseTime] = "assessment/trial" }, ["dogs"], false, null,
        JobStatus.Completed, DateTimeOffset.UtcNow, TrialFreshness.Current)
    {
        Work = new TrialWork(142, 380_000, 372_000, -2.1, [], 0, 0),
    };
}
