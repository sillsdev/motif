using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Commands;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Texts;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class PassageOccurrenceTests
{
    private static OccurrenceAnchor Anchor(int index) => new(Guid.Parse("00000000-0000-0000-0000-000000000001"),
        Guid.Parse("00000000-0000-0000-0000-000000000002"), Guid.Parse("00000000-0000-0000-0000-000000000003"), index);

    private static OccurrenceChoiceViewModel Place(int index, string? status = InterlinearAnalysisStatus.Unapproved) =>
        new(Anchor(index), "Text", index, "sentence " + index, status, "analysis");

    [Theory]
    [InlineData(InterlinearAnalysisStatus.Approved, PassageWordState.Approved)]
    [InlineData(InterlinearAnalysisStatus.Unapproved, PassageWordState.Guessed)]
    [InlineData(InterlinearAnalysisStatus.Unanalysed, PassageWordState.NeedsAnalysis)]
    [InlineData(null, PassageWordState.NeedsAnalysis)]
    public void StoredStatusMapsToThePassageStateAndOnlyApprovedIsNotAGap(string? status, PassageWordState expected)
    {
        var state = PassageWordStates.From(status);
        Assert.Equal(expected, state);
        Assert.Equal(expected != PassageWordState.Approved, PassageWordStates.IsGap(state));
    }

    private sealed class Source(int count, Func<int, OccurrenceChoiceViewModel> make) : IWordOccurrenceSource
    {
        public int Count { get; } = count;

        public Task<IReadOnlyList<OccurrenceChoiceViewModel>> ReadPageAsync(int offset, int size, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<OccurrenceChoiceViewModel>>(
                Enumerable.Range(offset, Math.Max(0, Math.Min(size, Count - offset))).Select(make).ToArray());
    }

    private sealed class Actions : IWordOccurrenceActions
    {
        public int? FailAfter { get; set; }
        public Action? AfterFirstWrite { get; set; }
        public List<OccurrenceAnchor> Staged { get; } = [];
        public int NotAWordCount { get; set; }

        public Task<IReadOnlyList<OccurrenceAnchor>> StageKeepAsync(WordReadingOption reading,
            IReadOnlyList<OccurrenceChoiceViewModel> places, CancellationToken token)
        {
            var written = new List<OccurrenceAnchor>();
            foreach (var place in places)
            {
                if (token.IsCancellationRequested || FailAfter == written.Count) break;
                written.Add(place.Anchor);
                if (written.Count == 1) AfterFirstWrite?.Invoke();
            }
            Staged.AddRange(written);
            return Task.FromResult<IReadOnlyList<OccurrenceAnchor>>(written);
        }

        public Task<bool> StageNotAWordAsync(string form, CancellationToken token) { NotAWordCount++; return Task.FromResult(true); }

    }

    private static WordOccurrencesViewModel Model(int count, Actions actions, params WordReadingOption[] readings) =>
        new("wa", readings, new Source(count, index => Place(index)), actions);

    private static readonly WordReadingOption One = new("a1", "one", true);
    private static readonly WordReadingOption Two = new("a2", "two", true);

    [UiThreadFact]
    public async Task UntickingOneOfThreeStagesExactlyTheOtherTwoAnchors()
    {
        var actions = new Actions();
        var model = Model(3, actions, One);
        await model.LoadPageAsync(0);
        model.CheckShownGaps();
        Assert.Equal(3, model.SelectedCount);
        model.Page[1].IsChecked = false;
        Assert.Equal(2, model.SelectedCount);

        model.ApproveCommand.Execute(null);
        await Task.Yield();

        Assert.Equal([Anchor(0), Anchor(2)], actions.Staged.OrderBy(a => a.Index));
        Assert.False(model.Page[1].IsStaged);
        Assert.True(model.Page[0].IsStaged);
    }

    [UiThreadFact]
    public async Task SelectionSurvivesPagingAndStagesPlacesOffThePage()
    {
        var actions = new Actions();
        var model = Model(45, actions, One);
        await model.LoadPageAsync(0);
        model.SetChecked(Anchor(2), true);
        await model.LoadPageAsync(40);
        model.SetChecked(Anchor(41), true);
        await model.LoadPageAsync(0);
        Assert.True(model.Page[2].IsChecked);
        Assert.Equal(2, model.SelectedCount);

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)model.ApproveCommand).ExecuteAsync(null);

        Assert.Equal([Anchor(2), Anchor(41)], actions.Staged.OrderBy(a => a.Index));
    }

    [UiThreadFact]
    public async Task AFailureMarksOnlyThePlacesThatWereWritten()
    {
        var actions = new Actions { FailAfter = 1 };
        var model = Model(3, actions, One);
        await model.LoadPageAsync(0);
        model.CheckShownGaps();

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)model.ApproveCommand).ExecuteAsync(null);

        Assert.Single(model.StagedAnchors);
        Assert.Equal(1, model.Page.Count(place => place.IsStaged));
        Assert.Equal(2, model.SelectedCount);
    }

    [UiThreadFact]
    public async Task CancellationLeavesLaterPlacesUnmarked()
    {
        var actions = new Actions();
        var model = Model(3, actions, One);
        actions.AfterFirstWrite = model.Cancel;
        await model.LoadPageAsync(0);
        model.CheckShownGaps();
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)model.ApproveCommand).ExecuteAsync(null);

        Assert.Single(model.StagedAnchors);
        Assert.Equal(1, model.Page.Count(place => place.IsStaged));
        Assert.Equal(2, model.SelectedCount);
    }

    [UiThreadFact]
    public async Task SeveralGuessesNeedAnExplicitChoiceAndNoReadingHasNoKeep()
    {
        var actions = new Actions();
        var several = Model(2, actions, One, Two);
        await several.LoadPageAsync(0);
        several.CheckShownGaps();
        Assert.True(several.NeedsReadingChoice);
        Assert.False(several.CanApprove);
        several.ChosenReading = Two;
        Assert.True(several.CanApprove);

        var none = Model(2, actions);
        await none.LoadPageAsync(0);
        none.CheckShownGaps();
        Assert.False(none.HasReadings);
        Assert.False(none.CanApprove);
    }

    [UiThreadFact]
    public async Task NotAWordNamesTheSpellingScopeAndThePanelHasNoAskCommand()
    {
        var actions = new Actions();
        var model = Model(2, actions, One);
        Assert.DoesNotContain(typeof(WordOccurrencesViewModel).GetProperties(), property => property.Name.Contains("Claude", StringComparison.Ordinal));
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)model.NotAWordCommand).ExecuteAsync(null);

        Assert.Equal(1, actions.NotAWordCount);
        Assert.Contains("wa", model.NotAWordScope);
        Assert.Contains("whole project", model.NotAWordScope);
        Assert.Empty(actions.Staged);
    }

    [UiThreadFact]
    public async Task ChangesStagesOneApproveWithAnAnchorPerPlaceAndStopsOnRefusal()
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        var wordform = Guid.NewGuid();
        var places = Enumerable.Range(0, 3).Select(index =>
            new WordActionTarget("wa", wordform, Anchor(index), "assessment")).ToArray();
        fake.PendingPutRefusal = new Refusal("change.slot-occupied", FailureReason.Refused, "The place is occupied.");
        fake.PendingPutRefusalOnCall = 3;

        var written = await changes.StageOccurrenceApprovalsAsync(places, new WordActionReading(null, "one", null),
            "stored-1", CancellationToken.None);

        Assert.Equal([Anchor(0), Anchor(1)], written);
        Assert.Equal(3, fake.PendingPutRequests.Count);
        Assert.All(fake.PendingPutRequests, request =>
        {
            Assert.Equal(ChangeKinds.Approve, request.Change.Kind);
            Assert.Equal("stored-1", request.Change.StoredAnalysisId);
        });
        Assert.Single(fake.PendingPutRequests.Take(2).Select(request => request.Change.GroupId).Distinct());
    }
}
