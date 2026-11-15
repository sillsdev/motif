using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class OpenProposalDocumentsTests
{
    [Fact]
    public async Task ASelectedDocumentStaysVisibleWhenItMovesIntoClosedHistory()
    {
        var fake = new FakeCommandClient();
        var status = "proposed";
        ProposalCard Card(string id) => new(id, id, "proposed", ProposalGroup.WaitingForYou,
            false, [], TrialStanding.Current, null, []);
        fake.ProposalStandingsCompleteWith(new([Card("one"), Card("two")], 0));
        fake.ShowProposalHandler = (request, _) => Task.FromResult(
            SIL.Motif.Contract.Commands.CommandOutcome<ProposalDetailProjection>.Success(
                new(request.ProposalId, request.ProposalId == "one" ? status : "proposed",
                    request.ProposalId, null, "intent", [])));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync("project.fwdata");
        await page.DetailReading;
        var selected = page.SelectedDocument!;
        Assert.Equal("one", selected.ProposalId);
        status = "applied";
        fake.ProposalStandingsCompleteWith(new([Card("two")], 1));
        await context.PublishBaselineCapturedAsync(openFirstRunSetup: false);
        await page.DetailReading;
        Assert.Same(selected, page.SelectedDocument);
        Assert.Equal("applied", selected.Detail!.Status);
        Assert.False(selected.CanAddComment);
        await context.StopProjectWorkAsync();
    }

    [Fact]
    public async Task ThreeDocumentsKeepTheirOwnEditorAndDifferenceWhenSelectionChanges()
    {
        var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
        await context.OpenProjectAsync("/tmp/w5.fwdata");
        var documents = new OpenProposalDocuments(context);
        var one = documents.Open("one");
        var two = documents.Open("two");
        var three = documents.Open("three");
        await Task.WhenAll(one.RefreshAsync(), two.RefreshAsync(), three.RefreshAsync());
        documents.Select(one);
        Assert.Equal(3, documents.Items.Count);
        Assert.Same(one, documents.Selected);
        Assert.Equal("one", one.RationaleEditor!.ProposalId);
        Assert.Equal("two", two.RationaleEditor!.ProposalId);
        Assert.NotSame(one.Difference, two.Difference);
        Assert.Same(three, documents.Open("three"));
        await documents.StopAsync();
        await context.StopProjectWorkAsync();
    }

    [Fact]
    public async Task ComparisonChecksAreIndependentAndAnUnnoticedThirdCannotReplaceThePair()
    {
        var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
        await context.OpenProjectAsync("/tmp/w5.fwdata");
        var documents = new OpenProposalDocuments(context);
        var one = documents.Open("one");
        var two = documents.Open("two");
        var three = documents.Open("three");
        Assert.True(documents.Check(one, true));
        Assert.True(documents.Check(two, true));
        Assert.False(documents.Check(three, true));
        Assert.Same(three, documents.Selected);
        Assert.Equal(new[] { one, two }, documents.ComparisonPair);
        Assert.True(documents.CanCompare);
        Assert.True(documents.Check(one, false));
        Assert.False(documents.CanCompare);
        await documents.StopAsync();
    }
    [Fact]
    public async Task InvalidationRejectsALateReadWithoutBlockingAnotherDocument()
    {
        var fake = new FakeCommandClient();
        var late = new TaskCompletionSource<SIL.Motif.Contract.Commands.CommandOutcome<ProposalDetailProjection>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.ShowProposalHandler = (request, _) => request.ProposalId == "one" ? late.Task : Task.FromResult(
            SIL.Motif.Contract.Commands.CommandOutcome<ProposalDetailProjection>.Success(
                new(request.ProposalId, "proposed", "Second", null, "digest", [])));
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var documents = new OpenProposalDocuments(context);
        var one = documents.Open("one");
        var reading = one.RefreshAsync();
        var two = documents.Open("two");
        await two.RefreshAsync();
        Assert.Equal("Second", two.Detail!.Label);
        Assert.True(one.IsLoading);
        context.Selection.PastedWords = "another word";
        late.SetResult(SIL.Motif.Contract.Commands.CommandOutcome<ProposalDetailProjection>.Success(
            new("one", "proposed", "Late", null, "digest", [])));
        await reading;
        Assert.Null(one.Detail);
        Assert.True(one.IsOutdated);
        Assert.False(one.CanApply);
        Assert.Equal("Second", two.Detail.Label);
        await documents.StopAsync();
    }

    [Fact]
    public async Task AnApplyInvalidatesEveryOpenCandidatesEvidence()
    {
        var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
        await context.OpenProjectAsync("project.fwdata");
        var documents = new OpenProposalDocuments(context);
        var one = documents.Open("one");
        var two = documents.Open("two");
        await documents.RefreshAsync();
        context.RecordApplied();
        Assert.True(one.IsOutdated);
        Assert.True(two.IsOutdated);
        Assert.False(one.CanApply);
        Assert.False(two.CanApply);
        Assert.False(one.Evidence.CanShowFacts);
        Assert.False(two.Evidence.CanShowFacts);
        await documents.StopAsync();
    }
}
