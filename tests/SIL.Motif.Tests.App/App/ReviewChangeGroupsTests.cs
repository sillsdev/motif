using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ReviewChangeGroupsTests
{
    [UiThreadFact]
    public async Task AProposalChangeEntryUsesItsSuppliedActionsAndSharesTheLookedAtState()
    {
        var change = new ChangeViewModel(ChangeKinds.Approve, "word", "reading");
        ReviewEntryViewModel? undone = null;
        var entry = new ReviewEntryViewModel(null, change, _ => Task.CompletedTask, item =>
        {
            undone = item;
            return Task.CompletedTask;
        });

        entry.IsLookedAt = true;
        Assert.True(change.IsLookedAt);
        Assert.False(entry.KeepCommand.CanExecute(null));
        await entry.UndoCommand.ExecuteAsync(null);
        Assert.Same(entry, undone);
    }

    private const string ProjectPath = @"C:\projects\markings.fwdata";

    [Fact]
    public void ExpandedContextIsUnavailableWhenTheSourceSentenceIsNotLoaded()
    {
        var change = new ChangeViewModel(ChangeKinds.Approve, "word", "reading",
            occurrence: new OccurrenceAnchor(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0));

        change.ToggleContext();

        Assert.True(change.HasUnavailableContext);
    }

    public static TheoryData<string, string> FitCheckReasons() => new()
    {
        { ChangeFitReasons.WordformDeleted("wordform/x"), "the word kitabu was deleted in FieldWorks" },
        { ChangeFitReasons.WordformChangedForm("wordform/x"), "the spelling of kitabu was changed in FieldWorks" },
        { ChangeFitReasons.WordformSpellingChanged("wordform/x"),
            "the spelling status of kitabu was changed in FieldWorks" },
        { ChangeFitReasons.AnalysisMissing("analysis/x", "wordform/x"),
            "the analysis ki-tabu was deleted or moved in FieldWorks" },
        { ChangeFitReasons.AnalysisReadingChanged("analysis/x"), "the analysis ki-tabu was edited in FieldWorks" },
        { ChangeFitReasons.AnalysisOpinionChanged("analysis/x"), "the opinion on ki-tabu was changed in FieldWorks" },
        { ChangeFitReasons.MorphReferenceMissing("morph/x"), "a morpheme in ki-tabu was deleted or changed in FieldWorks" },
        { ChangeFitReasons.ReadingAlreadyExists("wordform/x"), "FieldWorks already has this analysis" },
        { ChangeFitReasons.BaselineNotCurrent, "FieldWorks saved the project since you decided; check again" },
        { ChangeFitReasons.FingerprintMalformed, "Motif can no longer check this change; undo it and make it again" },
        { ChangeFitReasons.SpellingEvidenceMissing, "Motif can no longer check this change; undo it and make it again" },
        { ChangeFitReasons.MappingMissing, "Motif can no longer check this change; undo it and make it again" },
        { "A reason no one wrote.", "FieldWorks changed this word since you decided" },
    };

    [Theory]
    [MemberData(nameof(FitCheckReasons))]
    public void NoLongerFitsSaysWhatChangedInTheLinguistsWords(string reason, string expected)
    {
        var analysis = new ReviewAnalysis(new ParserReading(
            [new ParserReadingMorph("ki-", "7", "n", null, false, null),
             new ParserReadingMorph("tabu", "book", "n", null, false, null)]), ReadingGrade.Candidate, true, true);

        var change = new ChangeViewModel(ChangeKinds.Approve, "kitabu", "reading",
            fit: new ChangeFit("change", false, [reason]), analyses: [analysis], storedAnalysisId: "analysis/x");

        Assert.Equal(expected, change.DetailText);
        Assert.DoesNotContain("/x", change.DetailText);
    }

    [Fact]
    public void ARowShowsItsOneAnalysisAndLeavesOutAnEmptyWhere()
    {
        var chosen = new ReviewAnalysis(new ParserReading(
            [new ParserReadingMorph("wa-", "2", "n", null, false, null)]), ReadingGrade.Candidate, true, true);
        var other = new ReviewAnalysis(new ParserReading(
            [new ParserReadingMorph("x", "y", "n", null, false, null)]), ReadingGrade.Candidate, false, true);

        var change = new ChangeViewModel(ChangeKinds.Approve, "watoto", "reading",
            fit: new ChangeFit("change", true, []), analyses: [other, chosen], storedAnalysisId: "analysis/x");

        Assert.Equal(["wa-"], change.RowMorphs.Select(morph => morph.Form));
        Assert.False(change.RowAnalysisIsParserBuilt);
        Assert.True(change.StillFits);
        Assert.Equal("Staged", change.NoteTitle);
        Assert.Equal(string.Empty, change.DetailText);
        Assert.False(change.HasDetailText);
    }

    [Fact]
    public void AnAddedRowNamesItsSourceAndAnUncertainRowIsMarkedUncertain()
    {
        var added = new ChangeViewModel(ChangeKinds.Approve, "chakula", "reading",
            fit: new ChangeFit("added", true, []),
            analyses: [new ReviewAnalysis(new ParserReading([]), ReadingGrade.NoOpinion, true, false)]);
        var unsure = new ChangeViewModel(ChangeKinds.Approve, "watoto", "reading",
            fit: new ChangeFit("unsure", ChangeFitStatus.Uncertain, ["x"]));

        Assert.True(added.RowAnalysisIsParserBuilt);
        Assert.Equal("Added as Approved from PanGloss", added.DetailText);
        Assert.Equal("Needs another look", unsure.NoteTitle);
        Assert.False(unsure.StillFits);
    }

    [Fact]
    public void AnUncertainRowRepeatsNoGroupNoteAndNamesAnyOtherReason()
    {
        const string wordsChanged = "The words in the source sentence have changed.";
        const string notReparsed = "The paragraph parse is not current.";
        ChangeViewModel Unsure(string reason) => new(ChangeKinds.Approve, "watoto", "reading",
            fit: new ChangeFit("unsure", ChangeFitStatus.Uncertain, [reason])
            {
                Uncertainty = new ChangeUncertainty(reason, [], []),
            });

        Assert.False(Unsure(wordsChanged).HasDetailText);
        Assert.Equal("FieldWorks has not reparsed this paragraph after the edit.", Unsure(notReparsed).DetailText);
    }

    [UiThreadTheory]
    [InlineData(ChangeFitStatus.Fits, "Still fits")]
    [InlineData(ChangeFitStatus.NoLongerFits, "No longer fits")]
    [InlineData(ChangeFitStatus.Uncertain, "Needs another look")]
    public async Task CompactReviewFactsKeepFitAndExactTextLocationSeparateFromOpinion(string status,
        string expectedFit)
    {
        var occurrence = new OccurrenceAnchor(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2);
        var (context, fake) = await OpenReviewAsync([Change("one", "kitabu", ChangeKinds.Approve,
            occurrence: occurrence)],
            uncertainChangeIds: status == ChangeFitStatus.Uncertain ? ["one"] : null,
            noLongerFitsChangeIds: status == ChangeFitStatus.NoLongerFits ? ["one"] : null,
            occurrenceLocation: _ => new TextOccurrenceLocation(0, 3, 2, "Story, line 4, word 3"));
        var change = Assert.Single(context.Changes.Items);
        change.Listed = new ListedWordViewModel(WordRowViewModel.NotParsed(change.Word));
        change.SetWhereText(context.OccurrenceLocation(occurrence)?.Description ?? "Text location not loaded");
        var row = change.Presentation;

        Assert.Equal(expectedFit, row.PendingChange!.FitText);
        Assert.Equal("Story, line 4, word 3", row.PendingChange.WhereText);
        Assert.Equal("Unknown → Approved", row.StagedText);
        Assert.Equal("Keep: kitabu", row.PendingChange.KeepAutomationName);
        Assert.Equal("Undo: kitabu", row.PendingChange.UndoAutomationName);
        Assert.Empty(fake.ReviewMarkRequests);
        Assert.Empty(fake.ApplyPendingRequests);
    }

    [UiThreadFact]
    public async Task CompactReviewWithoutAFitResultDoesNotClaimTheChangeStillFits()
    {
        var (context, fake) = await OpenReviewAsync([Change("one", "kitabu", ChangeKinds.Approve)]);
        fake.PendingChangesIs(context.Changes.Snapshot with { FitSummary = [] });
        await context.Changes.ReloadAsync();
        var change = Assert.Single(context.Changes.Items);
        change.Listed = new ListedWordViewModel(WordRowViewModel.NotParsed(change.Word));

        Assert.Equal("Not checked", change.Presentation.PendingChange!.FitText);
        Assert.Equal("Not tied to a text occurrence", change.Presentation.PendingChange.WhereText);
        Assert.False(change.Presentation.PendingChange.StillFits);
    }

    private static async Task<(WorkspaceContext Context, FakeCommandClient Client)> OpenReviewAsync(
        IReadOnlyList<PendingChange> changes, string[]? uncertainChangeIds = null,
        Func<OccurrenceAnchor, TextOccurrenceLocation?>? occurrenceLocation = null,
        string[]? noLongerFitsChangeIds = null)
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", changes,
            changes.Select(change => new ChangeFit(change.ChangeId,
                uncertainChangeIds?.Contains(change.ChangeId) == true ? ChangeFitStatus.Uncertain
                    : noLongerFitsChangeIds?.Contains(change.ChangeId) == true ? ChangeFitStatus.NoLongerFits
                    : ChangeFitStatus.Fits, [])).ToArray()));
        var selection = new SelectionViewModel(fake);
        var context = new WorkspaceContext(selection, new AssessViewModel(fake, selection),
            new ChangesViewModel(fake), fake, new FolderPicker(), new DragSource(), new BaselineViewModel(fake));
        if (occurrenceLocation is not null) context.RegisterOccurrenceLocationProvider(occurrenceLocation);
        await context.OpenProjectAsync(ProjectPath);
        return (context, fake);
    }

    private static PendingChange Change(string id, string word, string kind,
        string opinion = ReadingGrade.Candidate, OccurrenceAnchor? occurrence = null, string? groupId = null,
        bool parserOnly = false)
    {
        var stored = !parserOnly && kind is ChangeKinds.Approve or ChangeKinds.Reject or ChangeKinds.Candidate or "remove-analysis";
        return new PendingChange(id, "wordform/" + id, word, kind, null, word, ["operation/" + id])
        {
            Analyses = stored ? [new ReviewAnalysis(new ParserReading([]), opinion, true, true)]
                : parserOnly ? [new ReviewAnalysis(new ParserReading([]), opinion, true, false)] : [],
            OriginPage = WorkspacePage.Texts.ToString(),
            Occurrence = occurrence,
            StoredAnalysisId = stored ? "analysis/" + id : null,
            GroupId = groupId,
        };
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) => Task.FromResult(allowedEffects);
    }
}
