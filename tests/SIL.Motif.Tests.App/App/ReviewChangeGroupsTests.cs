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
    private const string ProjectPath = @"C:\projects\markings.fwdata";

    [Fact]
    public async Task GroupsFollowTheDesignedOrderAndUseTheDesignedTitles()
    {
        var changes = new[]
        {
            Change("uncertain", "uncertain", ChangeKinds.Approve),
            Change("spelling", "spelling", ChangeKinds.IncorrectSpelling),
            Change("removed", "removed", "remove-analysis", ReadingGrade.Disapproved),
            Change("accepted", "accepted", ChangeKinds.AddCandidate, groupId: "accepted-set"),
            Change("added", "added", ChangeKinds.AddCandidate),
            Change("down-to-unknown", "down-to-unknown", ChangeKinds.Candidate, ReadingGrade.Disapproved),
            Change("down-to-approved", "down-to-approved", ChangeKinds.Approve, ReadingGrade.Disapproved),
            Change("approved-down", "approved-down", ChangeKinds.Reject, ReadingGrade.Approved),
            Change("up-to-unknown", "up-to-unknown", ChangeKinds.Candidate, ReadingGrade.Approved),
            Change("unknown-down", "unknown-down", ChangeKinds.Reject),
            Change("unknown-up", "unknown-up", ChangeKinds.Approve),
        };
        var (page, _) = await OpenReviewAsync(changes, ["uncertain"]);

        Assert.Equal(
        [
            "Unknown → Approved",
            "Unknown → Disapproved",
            "Approved → Disapproved",
            "Approved → Unknown",
            "Disapproved → Approved",
            "Disapproved → Unknown",
            "Added",
            "Removed",
            "Spelling → Incorrect",
            "Uncertain — check again",
        ], page.ReviewGroups.Select(group => group.Title));
        Assert.Equal("Unknown → Approved", Assert.Single(page.ReviewGroups[^1].Items).TransitionText);
    }

    [Fact]
    public async Task ItemsAreSortedByTextSentenceAndWord()
    {
        var firstText = Guid.Parse("00000002-0000-0000-0000-000000000000");
        var secondText = Guid.Parse("00000001-0000-0000-0000-000000000000");
        var paragraph = Guid.Parse("00000003-0000-0000-0000-000000000000");
        var firstSentence = Guid.Parse("00000004-0000-0000-0000-000000000000");
        var secondSentence = Guid.Parse("00000005-0000-0000-0000-000000000000");
        var changes = new[]
        {
            Change("later-text", "a", ChangeKinds.Approve,
                occurrence: new OccurrenceAnchor(secondText, paragraph, firstSentence, 0)),
            Change("later-word", "a", ChangeKinds.Approve,
                occurrence: new OccurrenceAnchor(firstText, paragraph, firstSentence, 2)),
            Change("later-sentence", "a", ChangeKinds.Approve,
                occurrence: new OccurrenceAnchor(firstText, paragraph, secondSentence, 0)),
            Change("first-word", "z", ChangeKinds.Approve,
                occurrence: new OccurrenceAnchor(firstText, paragraph, firstSentence, 1)),
        };
        var textOrder = new Dictionary<Guid, int> { [firstText] = 0, [secondText] = 1 };
        var (page, _) = await OpenReviewAsync(changes, occurrenceLocation: occurrence =>
            new TextOccurrenceLocation(textOrder[occurrence.TextId],
                occurrence.SegmentId == firstSentence ? 0 : 1, occurrence.Index,
                $"Text {textOrder[occurrence.TextId] + 1}, sentence {(occurrence.SegmentId == firstSentence ? 1 : 2)}, word {occurrence.Index + 1}"));

        Assert.Equal(["first-word", "later-word", "later-sentence", "later-text"],
            Assert.Single(page.ReviewGroups).Items.Select(change => change.ChangeId));
        Assert.Equal("Text 1, sentence 1, word 2",
            Assert.Single(page.ReviewGroups).Items.Single(item => item.ChangeId == "first-word").WhereText);
    }

    [Fact]
    public async Task AddedItemsNameWhetherTheyCameFromAnAddOrAnAcceptedSet()
    {
        var (page, _) = await OpenReviewAsync(
        [
            Change("accepted", "accepted", ChangeKinds.AddCandidate, groupId: "accepted-set"),
            Change("added", "added", ChangeKinds.AddCandidate),
        ]);

        var group = Assert.Single(page.ReviewGroups);
        Assert.Equal("Added", group.Title);
        Assert.Equal("Added as Unknown from Add",
            group.Items.Single(item => item.ChangeId == "added").SourceText);
        Assert.Equal("Added as Unknown from accepting a set",
            group.Items.Single(item => item.ChangeId == "accepted").SourceText);
        Assert.Equal("· 2 words", group.WordCountText);
        Assert.Equal("Undo: accepted",
            group.Items.Single(item => item.ChangeId == "accepted").UndoAutomationName);
    }

    [Fact]
    public async Task OpinionAddsShareAddedGroupAndNameTheirResultAndSource()
    {
        var (page, _) = await OpenReviewAsync(
        [
            Change("approved-reading", "approved", ChangeKinds.Approve, parserOnly: true),
            Change("disapproved-reading", "disapproved", ChangeKinds.Reject, parserOnly: true),
        ]);

        var group = Assert.Single(page.ReviewGroups);
        Assert.Equal("Added", group.Title);
        Assert.Equal("Added as Approved from PanGloss",
            group.Items.Single(item => item.ChangeId == "approved-reading").SourceText);
        Assert.Equal("Added as Disapproved from PanGloss",
            group.Items.Single(item => item.ChangeId == "disapproved-reading").SourceText);
    }

    [Fact]
    public async Task UnrecognizedPendingChangeDoesNotBreakReviewGroups()
    {
        var (page, _) = await OpenReviewAsync([Change("future", "word", "future-change")]);

        Assert.NotEmpty(page.ReviewGroups);
    }

    [Fact]
    public void ExpandedContextIsUnavailableWhenTheSourceSentenceIsNotLoaded()
    {
        var change = new ChangeViewModel(ChangeKinds.Approve, "word", "reading",
            occurrence: new OccurrenceAnchor(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0));

        change.ToggleContext();

        Assert.True(change.HasUnavailableContext);
    }

    [Fact]
    public async Task UndoAllRemovesEveryChangeInTheGroupOneAtATime()
    {
        var (page, fake) = await OpenReviewAsync(
        [
            Change("first", "first", ChangeKinds.AddCandidate),
            Change("second", "second", ChangeKinds.AddCandidate),
            Change("other", "other", ChangeKinds.IncorrectSpelling),
        ]);

        var group = page.ReviewGroups.Single(item => item.Title == "Added");
        await group.UndoAllCommand.ExecuteAsync(null);

        Assert.Equal(["first", "second"], fake.PendingRemoveRequests.Select(request => request.ChangeId));
        Assert.Equal("other", Assert.Single(page.Context.Changes.Items).ChangeId);
    }

    [Fact]
    public async Task UndoAllInOneVisibleGroupKeepsOtherMembersOfTheAcceptedSet()
    {
        var (page, fake) = await OpenReviewAsync(
        [
            Change("stale", "kitabu", ChangeKinds.AddCandidate, groupId: "accepted-set"),
            Change("kept", "kitabu", ChangeKinds.AddCandidate, groupId: "accepted-set"),
        ]);
        fake.PendingChangesIs(page.Context.Changes.Snapshot with
        {
            FitSummary = [new ChangeFit("stale", false, []), new ChangeFit("kept", true, [])],
        });
        await page.Context.Changes.ReloadAsync();

        await page.ReviewGroups.Single(group => group.IsNoLongerFits).UndoAllCommand.ExecuteAsync(null);

        Assert.Equal("stale", Assert.Single(fake.PendingRemoveRequests).ChangeId);
        Assert.Equal("kept", Assert.Single(page.Context.Changes.Items).ChangeId);
    }

    [Fact]
    public async Task UndoAllRemovesAnAcceptedSetByItsSharedGroupIdOnce()
    {
        var (page, fake) = await OpenReviewAsync(
        [
            Change("accepted-one", "one", ChangeKinds.AddCandidate, groupId: "accepted-set"),
            Change("accepted-two", "two", ChangeKinds.AddCandidate, groupId: "accepted-set"),
            Change("added", "added", ChangeKinds.AddCandidate),
        ]);

        var group = page.ReviewGroups.Single(item => item.Title == "Added");
        await group.UndoAllCommand.ExecuteAsync(null);

        Assert.Equal(["added", "accepted-set"], fake.PendingRemoveRequests.Select(request => request.ChangeId));
        Assert.Empty(page.Context.Changes.Items);
    }

    [Fact]
    public async Task UncertainActionsUseWordSpecificAccessibleNames()
    {
        var (page, _) = await OpenReviewAsync(
        [
            Change("one", "kitabu", ChangeKinds.Approve),
            Change("two", "kitabu cha", ChangeKinds.Approve),
        ], ["one", "two"]);

        var changes = Assert.Single(page.ReviewGroups).Items;
        Assert.Equal(["Check again: kitabu", "Check again: kitabu cha"],
            changes.Select(change => change.CheckAgainAutomationName));
        Assert.Equal(["Undo: kitabu", "Undo: kitabu cha"], changes.Select(change => change.UndoAutomationName));
    }

    [Fact]
    public async Task NoLongerFitsIsItsOwnGroupAtTheTopAndUncertainIsLast()
    {
        var changes = new[]
        {
            Change("unsure", "unsure", ChangeKinds.Approve),
            Change("kept", "kept", ChangeKinds.Approve),
            Change("gone", "gone", ChangeKinds.Approve),
        };
        var (page, _) = await OpenReviewAsync(changes, ["unsure"], noLongerFitsChangeIds: ["gone"]);

        Assert.Equal(["No longer fits", "Unknown → Approved", "Uncertain — check again"],
            page.ReviewGroups.Select(group => group.Title));
        var noLongerFits = page.ReviewGroups[0];
        Assert.True(noLongerFits.IsNoLongerFits);
        Assert.Equal("FieldWorks changed this word since you decided. It can't be applied as it is.", noLongerFits.Note);
        Assert.Equal("Remove the ones that no longer fit", noLongerFits.UndoAllText);
        Assert.Equal("gone", Assert.Single(noLongerFits.Items).ChangeId);
        var ordinary = page.ReviewGroups[1];
        Assert.False(ordinary.HasNote);
        Assert.Equal("Undo all", ordinary.UndoAllText);
        var uncertain = page.ReviewGroups[2];
        Assert.True(uncertain.IsUncertain);
        Assert.Equal("The sentence changed in FieldWorks since you decided.", uncertain.Note);
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
        Assert.Equal("Uncertain", unsure.NoteTitle);
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

    private static async Task<(ReviewPageModel Page, FakeCommandClient Client)> OpenReviewAsync(
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
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        return (page, fake);
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
