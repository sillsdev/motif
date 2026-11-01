using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProposalDifferenceViewModelTests
{
    private const string Project = @"C:\projects\one.fwdata";

    private static (ProposalDifferenceViewModel Model, ProposalDifferenceFixture Fixture, List<string> Opened) Make(
        ProposalDifferenceFixture? fixture = null)
    {
        fixture ??= new ProposalDifferenceFixture();
        var opened = new List<string>();
        return (new ProposalDifferenceViewModel(fixture.Read, opened.Add), fixture, opened);
    }

    private static DifferenceChipViewModel Chip(IEnumerable<DifferenceChipViewModel> chips, string label) =>
        Assert.Single(chips, chip => chip.Label == label);

    [Fact]
    public async Task ADifferenceThatWasNeverSavedLeavesTheSectionHidden()
    {
        var model = new ProposalDifferenceViewModel(
            (_, _) => Task.FromResult(CommandOutcome<ProposalDifferenceResponse>.Refused(
                new Refusal(RefusalCodes.DifferenceNone, FailureReason.NotFound, "No saved Difference."))),
            _ => { });

        await model.LoadAsync(Project, "proposal/one");

        Assert.Equal(ProposalDifferenceState.None, model.State);
        Assert.False(model.IsVisible);
        Assert.Null(model.ShownRefusal);
    }

    [Fact]
    public async Task AnotherRefusalIsShownInTheWindowsWords()
    {
        var model = new ProposalDifferenceViewModel(
            (_, _) => Task.FromResult(CommandOutcome<ProposalDifferenceResponse>.Refused(
                new Refusal("store.busy", FailureReason.Busy, "The store is busy."))),
            _ => { });

        await model.LoadAsync(Project, "proposal/one");

        Assert.Equal(ProposalDifferenceState.Refused, model.State);
        Assert.NotNull(model.ShownRefusal);
        Assert.True(model.IsVisible);
    }

    [Fact]
    public async Task TheSummaryIsReadFirstWithNoFilterAndShownAsCountsAndChips()
    {
        var (model, fixture, _) = Make();

        await model.LoadAsync(Project, "proposal/one");

        var request = Assert.Single(fixture.Requests);
        Assert.Equal(Project, request.ProjectPath);
        Assert.Equal("proposal/one", request.ProposalId);
        Assert.Null(request.DifferenceId);
        Assert.False(request.CouldBeBad);
        Assert.Null(request.Bucket);
        Assert.Equal(ProposalDifferenceState.Shown, model.State);
        Assert.Equal("15 words to check, 4 words improved", model.HeadlineText);
        Assert.Equal("Compared 30 words (60 occurrences) in Genesis 1.", model.ComparedText);
        Assert.Equal("2 occurrences could not be matched to one measured word.", model.UnboundText);
        Assert.Equal("Not in this Text: 3 words the Trial measured.", model.OutsideText);
        Assert.Equal([MotifAppStrings.ProposalDifferenceNoteParser], model.Notes);
        Assert.Equal(
            ["Words to Check", "Lost an Approved Analysis", "Analyses Changed"], model.CheckChips.Select(chip => chip.Label));
        Assert.Equal([15, 10, 5], model.CheckChips.Select(chip => chip.Count));
        Assert.Equal(["Now One Approved Match"], model.ImprovedChips.Select(chip => chip.Label));
        Assert.Equal(["One Approved Match → No Approved Match", "No Parse → One Approved Match"],
            model.MovedChips.Select(chip => chip.Label));
        Assert.Equal(["Stopped at the Work Limit"], model.LimitationChips.Select(chip => chip.Label));
        Assert.Equal(ProposalDifferenceViewModel.PageSize, model.Words.Count);
        Assert.Equal("Showing 25 of 30.", model.WordsStatusText);
        Assert.True(model.CanShowMoreWords);
        Assert.All(model.CheckChips.Concat(model.MovedChips), chip => Assert.True(chip.CanChoose));
    }

    [Fact]
    public async Task NoLabelShowsAWireCodeOrARetiredWord()
    {
        var (model, _, _) = Make();
        await model.LoadAsync(Project, "proposal/one");

        var labels = Enum.GetValues<ProposalDifferenceBucket>().Select(ProposalDifferenceWords.Bucket)
            .Concat(Enum.GetValues<HumanAgreementLimitation>().Select(ProposalDifferenceWords.Limitation))
            .Concat(Enum.GetValues<HumanAgreementApprovalState>()
                .Select(state => ProposalDifferenceWords.State(ProposalDifferenceWords.Code(state))))
            .Concat(model.MovedChips.Select(chip => chip.Label))
            .Append(model.HeadlineText).Append(model.ComparedText).Append(model.UnboundText).Append(model.OutsideText)
            .Concat(model.Notes).ToArray();

        Assert.All(labels, label => Assert.DoesNotContain("delta.", label, StringComparison.Ordinal));
        Assert.All(labels, label => Assert.DoesNotContain(" ms", label, StringComparison.Ordinal));
        WindowWordsTests.AssertWindowWords(labels);
        Assert.Equal(Enum.GetValues<ProposalDifferenceBucket>().Length,
            Enum.GetValues<ProposalDifferenceBucket>().Select(ProposalDifferenceWords.Bucket).Distinct().Count());
    }

    [Fact]
    public async Task ChoosingACategoryFiltersTheWordsAndChoosingItAgainLiftsTheFilter()
    {
        var (model, fixture, _) = Make();
        await model.LoadAsync(Project, "proposal/one");
        var regression = Chip(model.CheckChips, "Lost an Approved Analysis");

        regression.ChooseCommand.Execute(null);
        await Task.Yield();

        var request = fixture.Requests[^1];
        Assert.Equal("delta.regression", request.Bucket);
        Assert.Equal("difference/one", request.DifferenceId);
        Assert.Equal(0, request.Offset);
        Assert.True(regression.IsActive);
        Assert.True(model.HasFilter);
        Assert.Equal(10, model.MatchingWords);
        Assert.Equal(10, model.Words.Count);
        Assert.False(model.CanShowMoreWords);

        regression.ChooseCommand.Execute(null);
        await Task.Yield();

        Assert.Null(fixture.Requests[^1].Bucket);
        Assert.False(regression.IsActive);
        Assert.False(model.HasFilter);
        Assert.Equal(30, model.MatchingWords);
    }

    [Fact]
    public async Task AWordsToCheckChipAMovedChipAndALimitChipEachReachTheQuery()
    {
        var (model, fixture, _) = Make();
        await model.LoadAsync(Project, "proposal/one");

        Chip(model.CheckChips, "Words to Check").ChooseCommand.Execute(null);
        await Task.Yield();
        Assert.True(fixture.Requests[^1].CouldBeBad);
        Assert.Equal(15, model.MatchingWords);

        Chip(model.MovedChips, "No Parse → One Approved Match").ChooseCommand.Execute(null);
        await Task.Yield();
        Assert.Equal("no-parse", fixture.Requests[^1].BeforeState);
        Assert.Equal("approved-unique", fixture.Requests[^1].AfterState);
        Assert.True(fixture.Requests[^1].CouldBeBad);
        Assert.Equal(0, model.MatchingWords);

        model.ClearFiltersCommand.Execute(null);
        await Task.Yield();
        Assert.False(fixture.Requests[^1].CouldBeBad);
        Assert.Null(fixture.Requests[^1].BeforeState);
        Assert.False(model.HasFilter);

        Chip(model.LimitationChips, "Stopped at the Work Limit").ChooseCommand.Execute(null);
        await Task.Yield();
        Assert.Equal("capped", fixture.Requests[^1].Limitation);
        Assert.Equal(2, model.MatchingWords);
        Assert.All(model.LimitationChips, chip => Assert.True(chip.IsActive));
    }

    [Fact]
    public async Task ShowMoreWordsReadsTheNextPageAndAppendsIt()
    {
        var (model, fixture, _) = Make();
        await model.LoadAsync(Project, "proposal/one");

        model.ShowMoreWordsCommand.Execute(null);
        await Task.Yield();

        Assert.Equal(25, fixture.Requests[^1].Offset);
        Assert.Equal(30, model.Words.Count);
        Assert.Equal("word29", model.Words[^1].Label);
        Assert.False(model.CanShowMoreWords);
        Assert.Equal("Showing 30 of 30.", model.WordsStatusText);
    }

    [Fact]
    public async Task ChoosingAWordListsItsPlacesAPageAtATimeAndCanOpenIt()
    {
        var (model, fixture, opened) = Make(new ProposalDifferenceFixture { PlacesPerWord = 30 });
        await model.LoadAsync(Project, "proposal/one");

        model.Words[3].ChooseCommand.Execute(null);
        await Task.Yield();

        var request = fixture.Requests[^1];
        Assert.Equal("word03", request.Word);
        Assert.Equal("difference/one", request.DifferenceId);
        Assert.Equal(0, request.OccurrenceOffset);
        Assert.Equal("Places of word03", model.SelectedWordTitle);
        Assert.Equal("One Approved Match → No Approved Match", model.SelectedWordMovedText);
        Assert.Equal("Lost an Approved Analysis", model.SelectedWordBucketsText);
        Assert.Equal(25, model.Places.Count);
        Assert.Equal("Line 1, word 1", model.Places[0]);
        Assert.True(model.CanShowMorePlaces);
        Assert.True(model.Words[3].IsActive);

        model.ShowMorePlacesCommand.Execute(null);
        await Task.Yield();

        Assert.Equal(25, fixture.Requests[^1].OccurrenceOffset);
        Assert.Equal(30, model.Places.Count);
        Assert.False(model.CanShowMorePlaces);

        model.OpenWordCommand.Execute(null);
        Assert.Equal(["word03"], opened);

        model.Words[3].ChooseCommand.Execute(null);
        await Task.Yield();
        Assert.False(model.HasSelectedWord);
        Assert.Empty(model.Places);
    }

    [Fact]
    public async Task TheListsStopAtTheMostShownAndAskForANarrowerFilter()
    {
        var (model, fixture, _) = Make(new ProposalDifferenceFixture(wordCount: 150) { PlacesPerWord = 150 });
        await model.LoadAsync(Project, "proposal/one");
        Assert.False(model.ShowsNarrowHint);

        for (var page = 0; page < 5 && model.CanShowMoreWords; page++) model.ShowMoreWordsCommand.Execute(null);

        Assert.Equal(ProposalDifferenceViewModel.MostShown, model.Words.Count);
        Assert.False(model.CanShowMoreWords);
        Assert.True(model.ShowsNarrowHint);
        Assert.Equal("Showing 100 of 150.", model.WordsStatusText);

        model.Words[0].ChooseCommand.Execute(null);
        Assert.Equal(ProposalDifferenceViewModel.PageSize, model.Places.Count);
        for (var page = 0; page < 8 && model.CanShowMorePlaces; page++) model.ShowMorePlacesCommand.Execute(null);

        Assert.Equal(ProposalDifferenceViewModel.MostShown, model.Places.Count);
        Assert.False(model.CanShowMorePlaces);
        Assert.True(model.ShowsPlacesStatus);
        Assert.Equal("Showing 100 of 150.", model.PlacesStatusText);
        Assert.Equal(75, fixture.Requests[^1].OccurrenceOffset);
    }

    [Fact]
    public async Task ADecidedProposalShowsOnlyItsSummaryAndTheChipsOnlyCount()
    {
        var (model, fixture, _) = Make(new ProposalDifferenceFixture(wordDetailKept: false));

        await model.LoadAsync(Project, "proposal/one");

        Assert.Single(fixture.Requests);
        Assert.Equal(ProposalDifferenceState.Shown, model.State);
        Assert.True(model.IsSummaryOnly);
        Assert.False(model.ShowsWordList);
        Assert.Empty(model.Words);
        Assert.Equal("15 words to check, 4 words improved", model.HeadlineText);
        Assert.NotEmpty(model.CheckChips);
        Assert.NotEmpty(model.MovedChips);
        Assert.All(model.CheckChips.Concat(model.MovedChips).Concat(model.LimitationChips),
            chip => Assert.False(chip.CanChoose));
        Assert.False(model.CanShowMoreWords);
    }

    [Fact]
    public async Task ASlowEarlierReadNeverOverwritesALaterOne()
    {
        var fixture = new ProposalDifferenceFixture();
        var gate = new TaskCompletionSource();
        var calls = 0;
        var model = new ProposalDifferenceViewModel(async (request, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1) await gate.Task;
            return await fixture.Read(request, token);
        }, _ => { });

        var first = model.LoadAsync(Project, "proposal/slow");
        await model.LoadAsync(Project, "proposal/fast");
        gate.SetResult();
        await first;

        Assert.Equal(ProposalDifferenceState.Shown, model.State);
        Assert.Equal("proposal/fast", fixture.Requests[0].ProposalId);
        Assert.Equal(25, model.Words.Count);
    }

    [Fact]
    public async Task ClearForgetsTheDifference()
    {
        var (model, _, _) = Make();
        await model.LoadAsync(Project, "proposal/one");

        model.Clear();

        Assert.False(model.IsVisible);
        Assert.Null(model.Summary);
        Assert.Empty(model.Words);
        Assert.Empty(model.CheckChips);
    }
    [Fact]
    public async Task NotesAreTheWindowsOwnWordsWithoutSidesHashesOrTokeniserNames()
    {
        var (model, _, _) = Make();
        await model.LoadAsync(Project, "proposal/one");

        var note = Assert.Single(model.Notes);

        Assert.Equal(MotifAppStrings.ProposalDifferenceNoteParser, note);
        Assert.DoesNotContain("side", note, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("[0-9a-f]{12}", note);
    }

    [Fact]
    public void EachNoteTheProductWritesHasItsOwnWordsAndAnythingElseIsOneGeneralNote()
    {
        string[] warnings =
        [
            ProposalDifferenceWarnings.ParserDiffers + "1.0 (0123456789ab) and the after side by 1.1 (ba9876543210). A word that differs...",
            ProposalDifferenceWarnings.TokeniserDiffers + "regex 1 and the after side with regex 2, so the two may differ.",
            ProposalDifferenceWarnings.RetryDiffers + " under a longer limit, so the two runs used different effective per-word limits.",
            "A note an older version wrote.",
            "Another old note.",
        ];

        var notes = ProposalDifferenceWords.Notes(warnings);

        Assert.Equal(
        [
            MotifAppStrings.ProposalDifferenceNoteParser, MotifAppStrings.ProposalDifferenceNoteTokeniser,
            MotifAppStrings.ProposalDifferenceNoteRetry, MotifAppStrings.ProposalDifferenceNoteOther,
        ], notes);
        WindowWordsTests.AssertWindowWords(notes);
        Assert.All(notes, text => Assert.DoesNotContain("side", text, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(ProposalDifferenceWords.Notes([]));
    }

    [Theory]
    [InlineData("filter")]
    [InlineData("more")]
    [InlineData("word")]
    public async Task AProposalDecidedFromOutsideWhileOpenBecomesSummaryOnlyOnTheNextRead(string action)
    {
        var (model, fixture, _) = Make();
        await model.LoadAsync(Project, "proposal/one");
        Assert.False(model.IsSummaryOnly);
        fixture.WordDetailKept = false;

        switch (action)
        {
            case "filter": Chip(model.CheckChips, "Lost an Approved Analysis").ChooseCommand.Execute(null); break;
            case "more": model.ShowMoreWordsCommand.Execute(null); break;
            default: model.Words[0].ChooseCommand.Execute(null); break;
        }
        await Task.Yield();

        Assert.True(model.IsSummaryOnly);
        Assert.False(model.ShowsWordList);
        Assert.Empty(model.Words);
        Assert.False(model.CanShowMoreWords);
        Assert.False(model.HasFilter);
        Assert.False(model.HasSelectedWord);
        Assert.Null(model.ShownRefusal);
        Assert.NotEmpty(model.CheckChips);
        Assert.All(model.CheckChips.Concat(model.MovedChips).Concat(model.LimitationChips),
            chip => Assert.False(chip.CanChoose));
    }

    [Fact]
    public async Task AFilterThenShowMoreNeverAppendsAPageOfAnotherFilter()
    {
        var gate = new TaskCompletionSource();
        var fixture = new ProposalDifferenceFixture();
        var (model, _, _) = Make(fixture);
        await model.LoadAsync(Project, "proposal/one");
        Assert.True(model.CanShowMoreWords);
        fixture.BeforeRead = (_, _) => gate.Task;
        var loaded = fixture.Requests.Count;

        Chip(model.CheckChips, "Lost an Approved Analysis").ChooseCommand.Execute(null);

        Assert.False(model.CanShowMoreWords);
        model.ShowMoreWordsCommand.Execute(null);
        gate.SetResult();
        await Task.Yield();
        await Task.Delay(50);

        Assert.All(fixture.Requests.Skip(loaded), request => Assert.Equal(0, request.Offset));
        Assert.Equal(10, model.Words.Count);
        Assert.All(model.Words, word => Assert.True(int.Parse(word.Label[4..]) < 10));
    }

    [Fact]
    public async Task ShowMoreThatIsStillReadingWhenAFilterIsChosenNeverAppendsItsPage()
    {
        var gate = new TaskCompletionSource();
        var fixture = new ProposalDifferenceFixture();
        var (model, _, _) = Make(fixture);
        await model.LoadAsync(Project, "proposal/one");
        fixture.BeforeRead = (request, _) => request.Offset == 25 ? gate.Task : Task.CompletedTask;

        model.ShowMoreWordsCommand.Execute(null);
        Chip(model.CheckChips, "Lost an Approved Analysis").ChooseCommand.Execute(null);
        await Task.Yield();
        gate.SetResult();
        await Task.Delay(50);

        Assert.Equal(10, model.Words.Count);
        Assert.Equal(10, model.MatchingWords);
    }

    [Fact]
    public async Task ASupersededReadIsCancelledQuietlyAndIsNotShownAsAFailure()
    {
        var fixture = new ProposalDifferenceFixture();
        var (model, _, _) = Make(fixture);
        await model.LoadAsync(Project, "proposal/one");
        var tokens = new List<CancellationToken>();
        fixture.BeforeRead = async (request, token) =>
        {
            tokens.Add(token);
            if (request.Bucket == "delta.regression") await Task.Delay(Timeout.Infinite, token);
        };

        Chip(model.CheckChips, "Lost an Approved Analysis").ChooseCommand.Execute(null);
        Assert.True(model.IsReadingMore);
        Chip(model.CheckChips, "Analyses Changed").ChooseCommand.Execute(null);
        await Task.Delay(50);

        Assert.True(tokens[0].IsCancellationRequested);
        Assert.False(tokens[1].IsCancellationRequested);
        Assert.Null(model.ShownRefusal);
        Assert.False(model.IsReadingMore);
        Assert.Equal(5, model.Words.Count);
    }

    [Fact]
    public async Task ClearingTheDifferenceCancelsReadsStillRunning()
    {
        var fixture = new ProposalDifferenceFixture();
        var (model, _, _) = Make(fixture);
        await model.LoadAsync(Project, "proposal/one");
        CancellationToken seen = default;
        fixture.BeforeRead = async (_, token) =>
        {
            seen = token;
            await Task.Delay(Timeout.Infinite, token);
        };
        Chip(model.CheckChips, "Lost an Approved Analysis").ChooseCommand.Execute(null);

        model.Clear();
        await Task.Delay(50);

        Assert.True(seen.IsCancellationRequested);
        Assert.Null(model.ShownRefusal);
    }

    [Theory]
    [InlineData("filter")]
    [InlineData("more")]
    [InlineData("word")]
    [InlineData("places")]
    public async Task AReadThatThrowsIsShownAsARefusalInsteadOfGoingUnobserved(string action)
    {
        var fixture = new ProposalDifferenceFixture { PlacesPerWord = 60 };
        var (model, _, _) = Make(fixture);
        await model.LoadAsync(Project, "proposal/one");
        if (action == "places")
        {
            model.Words[0].ChooseCommand.Execute(null);
            await Task.Yield();
        }
        fixture.BeforeRead = (_, _) => Task.FromException(new InvalidOperationException("The store broke."));

        switch (action)
        {
            case "filter": Chip(model.CheckChips, "Lost an Approved Analysis").ChooseCommand.Execute(null); break;
            case "more": model.ShowMoreWordsCommand.Execute(null); break;
            case "word": model.Words[1].ChooseCommand.Execute(null); break;
            default: model.ShowMorePlacesCommand.Execute(null); break;
        }
        await Task.Delay(50);

        Assert.NotNull(model.ShownRefusal);
        Assert.Equal(RefusalCodes.WindowDifferenceReadFailed, model.ShownRefusal!.Code);
        Assert.Equal(MotifAppStrings.ProposalDifferenceUnreadable, model.ShownRefusal.Sentence);
        Assert.False(model.IsReadingMore);
    }

    [Fact]
    public async Task AFirstReadThatThrowsLeavesTheSectionRefusedNotLoading()
    {
        var model = new ProposalDifferenceViewModel(
            (_, _) => Task.FromException<CommandOutcome<ProposalDifferenceResponse>>(new IOException("gone")), _ => { });

        await model.LoadAsync(Project, "proposal/one");

        Assert.Equal(ProposalDifferenceState.Refused, model.State);
        Assert.NotNull(model.ShownRefusal);
    }

    [Fact]
    public async Task TheSectionSaysItIsReadingWhileAFilterReadRuns()
    {
        var gate = new TaskCompletionSource();
        var fixture = new ProposalDifferenceFixture();
        var (model, _, _) = Make(fixture);
        await model.LoadAsync(Project, "proposal/one");
        Assert.False(model.IsReadingMore);
        fixture.BeforeRead = (_, _) => gate.Task;

        Chip(model.CheckChips, "Lost an Approved Analysis").ChooseCommand.Execute(null);
        Assert.True(model.IsReadingMore);
        gate.SetResult();
        await Task.Delay(50);

        Assert.False(model.IsReadingMore);
    }
}
