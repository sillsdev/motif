using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

// A count that lists fewer words must never read as an empty list.
public sealed class ProposalDifferenceListedTests
{
    private const string Project = @"C:\projects\one.fwdata";
    private static readonly Guid GenesisId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static ProposalDifferenceViewModel Make(ProposalDifferenceFixture fixture) =>
        new(fixture.Read, _ => { }, null, () => [new DifferenceTextChoice(GenesisId, "Genesis 1")]);

    private static DifferenceChipViewModel Chip(IEnumerable<DifferenceChipViewModel> chips, string label) =>
        Assert.Single(chips, chip => chip.Label.StartsWith(label, StringComparison.Ordinal));

    [Fact]
    public async Task ASummaryThatListsEveryWordShowsNoNoteAndPlainChips()
    {
        var fixture = new ProposalDifferenceFixture();
        var model = Make(fixture);

        await model.LoadAsync(Project, "proposal/one");

        Assert.False(model.HasListedNote);
        Assert.Equal(string.Empty, model.ListedNoteText);
        Assert.Equal("Lost an Approved Analysis", model.CheckChips[1].Label);
        Assert.All(model.MovedChips, chip => Assert.True(chip.CanChoose));
        Assert.DoesNotContain("counted but not listed", model.WordsStatusText);
    }

    [Fact]
    public async Task WordsThatAreOnlyCountedAreSaidSoInANoteAndOnTheChipsThatCutThem()
    {
        var fixture = new ProposalDifferenceFixture { Cut = 40, Unchanged = 100 };
        var model = Make(fixture);

        await model.LoadAsync(Project, "proposal/one");

        Assert.True(model.HasListedNote);
        Assert.StartsWith("30 of 170 words are listed; 140 words are counted but not listed", model.ListedNoteText);
        Assert.Contains("at most 100", model.ListedNoteText);
        Assert.Contains("at most 10 of its places", model.ListedNoteText);
        var regression = Chip(model.CheckChips, "Lost an Approved Analysis");
        Assert.Equal(50, regression.Count);
        Assert.Equal("Lost an Approved Analysis (10 listed)", regression.Label);
        Assert.True(regression.CanChoose);
        Assert.Equal("Analyses Changed", Chip(model.CheckChips, "Analyses Changed").Label);
        Assert.Equal(55, Chip(model.CheckChips, "Words to Check").Count);
    }

    [Fact]
    public async Task AWordWithMorePlacesThanAreListedSaysHowManyAreOnlyCountedAndOffersNoMore()
    {
        var fixture = new ProposalDifferenceFixture { PlacesPerWord = 10, PlacesCounted = 400 };
        var model = Make(fixture);
        await model.LoadAsync(Project, "proposal/one");

        model.Words[0].ChooseCommand.Execute(null);
        await Task.Yield();

        Assert.Equal(10, model.Places.Count);
        Assert.False(model.CanShowMorePlaces);
        Assert.True(model.ShowsPlacesStatus);
        Assert.Contains("Showing 10 of 10 listed places. 390 more are counted but not listed", model.PlacesStatusText);
        Assert.Contains("all of them are in the Text", model.PlacesStatusText);
    }

    [Fact]
    public async Task AnUnchangedCellIsCountedBeyondTheListAndItsChipDoesNotFilter()
    {
        var fixture = new ProposalDifferenceFixture { Unchanged = 100 };
        var model = Make(fixture);

        await model.LoadAsync(Project, "proposal/one");

        var unchanged = Assert.Single(model.MovedChips, chip => chip.Code == "approved-unique\napproved-unique");
        Assert.Equal(100, unchanged.Count);
        Assert.False(unchanged.CanChoose);
        Assert.All(model.MovedChips.Where(chip => chip != unchanged), chip => Assert.True(chip.CanChoose));
        Assert.False(unchanged.ChooseCommand.CanExecute(null));
        Assert.DoesNotContain("listed", unchanged.Label);
    }

    [Theory]
    [InlineData(DifferenceOffer.Available, true)]
    [InlineData(DifferenceOffer.Deferred, true)]
    [InlineData(DifferenceOffer.Decided, false)]
    public async Task ADamagedNoteSaysMakeDifferenceReplacesItOnlyWhileTheButtonIsOffered(DifferenceOffer offer, bool says)
    {
        var fixture = new ProposalDifferenceFixture
        {
            SavedDamaged = { new("difference/bad", GenesisId.ToString("D"), ProposalDifferenceReferences.Human, "2026-11-01T00:00:00Z") },
        };
        var model = Make(fixture);

        await model.LoadAsync(Project, "proposal/one", offer: offer);

        Assert.Contains("could not be read", model.SavedNoteText);
        Assert.Equal(says, model.SavedNoteText.Contains(MotifAppStrings.ProposalDifferenceSavedDamagedRemake));
    }

    [Fact]
    public async Task ChoosingACutChipSaysHowManyMoreWordsAreOnlyCounted()
    {
        var fixture = new ProposalDifferenceFixture { Cut = 40 };
        var model = Make(fixture);
        await model.LoadAsync(Project, "proposal/one");

        Chip(model.CheckChips, "Lost an Approved Analysis").ChooseCommand.Execute(null);
        await WaitFor(model, () => model.MatchingWords == 10);

        Assert.Equal("Showing 10 of 10. 40 more words are counted but not listed.", model.WordsStatusText);
    }

    [Fact]
    public async Task ACountWithNoListedWordsSaysSoInsteadOfNoWordsMatch()
    {
        var fixture = new ProposalDifferenceFixture { Cut = 40 };
        var model = Make(fixture);
        await model.LoadAsync(Project, "proposal/one");
        fixture.NoWordsMatch = true;

        Chip(model.CheckChips, "Lost an Approved Analysis").ChooseCommand.Execute(null);
        await WaitFor(model, () => model.MatchingWords == 0);

        Assert.True(model.ShowsNoMatchingWords);
        Assert.Equal("50 words are counted but not listed.", model.NoMatchingText);
    }

    [Fact]
    public async Task AnOldSummaryWithNoWordsListedFieldStillFiltersEveryChip()
    {
        var fixture = new ProposalDifferenceFixture();
        var model = Make(fixture);

        await model.LoadAsync(Project, "proposal/one");

        Assert.Null(model.Summary!.WordsListed);
        Assert.All(model.MovedChips.Concat(model.CheckChips).Concat(model.LimitationChips), chip => Assert.True(chip.CanChoose));
        fixture.NoWordsMatch = true;
        Chip(model.CheckChips, "Lost an Approved Analysis").ChooseCommand.Execute(null);
        await WaitFor(model, () => model.MatchingWords == 0);
        Assert.Equal(MotifAppStrings.ProposalDifferenceNoMatching, model.NoMatchingText);
    }

    [Fact]
    public async Task DamagedWordRowsWithAReadableSummaryShowTheSummarySayTheWordsCannotBeReadAndOfferMakeDifference()
    {
        var fixture = new ProposalDifferenceFixture { WordRowsDamaged = true, TextId = GenesisId.ToString("D") };
        var model = Make(fixture);

        await model.LoadAsync(Project, "proposal/one");

        Assert.Equal(ProposalDifferenceState.Shown, model.State);
        Assert.Contains("Compared 30 words", model.ComparedText);
        Assert.True(model.ShowsDetailUnreadable);
        Assert.False(model.ShowsDetailRemoved);
        Assert.False(model.ShowsWordList);
        Assert.All(model.MovedChips.Concat(model.CheckChips), chip => Assert.False(chip.CanChoose));
        Assert.True(model.ShowsMakeControls);
        Assert.True(model.ShowsRemakeHint);
        Assert.Equal(MotifAppStrings.ProposalDifferenceMakeAgain, model.MakeLabel);
    }

    [Fact]
    public async Task WhenEverySavedDifferenceIsDamagedTheSectionNamesThemAndOffersMakeDifference()
    {
        var fixture = new ProposalDifferenceFixture
        {
            EveryDifferenceDamaged =
            [
                new("difference/bad", GenesisId.ToString("D"), ProposalDifferenceReferences.Human, "2026-11-01T00:00:00Z"),
            ],
        };
        var model = Make(fixture);

        await model.LoadAsync(Project, "proposal/one");

        Assert.Equal(ProposalDifferenceState.Absent, model.State);
        Assert.Null(model.ShownRefusal);
        Assert.True(model.HasSavedNote);
        Assert.Contains("Genesis 1", model.SavedNoteText);
        Assert.Contains("people's decisions", model.SavedNoteText);
        Assert.Contains("could not be read", model.SavedNoteText);
        Assert.True(model.ShowsMakeControls);
        Assert.Equal(MotifAppStrings.ProposalDifferenceMake, model.MakeLabel);
    }

    private static async Task WaitFor(ProposalDifferenceViewModel model, Func<bool> done)
    {
        for (var attempt = 0; attempt < 200 && (!done() || model.IsReadingMore); attempt++) await Task.Delay(10);
        Assert.True(done());
    }
}
