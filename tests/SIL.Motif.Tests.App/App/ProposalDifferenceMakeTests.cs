using System.Globalization;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

// Making a Difference from the window: an explicit action on the selected Proposal, never an apply.
public sealed class ProposalDifferenceMakeTests
{
    private const string Project = @"C:\projects\one.fwdata";
    private static readonly Guid GenesisId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ExodusId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    // Reads answer "none" until a run has made the Difference, as the store does.
    private sealed class Harness
    {
        public ProposalDifferenceFixture Fixture { get; } = new();

        public bool Made { get; set; }

        public List<ProposalDifferenceProduceRequest> Requests { get; } = [];

        public Func<ProposalDifferenceProduceRequest, IProgress<AssessmentProgress>, CancellationToken,
            Task<CommandOutcome<ProposalDifferenceResponse>>>? Behavior { get; set; }

        public IReadOnlyList<DifferenceTextChoice> Texts { get; set; } =
            [new(GenesisId, "Genesis 1"), new(ExodusId, "Exodus 1")];

        public ProposalDifferenceViewModel Model { get; }

        public Harness(bool alreadyMade = false)
        {
            Made = alreadyMade;
            Fixture.TextId = GenesisId.ToString("D");
            Model = new ProposalDifferenceViewModel(ReadAsync, _ => { }, ProduceAsync, () => Texts);
        }

        private Task<CommandOutcome<ProposalDifferenceResponse>> ReadAsync(
            ProposalDifferenceShowRequest request, CancellationToken cancellationToken) => Made
            ? Fixture.Read(request, cancellationToken)
            : Task.FromResult(CommandOutcome<ProposalDifferenceResponse>.Refused(
                new Refusal(RefusalCodes.DifferenceNone, FailureReason.NotFound, "No saved Difference.")));

        private async Task<CommandOutcome<ProposalDifferenceResponse>> ProduceAsync(
            ProposalDifferenceProduceRequest request, IProgress<AssessmentProgress> progress,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Behavior is { } behavior) return await behavior(request, progress, cancellationToken);
            Made = true;
            Fixture.TextId = request.TextId;
            return await Fixture.Read(new ProposalDifferenceShowRequest(request.ProjectPath, request.ProposalId),
                cancellationToken);
        }
    }

    [Fact]
    public async Task WithNoSavedDifferenceTheSectionOffersToMakeOneForTheFirstText()
    {
        var harness = new Harness();

        await harness.Model.LoadAsync(Project, "proposal/one");

        var model = harness.Model;
        Assert.Equal(ProposalDifferenceState.Absent, model.State);
        Assert.True(model.IsVisible);
        Assert.True(model.OffersMake);
        Assert.True(model.ShowsMakeControls);
        Assert.True(model.ShowsMakeAdvice);
        Assert.Equal("Make Difference", model.MakeLabel);
        Assert.Equal(GenesisId, model.SelectedText!.Id);
        Assert.True(model.Maker.RunCommand.CanExecute(null));
        Assert.Empty(harness.Requests);
    }

    [Fact]
    public async Task MakingSendsTheChosenTextAndShowsTheDifferenceAndOffersMakingItAgain()
    {
        var harness = new Harness();
        var model = harness.Model;
        await model.LoadAsync(Project, "proposal/one");
        model.SelectedText = model.TextChoices[1];

        await model.Maker.RunCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Requests);
        Assert.Equal(new ProposalDifferenceProduceRequest(Project, "proposal/one", ExodusId.ToString("D")), request);
        Assert.Equal(RunState.Completed, model.Maker.State);
        Assert.Equal(ProposalDifferenceState.Shown, model.State);
        Assert.Equal(harness.Fixture.Summary.DifferenceId, model.Summary!.DifferenceId);
        Assert.Equal(ProposalDifferenceViewModel.PageSize, model.Words.Count);
        Assert.Equal("Make Difference Again", model.MakeLabel);
        Assert.False(model.ShowsMakeAdvice);
        Assert.True(model.Maker.RunCommand.CanExecute(null));
        Assert.Null(model.Maker.FailureShown);
    }

    [Fact]
    public async Task AProposalThatAlreadyHasADifferenceOffersMakingItAgainWithTheSameAction()
    {
        var harness = new Harness(alreadyMade: true);
        var model = harness.Model;

        await model.LoadAsync(Project, "proposal/one");

        Assert.Equal(ProposalDifferenceState.Shown, model.State);
        Assert.True(model.ShowsMakeControls);
        Assert.Equal("Make Difference Again", model.MakeLabel);
        Assert.Equal(GenesisId, model.SelectedText!.Id);

        await model.Maker.RunCommand.ExecuteAsync(null);

        Assert.Single(harness.Requests);
        Assert.Equal(ProposalDifferenceState.Shown, model.State);
    }

    [Fact]
    public async Task TheTextListFollowsTheTextOfTheDifferenceShownAndTheLabelNamesWhetherThatTextHasOne()
    {
        var harness = new Harness(alreadyMade: true);
        harness.Fixture.TextId = ExodusId.ToString("D");
        var model = harness.Model;

        await model.LoadAsync(Project, "proposal/one");

        Assert.Equal(ExodusId, model.SelectedText!.Id);
        Assert.Equal("Make Difference Again", model.MakeLabel);

        model.SelectedText = model.TextChoices[0];
        Assert.Equal("Make Difference", model.MakeLabel);

        model.SelectedText = model.TextChoices[1];
        Assert.Equal("Make Difference Again", model.MakeLabel);
        await model.Maker.RunCommand.ExecuteAsync(null);
        Assert.Equal(ExodusId.ToString("D"), Assert.Single(harness.Requests).TextId);
    }

    private const string SourceValidated = ProposalDifferenceReferences.SourceValidated;
    private const string Human = ProposalDifferenceReferences.Human;

    // The window sends no reference: the shown Difference can be stale, so the store decides at the write.
    [Theory]
    [InlineData(Human, SourceValidated)]
    [InlineData(SourceValidated, Human)]
    public async Task MakingAgainKeepsWhatTheStoreHoldsNotWhatTheStaleShownDifferenceSays(string shown, string stored)
    {
        var harness = new Harness(alreadyMade: true);
        harness.Fixture.Reference = shown;
        var model = harness.Model;
        await model.LoadAsync(Project, "proposal/one");
        Assert.Equal(shown, model.Summary!.Reference);
        Assert.Equal(shown == SourceValidated, model.HasReferenceNote);

        harness.Fixture.Reference = stored;
        await model.Maker.RunCommand.ExecuteAsync(null);

        Assert.Equal(stored, model.Summary!.Reference);
        Assert.Equal(stored == SourceValidated, model.HasReferenceNote);
        Assert.Equal(ProposalDifferenceWords.Advice(model.Summary), model.AdviceText);
        Assert.True(model.HasAdviceText);
    }

    [Theory]
    [InlineData(Human, false)]
    [InlineData(SourceValidated, false)]
    [InlineData(Human, true)]
    [InlineData(SourceValidated, true)]
    public async Task WhileAnyRunGoesNeitherTheNoteNorTheAdviceShowAndTheyComeBackWhenItIsCancelled(
        string shown, bool anotherText)
    {
        var harness = new Harness(alreadyMade: true);
        harness.Fixture.Reference = shown;
        var model = harness.Model;
        await model.LoadAsync(Project, "proposal/one");
        if (anotherText) model.SelectedText = model.TextChoices[1];
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        harness.Behavior = async (_, _, _) =>
        {
            started.SetResult();
            await release.Task;
            return CommandOutcome<ProposalDifferenceResponse>.Refused(new Refusal(
                RefusalCodes.DifferenceCancelled, FailureReason.Cancelled, "Cancelled."));
        };
        var raised = new List<string?>();
        model.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var run = model.Maker.RunCommand.ExecuteAsync(null);
        await started.Task;

        Assert.False(model.HasReferenceNote);
        Assert.False(model.HasAdviceText);
        Assert.Contains(nameof(ProposalDifferenceViewModel.HasAdviceText), raised);

        model.Maker.CancelCommand.Execute(null);
        release.SetResult();
        await run;

        Assert.Equal(RunState.Cancelled, model.Maker.State);
        Assert.Equal(shown == SourceValidated, model.HasReferenceNote);
        Assert.True(model.HasAdviceText);
        Assert.Equal(ProposalDifferenceWords.Advice(model.Summary), model.AdviceText);
    }

    [Fact]
    public async Task StoppingARunGivesBackTheShownDifferencesNoteAndAdvice()
    {
        var harness = new Harness(alreadyMade: true);
        harness.Fixture.Reference = SourceValidated;
        var model = harness.Model;
        await model.LoadAsync(Project, "proposal/one");
        var started = new TaskCompletionSource();
        harness.Behavior = async (_, _, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        };

        var run = model.Maker.RunCommand.ExecuteAsync(null);
        await started.Task;
        Assert.False(model.HasReferenceNote);
        Assert.False(model.HasAdviceText);
        await model.StopAsync();
        await run;

        Assert.False(model.Maker.IsActive);
        Assert.True(model.HasReferenceNote);
        Assert.True(model.HasAdviceText);
        Assert.Equal(MotifAppStrings.ProposalDifferenceAdviceSourceValidated, model.AdviceText);
    }

    [Fact]
    public async Task TheLabelIsRaisedAgainWhenAMadeDifferenceForTheChosenTextIsAdopted()
    {
        var harness = new Harness(alreadyMade: true);
        var model = harness.Model;
        await model.LoadAsync(Project, "proposal/one");
        model.SelectedText = model.TextChoices[1];
        var raised = new List<string?>();
        model.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await model.Maker.RunCommand.ExecuteAsync(null);

        Assert.Contains(nameof(ProposalDifferenceViewModel.MakeLabel), raised);
        Assert.Equal("Make Difference Again", model.MakeLabel);
    }

    [Fact]
    public async Task MakingForAnotherTextThanTheShownOneReadsMakeDifferenceAndThenAgainOnceItIsShown()
    {
        var harness = new Harness(alreadyMade: true);
        var model = harness.Model;
        await model.LoadAsync(Project, "proposal/one");
        model.SelectedText = model.TextChoices[1];
        Assert.Equal("Make Difference", model.MakeLabel);

        await model.Maker.RunCommand.ExecuteAsync(null);

        Assert.Equal(ExodusId, model.SelectedText!.Id);
        Assert.Equal("Make Difference Again", model.MakeLabel);
        Assert.Equal(ExodusId.ToString("D"), model.Summary!.TextId);
    }

    [Fact]
    public async Task ARunThatStillCompletesAfterCancelShowsTheDifferenceItSavedInsteadOfPretendingOtherwise()
    {
        var harness = new Harness();
        var model = harness.Model;
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        harness.Behavior = async (request, _, token) =>
        {
            started.SetResult();
            await release.Task;
            harness.Made = true;
            harness.Fixture.TextId = request.TextId;
            return await harness.Fixture.Read(new ProposalDifferenceShowRequest(Project, "proposal/one"), token);
        };
        await model.LoadAsync(Project, "proposal/one");
        var run = model.Maker.RunCommand.ExecuteAsync(null);
        await started.Task;

        model.Maker.CancelCommand.Execute(null);

        Assert.Equal("Cancelling.", model.Maker.ProgressText);
        release.SetResult();
        await run;

        Assert.Equal(RunState.Completed, model.Maker.State);
        Assert.False(model.Maker.WasCancelled);
        Assert.Equal(ProposalDifferenceState.Shown, model.State);
        Assert.NotNull(model.Summary);
        Assert.Equal("Make Difference Again", model.MakeLabel);
    }

    [Fact]
    public async Task ProgressIsToldInWordsAndTheRunCanBeCancelledAndSavesNothing()
    {
        var harness = new Harness();
        var model = harness.Model;
        var reported = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        CancellationToken seen = default;
        harness.Behavior = async (_, progress, token) =>
        {
            seen = token;
            progress.Report(new AssessmentProgress(AssessmentStage.SelectingWords, 0, null, "Reading the Text."));
            progress.Report(new AssessmentProgress(AssessmentStage.Finalizing, 3, 1200, "Comparing words."));
            reported.SetResult();
            await release.Task;
            return CommandOutcome<ProposalDifferenceResponse>.Refused(new Refusal(
                RefusalCodes.DifferenceCancelled, FailureReason.Cancelled, "Cancelled."));
        };
        await model.LoadAsync(Project, "proposal/one");

        var run = model.Maker.RunCommand.ExecuteAsync(null);
        await reported.Task;
        await Task.Yield();

        Assert.True(model.Maker.IsActive);
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture,
            MotifAppStrings.ProposalDifferenceMakingFormat, 3.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            1200.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)), model.Maker.ProgressText);
        Assert.False(model.Maker.IsIndeterminate);
        Assert.True(model.Maker.CancelCommand.CanExecute(null));
        Assert.False(model.Maker.RunCommand.CanExecute(null));

        model.Maker.CancelCommand.Execute(null);

        Assert.True(seen.IsCancellationRequested);
        Assert.Equal(MotifAppStrings.ProposalDifferenceCancelling, model.Maker.ProgressText);
        Assert.False(model.Maker.CancelCommand.CanExecute(null));
        release.SetResult();
        await run;

        Assert.Equal(RunState.Cancelled, model.Maker.State);
        Assert.True(model.Maker.WasCancelled);
        Assert.Null(model.Maker.FailureShown);
        Assert.Equal(MotifAppStrings.RefusalDifferenceCancelled, model.Maker.CancelledText);
        Assert.Equal(ProposalDifferenceState.Absent, model.State);
        Assert.False(harness.Made);
        Assert.True(model.Maker.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task AnUnknownTotalShowsReadingTheTextThenMakingTheDifference()
    {
        var harness = new Harness();
        var model = harness.Model;
        var reported = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        harness.Behavior = async (_, progress, _) =>
        {
            progress.Report(new AssessmentProgress(AssessmentStage.SelectingWords, 0, null, "Reading the Text."));
            reported.SetResult();
            await release.Task;
            return CommandOutcome<ProposalDifferenceResponse>.Refused(
                new Refusal(RefusalCodes.DifferenceNoTrial, FailureReason.NotFound, "No Trial."));
        };
        await model.LoadAsync(Project, "proposal/one");

        var run = model.Maker.RunCommand.ExecuteAsync(null);
        await reported.Task;
        await Task.Yield();

        Assert.Equal(MotifAppStrings.ProposalDifferenceMakingReadingText, model.Maker.ProgressText);
        Assert.True(model.Maker.IsIndeterminate);
        release.SetResult();
        await run;
    }

    [Theory]
    [InlineData("difference.no-trial")]
    [InlineData("difference.trial-incomplete")]
    [InlineData("difference.no-parse-assessment")]
    [InlineData("difference.no-before")]
    [InlineData("difference.no-baseline")]
    [InlineData("difference.baseline-changed")]
    [InlineData("difference.text-not-found")]
    [InlineData("difference.proposal-decided")]
    [InlineData("difference.duplicate-parse-rows")]
    [InlineData("difference.unexpected-opinion")]
    public async Task ARefusedRunSaysWhyInTheWindowsWordsAndCanBeTriedAgain(string code)
    {
        var harness = new Harness();
        var model = harness.Model;
        harness.Behavior = (_, _, _) => Task.FromResult(CommandOutcome<ProposalDifferenceResponse>.Refused(
            new Refusal(code, FailureReason.Refused, "Run: motif proposal trial --project <fwdata> <proposalId>")));
        await model.LoadAsync(Project, "proposal/one");

        await model.Maker.RunCommand.ExecuteAsync(null);

        Assert.Equal(RunState.Refused, model.Maker.State);
        var shown = model.Maker.FailureShown!;
        Assert.NotEqual(WindowRefusal.GenericSentence, shown.Sentence);
        Assert.NotEqual(MotifAppStrings.ProposalDifferenceMakeFailed, shown.Sentence);
        WindowWordsTests.AssertWindowWords([shown.Sentence, shown.Details ?? string.Empty]);
        Assert.DoesNotContain("motif ", shown.Sentence, StringComparison.Ordinal);
        Assert.False(model.Maker.WasCancelled);
        Assert.Equal(ProposalDifferenceState.Absent, model.State);
        Assert.True(model.Maker.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task ARunThatThrowsIsShownAsARefusalInsteadOfGoingUnobserved()
    {
        var harness = new Harness();
        var model = harness.Model;
        harness.Behavior = (_, _, _) => throw new InvalidOperationException("The store broke.");
        await model.LoadAsync(Project, "proposal/one");

        await model.Maker.RunCommand.ExecuteAsync(null);

        Assert.Equal(RunState.Refused, model.Maker.State);
        Assert.Equal(MotifAppStrings.ProposalDifferenceMakeFailed, model.Maker.FailureShown!.Sentence);
    }

    [Fact]
    public async Task AProposalNeverTriedOrAlreadyDecidedOffersNoActionAndSaysWhy()
    {
        var harness = new Harness();
        var model = harness.Model;

        await model.LoadAsync(Project, "proposal/one", offer: DifferenceOffer.NotTried);

        Assert.True(model.IsVisible);
        Assert.True(model.ShowsNotTriedNote);
        Assert.False(model.ShowsMakeControls);
        Assert.False(model.Maker.RunCommand.CanExecute(null));

        await model.LoadAsync(Project, "proposal/one", offer: DifferenceOffer.Decided);

        Assert.True(model.ShowsDecidedNote);
        Assert.False(model.ShowsNotTriedNote);
        Assert.False(model.ShowsMakeControls);
        Assert.False(model.Maker.RunCommand.CanExecute(null));
        Assert.Empty(harness.Requests);
    }

    [Fact]
    public async Task ADecidedProposalWithOnlyASummaryOffersNoAction()
    {
        var harness = new Harness(alreadyMade: true);
        harness.Fixture.WordDetailKept = false;

        await harness.Model.LoadAsync(Project, "proposal/one", offer: DifferenceOffer.Decided);

        Assert.Equal(ProposalDifferenceState.Shown, harness.Model.State);
        Assert.False(harness.Model.OffersMake);
        Assert.False(harness.Model.ShowsMakeControls);
        Assert.False(harness.Model.ShowsRemakeHint);
        Assert.False(harness.Model.Maker.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task ADeferredProposalShowsItsSummaryAndOffersToMakeTheDifferenceAgainToSeeWords()
    {
        var harness = new Harness(alreadyMade: true);
        harness.Fixture.WordDetailKept = false;

        await harness.Model.LoadAsync(Project, "proposal/one", offer: DifferenceOffer.Deferred);

        Assert.Equal(ProposalDifferenceState.Shown, harness.Model.State);
        Assert.True(harness.Model.IsSummaryOnly);
        Assert.Empty(harness.Model.Words);
        Assert.True(harness.Model.OffersMake);
        Assert.True(harness.Model.ShowsRemakeHint);
        Assert.True(harness.Model.ShowsMakeControls);
        Assert.Equal("Make Difference Again", harness.Model.MakeLabel);
        Assert.True(harness.Model.Maker.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task AnUndecidedProposalWithWordsShowsNoRemakeHint()
    {
        var harness = new Harness(alreadyMade: true);

        await harness.Model.LoadAsync(Project, "proposal/one");

        Assert.False(harness.Model.IsSummaryOnly);
        Assert.False(harness.Model.ShowsRemakeHint);
    }

    [Fact]
    public async Task TheSavedDifferencesAreListedWithTheirTextAndReferenceAndChoosingOneReadsIt()
    {
        var harness = new Harness(alreadyMade: true);
        harness.Fixture.SavedOthers.Add(new ProposalDifferenceSaved("difference/two", ExodusId.ToString("D"),
            "Exodus 1", ProposalDifferenceReferences.SourceValidated, "2026-10-30T00:00:00Z", true));
        harness.Fixture.SavedNotListed = 3;

        await harness.Model.LoadAsync(Project, "proposal/one");

        Assert.True(harness.Model.ShowsSavedChoices);
        Assert.Equal(2, harness.Model.SavedChoices.Count);
        Assert.Contains("Genesis 1", harness.Model.SavedChoices[0].Label);
        Assert.Contains("Exodus 1", harness.Model.SavedChoices[1].Label);
        Assert.NotEqual(harness.Model.SavedChoices[0].Label, harness.Model.SavedChoices[1].Label);
        Assert.Equal("difference/one", harness.Model.SelectedSaved!.DifferenceId);
        Assert.True(harness.Model.HasSavedNote);
        Assert.Contains("3", harness.Model.SavedNoteText);
        Assert.Contains("Genesis 1", harness.Model.BasisText);
    }

    [Fact]
    public async Task TheSavedListTellsDifferencesOfTheSameTextApartByWhenTheyWereMade()
    {
        var harness = new Harness(alreadyMade: true);
        harness.Fixture.SavedOthers.Add(new ProposalDifferenceSaved("difference/two", "text/one", "Genesis 1",
            ProposalDifferenceReferences.Human, "2026-09-30T08:30:00Z", true));

        await harness.Model.LoadAsync(Project, "proposal/one");

        var labels = harness.Model.SavedChoices.Select(choice => choice.Label).ToArray();
        Assert.Equal(2, labels.Distinct().Count());
        Assert.Contains(DateTimeOffset.Parse("2026-10-31T00:00:00Z").ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            labels[0]);
        Assert.Contains(DateTimeOffset.Parse("2026-09-30T08:30:00Z").ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            labels[1]);
        Assert.DoesNotContain("text/one", labels[0]);
    }

    [Fact]
    public async Task TwoTextsWithTheSameTitleAreToldApartByTheStartOfTheirId()
    {
        var harness = new Harness(alreadyMade: true);
        harness.Fixture.TextId = "aaaaaaaa-1111-1111-1111-111111111111";
        harness.Fixture.SavedOthers.Add(new ProposalDifferenceSaved("difference/two", "bbbbbbbb-2222-2222-2222-222222222222",
            "Genesis 1", ProposalDifferenceReferences.Human, "2026-10-31T00:00:00Z", true));

        await harness.Model.LoadAsync(Project, "proposal/one");

        Assert.Contains("aaaaaaaa", harness.Model.SavedChoices[0].Label);
        Assert.Contains("bbbbbbbb", harness.Model.SavedChoices[1].Label);
    }

    [Fact]
    public async Task ASavedDifferenceThatCouldNotBeReadIsCountedInTheNoteBesideTheOnesListed()
    {
        var harness = new Harness(alreadyMade: true);
        harness.Fixture.SavedUnreadable = 1;

        await harness.Model.LoadAsync(Project, "proposal/one");

        Assert.False(harness.Model.ShowsSavedChoices);
        Assert.True(harness.Model.HasSavedNote);
        Assert.Contains("could not be read", harness.Model.SavedNoteText);
        Assert.Equal(0, harness.Model.SavedNotListed);
    }

    [Fact]
    public async Task OneSavedDifferenceShowsNoListButStillNamesItsTextAndReference()
    {
        var harness = new Harness(alreadyMade: true);

        await harness.Model.LoadAsync(Project, "proposal/one");

        Assert.False(harness.Model.ShowsSavedChoices);
        Assert.Contains("Genesis 1", harness.Model.BasisText);
    }

    [Fact]
    public async Task AProjectWithNoTextsSaysSoInsteadOfOfferingAButton()
    {
        var harness = new Harness { Texts = [] };

        await harness.Model.LoadAsync(Project, "proposal/one");

        Assert.True(harness.Model.ShowsNoTextsNote);
        Assert.False(harness.Model.ShowsMakeControls);
        Assert.False(harness.Model.Maker.RunCommand.CanExecute(null));

        harness.Texts = [new(GenesisId, "Genesis 1")];
        harness.Model.RefreshTexts();

        Assert.True(harness.Model.ShowsMakeControls);
        Assert.False(harness.Model.ShowsNoTextsNote);
        Assert.Equal(GenesisId, harness.Model.SelectedText!.Id);
        Assert.True(harness.Model.Maker.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefreshingTheTextsKeepsTheChosenOneAndFallsBackWhenItIsGone()
    {
        var harness = new Harness();
        var model = harness.Model;
        await model.LoadAsync(Project, "proposal/one");
        model.SelectedText = model.TextChoices[1];

        harness.Texts = [new(GenesisId, "Genesis 1"), new(ExodusId, "Exodus 1"), new(Guid.NewGuid(), "Leviticus 1")];
        model.RefreshTexts();
        Assert.Equal(ExodusId, model.SelectedText!.Id);

        harness.Texts = [new(GenesisId, "Genesis 1")];
        model.RefreshTexts();
        Assert.Equal(GenesisId, model.SelectedText!.Id);
    }

    [Fact]
    public async Task SelectingAnotherProposalWhileARunIsGoingCancelsItAndNeverShowsItsResult()
    {
        var harness = new Harness();
        var model = harness.Model;
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        CancellationToken seen = default;
        harness.Behavior = async (_, _, token) =>
        {
            seen = token;
            started.SetResult();
            await release.Task;
            harness.Made = true;
            return await harness.Fixture.Read(new ProposalDifferenceShowRequest(Project, "proposal/one"), token);
        };
        await model.LoadAsync(Project, "proposal/one");
        var run = model.Maker.RunCommand.ExecuteAsync(null);
        await started.Task;

        await model.LoadAsync(Project, "proposal/two");

        Assert.True(seen.IsCancellationRequested);
        release.SetResult();
        await run;
        Assert.Equal(RunState.Idle, model.Maker.State);
        Assert.Null(model.Summary);
    }

    [Fact]
    public async Task ClearingTheSectionCancelsARunInProgress()
    {
        var harness = new Harness();
        var model = harness.Model;
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        CancellationToken seen = default;
        harness.Behavior = async (_, _, token) =>
        {
            seen = token;
            started.SetResult();
            await release.Task;
            return CommandOutcome<ProposalDifferenceResponse>.Refused(
                new Refusal(RefusalCodes.DifferenceCancelled, FailureReason.Cancelled, "Cancelled."));
        };
        await model.LoadAsync(Project, "proposal/one");
        var run = model.Maker.RunCommand.ExecuteAsync(null);
        await started.Task;

        model.Clear();

        Assert.True(seen.IsCancellationRequested);
        Assert.False(model.IsVisible);
        release.SetResult();
        await run;
        Assert.Equal(ProposalDifferenceState.None, model.State);
    }

    [Fact]
    public async Task EverythingTheMakingControlsSayUsesWindowWordsAndNeverTime()
    {
        var shown = new[]
        {
            MotifAppStrings.ProposalDifferenceMakeAdvice, MotifAppStrings.ProposalDifferenceTextLabel,
            MotifAppStrings.ProposalDifferenceMake, MotifAppStrings.ProposalDifferenceMakeAgain,
            MotifAppStrings.ProposalDifferenceMaking, MotifAppStrings.ProposalDifferenceMakingReadingText,
            MotifAppStrings.ProposalDifferenceCancelling, MotifAppStrings.ProposalDifferenceNoTexts,
            MotifAppStrings.ProposalDifferenceNotTried, MotifAppStrings.ProposalDifferenceDecidedNone,
            MotifAppStrings.ProposalDifferenceMakeFailed,
            string.Format(System.Globalization.CultureInfo.CurrentCulture,
                MotifAppStrings.ProposalDifferenceMakingFormat, "3", "1,200"),
        };
        await Task.CompletedTask;

        WindowWordsTests.AssertWindowWords(shown);
        Assert.All(shown, text => Assert.DoesNotContain(" ms", text, StringComparison.Ordinal));
    }
}
