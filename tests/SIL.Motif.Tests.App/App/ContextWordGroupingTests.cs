using SIL.Motif.App;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Words covers the whole Context: one row for each written shape in its writing system, counting every place.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ContextWordGroupingTests : IAsyncLifetime
{
    private static readonly Guid TextA = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
    private static readonly Guid TextB = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");
    private static readonly Guid TextLater = Guid.Parse("cccccccc-3333-3333-3333-333333333333");
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private readonly List<(SelectionModelFixture Fixture, TextWordsViewModel Words)> _opened = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            foreach (var (fixture, words) in _opened)
            {
                await words.StopAsync();
                await fixture.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(60));
        return Task.CompletedTask;
    }

    private static ProjectAnalysis Analysis(string key, string gloss) =>
        new(key, [new ParserReadingMorph("x", gloss, "n", null, false, null)]);

    private static WordOccurrence Place(Guid text, int line, string status = "unanalysed", ProjectAnalysis? analysis = null) =>
        new(text, text == TextA ? "Alpha" : text == TextB ? "Beta" : "Later", line, "s", status, analysis);

    private async Task<TextWordsViewModel> OpenAsync(TextWordsResponse source, IReadOnlyList<Guid>? chosen = null)
    {
        var fake = new FakeCommandClient();
        var words = new TextWordsViewModel(fake, new SelectionViewModel(fake), fake.ReaderOwner);
        var fixture = new SelectionModelFixture(fake);
        _opened.Add((fixture, words));
        await words.SetProjectAsync(ProjectPath);
        await fixture.PublishAsync(SelectionModelFixture.WithOccurrenceTexts(source), textIds: chosen);
        return words;
    }

    [Fact]
    public void ARepeatedWrittenFormIsOneRowWhoseCountKeepsEveryPlaceInEveryChosenText()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var words = await OpenAsync(new TextWordsResponse(
                [new TextWord("kitabu", Guid.NewGuid().ToString(), [Place(TextA, 1), Place(TextA, 2), Place(TextB, 1)], [], []),
                 new TextWord("mtoto", Guid.NewGuid().ToString(), [Place(TextB, 2)], [], [])], [], true, 4));

            Assert.Equal(2, words.AllCount);
            Assert.Equal(3, words.Rows.Single(row => row.Form == "kitabu").OccurrenceCount);
            Assert.Equal(4, words.OccurrenceCount);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ATextThatIsLaterContributesNothingAndChoosingItAddsItsWords()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var source = new TextWordsResponse(
                [new TextWord("kitabu", Guid.NewGuid().ToString(), [Place(TextA, 1), Place(TextLater, 1)], [], []),
                 new TextWord("later-only", Guid.NewGuid().ToString(), [Place(TextLater, 2)], [], [])], [], true, 3);
            var words = await OpenAsync(source, [TextA]);

            Assert.Equal(["kitabu"], words.Rows.Select(row => row.Form));
            Assert.Equal(1, words.Rows[0].OccurrenceCount);
            Assert.Equal(1, words.OccurrenceCount);

            var fixture = _opened[^1].Fixture;
            await fixture.PublishAsync(SelectionModelFixture.WithOccurrenceTexts(source), textIds: [TextA, TextLater]);
            Assert.Equal(2, words.AllCount);
            Assert.Equal(2, words.Rows.Single(row => row.Form == "kitabu").OccurrenceCount);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void NfcAndNfdSpellingsOfOneFormGroupOnceAndNoWordformIsGuessedForTheRow()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var composed = "caf\u00e9";
            var decomposed = "cafe\u0301";
            Assert.NotEqual(composed, decomposed);
            var words = await OpenAsync(new TextWordsResponse(
                [new TextWord(composed, Guid.NewGuid().ToString(), [Place(TextA, 1), Place(TextA, 2)], [], []) { FormWritingSystem = "fr" },
                 new TextWord(decomposed, Guid.NewGuid().ToString(), [Place(TextB, 1)], [], []) { FormWritingSystem = "fr" }],
                [], true, 3));

            var row = Assert.Single(words.Rows);
            Assert.Equal(3, row.OccurrenceCount);
            Assert.Equal(1, words.AllCount);
            Assert.Null(row.WordformId);
            Assert.Equal(decomposed, row.Form);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void WritingSystemsThatSpellAWordAlikeAreNeverFoldedTogether()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var words = await OpenAsync(new TextWordsResponse(
                [new TextWord("ana", Guid.NewGuid().ToString(), [Place(TextA, 1)], [], []) { FormWritingSystem = "sw" },
                 new TextWord("ana", Guid.NewGuid().ToString(), [Place(TextA, 2)], [], []) { FormWritingSystem = "en" }],
                [], true, 2));

            Assert.Equal(2, words.Rows.Count);
            Assert.Equal(["en", "sw"], words.Rows.Select(row => row.FormWritingSystem).OrderBy(ws => ws));
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void HomographReadingsStayDistinctUnderTheOneSpelling()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var love = Analysis("love", "love");
            var like = Analysis("like", "like");
            var words = await OpenAsync(new TextWordsResponse(
                [new TextWord("anapenda", Guid.NewGuid().ToString(),
                    [Place(TextA, 1, "approved", love), Place(TextA, 2, "approved", like), Place(TextB, 1, "approved", love)],
                    [love, like], []) { Analyses = [love, like] }], [], true, 3));

            var row = Assert.Single(words.Rows);
            Assert.True(row.HasSeveralAnalyses);
            Assert.Equal(2, row.Summary!.ChosenAnalysisCount);
            Assert.Equal(3, row.OccurrenceCount);
            Assert.Equal(1, words.SeveralFilterCount);
        }, TimeSpan.FromSeconds(60));
    }

    private static SelectionWordSummary Member(Guid? wordform, string form, string? ws, int places, params Guid[] chosen) =>
        new(new TextWordKey(wordform, form, ws), "approved", places, new Dictionary<string, int>(), 0, false, false, null, null)
        { ChosenAnalysisIds = chosen, ChosenAnalysisCount = chosen.Length, ApprovedAnalysisCount = 1 };

    [Fact]
    public void GroupingNeverMergesWritingSystemsAndAddsPlacesAndDistinctReadingsAcrossWordforms()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var shapes = ContextWordShape.Group(
        [
            Member(Guid.NewGuid(), "ana", "sw", 2, a),
            Member(Guid.NewGuid(), "ana", "en", 5),
            Member(Guid.NewGuid(), "ana", "sw", 3, b),
        ]);

        Assert.Equal(2, shapes.Count);
        var sw = shapes.Single(shape => shape.WritingSystem == "sw");
        Assert.Equal(5, sw.Row.OccurrenceCount);
        Assert.Equal(2, sw.Row.ChosenAnalysisCount);
        Assert.True(sw.HasSeveralWordforms);
        Assert.Equal(2, sw.Row.ApprovedAnalysisCount);
        Assert.Equal(5, shapes.Single(shape => shape.WritingSystem == "en").Row.OccurrenceCount);
    }

    private static readonly Guid FormA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid FormB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    private static ProjectAnalysis Stored(string gloss, string id, string opinion) =>
        Analysis(gloss, gloss) with { StoredAnalysisId = CanonicalId.FromGuid(Guid.Parse(id)).Value, StoredAnalysisOpinion = opinion };

    private static readonly ProjectAnalysis Book = Stored("book", "dddddddd-0000-0000-0000-000000000001", ReadingGrade.Approved);
    private static readonly ProjectAnalysis Love = Stored("love", "dddddddd-0000-0000-0000-000000000002", ReadingGrade.Candidate);

    private async Task<(TextWordsViewModel Words, FakeCommandClient Fake, ChangesViewModel Changes, TextWordRowViewModel Row)>
        OpenSharedAsync(bool refuseOthers = false, bool otherHasReading = true, bool route = false)
    {
        var fake = new FakeCommandClient();
        var words = new TextWordsViewModel(fake, new SelectionViewModel(fake), fake.ReaderOwner);
        var fixture = new SelectionModelFixture(fake);
        _opened.Add((fixture, words));
        await words.SetProjectAsync(ProjectPath);
        words.HomographReadsRefused = refuseOthers;
        var changes = new ChangesViewModel(fake);
        if (route) words.WordRowRoutes = new WordRowRoutes { Changes = changes, StageOpinion = change => changes.StageStoredOpinionAsync(change) };
        await fixture.PublishAsync(SelectionModelFixture.WithOccurrenceTexts(new TextWordsResponse(
            [new TextWord("kitabu", FormA.ToString(), [Place(TextA, 1), Place(TextA, 2)], [Book], []) { Analyses = [Book] },
             new TextWord("kitabu", FormB.ToString(), [Place(TextB, 1)], [], []) { Analyses = otherHasReading ? [Love] : [] }], [], true, 3)));
        await changes.OpenProjectAsync(ProjectPath);
        var row = Assert.Single(words.Rows);
        words.RealizeRow(row);
        row.Listed.IsOpen = true;
        await words.CardPending;
        await words.SettleVisibleDetailsAsync();
        return (words, fake, changes, row);
    }

    private static WordOccurrencesViewModel PanelOf(TextWordsViewModel words, FakeCommandClient fake, ChangesViewModel changes,
        TextWordRowViewModel row) => ContextWordPanel.Build(row, words.ShapeOf(row.Summary!.Key)!, fake.ReaderOwner.Reader!,
        fake.ReaderOwner.Summary!.Texts, changes, null, _ => false)!;

    [Fact]
    public void ASharedRowKeepsEachHomographsOwnReadingLabelledAndItsOwnFacts()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (words, fake, changes, row) = await OpenSharedAsync();

            Assert.Equal(2, row.Homographs.Count);
            Assert.Equal(["book", "love"], row.Homographs.Select(homograph => homograph.Analyses.Single().Morphs[0].Gloss)
                .OrderBy(gloss => gloss));
            Assert.Equal(ReadingGrade.Approved, row.Homographs.Single(homograph => homograph.WordformId == FormA)
                .Analyses.Single().StoredAnalysisOpinion);
            Assert.Equal(ReadingGrade.Candidate, row.Homographs.Single(homograph => homograph.WordformId == FormB)
                .Analyses.Single().StoredAnalysisOpinion);
            Assert.Equal(2, row.StoredAnalyses.Count);
            var panel = PanelOf(words, fake, changes, row);
            Assert.Equal(2, panel.Readings.Count);
            Assert.All(panel.Readings, reading => Assert.StartsWith("Homograph ", reading.Label));
            Assert.Contains(panel.Readings, reading => reading.Label.Contains("book"));
            Assert.Contains(panel.Readings, reading => reading.Label.Contains("love"));
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ChoosingOneHomographsReadingStagesOnlyThatHomographsPlaces()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (words, fake, changes, row) = await OpenSharedAsync();
            var panel = PanelOf(words, fake, changes, row);
            await panel.LoadPageAsync(0);
            panel.CheckShownGaps();
            Assert.Equal(3, panel.SelectedCount);

            panel.ChosenReading = panel.Readings.Single(reading => reading.Label.Contains("book"));
            await ((IAsyncRelayCommand)panel.ApproveCommand).ExecuteAsync(null);

            Assert.Equal(2, fake.PendingPutRequests.Count);
            Assert.All(fake.PendingPutRequests, request =>
            {
                Assert.Equal(CanonicalId.FromGuid(FormA).Value, request.Change.WordformId);
                Assert.Equal(Book.StoredAnalysisId, request.Change.StoredAnalysisId);
                Assert.Equal(TextA, request.Change.Occurrence!.TextId);
            });
            Assert.Equal(2, panel.StagedAnchors.Count);
            Assert.Equal(1, panel.SelectedCount);
            Assert.Contains("left unchanged", panel.StatusSentence);

            panel.ChosenReading = panel.Readings.Single(reading => reading.Label.Contains("love"));
            await ((IAsyncRelayCommand)panel.ApproveCommand).ExecuteAsync(null);

            Assert.Equal(3, fake.PendingPutRequests.Count);
            var last = fake.PendingPutRequests[^1].Change;
            Assert.Equal(CanonicalId.FromGuid(FormB).Value, last.WordformId);
            Assert.Equal(Love.StoredAnalysisId, last.StoredAnalysisId);
            Assert.Equal(TextB, last.Occurrence!.TextId);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void TheKeyboardShortcutOnASharedRowSeesEveryHomographsReadingsAndStagesNothing()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, fake, _, row) = await OpenSharedAsync();

            var result = await row.StageOpinionShortcutAsync(KeyboardShortcutBehavior.Approve);

            Assert.True(result.Handled);
            Assert.Contains("2 analyses", result.StatusMessage);
            Assert.Empty(fake.PendingPutRequests);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void WhenAnotherHomographsReadingsCannotBeReadNothingIsOfferedOrStagedAtEitherHomograph()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (words, fake, changes, row) = await OpenSharedAsync(refuseOthers: true);

            Assert.True(row.HomographsUnread);
            Assert.Empty(row.StoredAnalyses);
            var panel = PanelOf(words, fake, changes, row);
            Assert.Empty(panel.Readings);
            Assert.Contains("could not be read", panel.StatusSentence);
            await panel.LoadPageAsync(0);
            panel.CheckShownGaps();
            Assert.False(panel.CanApprove);
            Assert.True((await row.StageOpinionShortcutAsync(KeyboardShortcutBehavior.Approve)).Handled);
            Assert.Empty(fake.PendingPutRequests);

            words.HomographReadsRefused = false;
            Assert.True(await words.ReadHomographsAsync(row));
            Assert.Equal(2, row.Homographs.Count);
            Assert.Equal(2, PanelOf(words, fake, changes, row).Readings.Count);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void TheKeyboardShortcutOnASharedRowStagesAtTheOnlyWordformThatHoldsAReading()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, fake, _, row) = await OpenSharedAsync(otherHasReading: false, route: true);

            var result = await row.StageOpinionShortcutAsync(KeyboardShortcutBehavior.Disapprove);

            Assert.True(result.Handled);
            var request = Assert.Single(fake.PendingPutRequests);
            Assert.Equal(CanonicalId.FromGuid(FormA).Value, request.Change.WordformId);
            Assert.Equal(Book.StoredAnalysisId, request.Change.StoredAnalysisId);
        }, TimeSpan.FromSeconds(60));
    }
}
