using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Shift+Enter and Tab over authored places: which places a keep reaches and where the next gap is, with each
/// place's status set on its own so approved, guessed and unanalysed places sit side by side.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Unit")]
public sealed class PassageKeepScopeTests
{
    private static readonly Guid StoryId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LetterId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid AnalysisA = Guid.Parse("20000000-0000-0000-0000-00000000000a");
    private static readonly Guid AnalysisB = Guid.Parse("20000000-0000-0000-0000-00000000000b");

    private sealed record Place(string Form, string? Status, Guid? Analysis);

    private static OccurrenceAnchor AnchorOf(Guid text, int line, int offset) =>
        new(text, Guid.Parse($"30000000-0000-0000-0000-{line:D12}"), Guid.Parse($"40000000-0000-0000-0000-{line:D12}"), offset);

    private static TextWordsProjectedAnalysis Analysis(string key, Guid id) =>
        new(key, [new TextWordsProjectedMorph(key, key, "n", null, false, null)])
        {
            AnalysisId = id,
            Opinion = "unknown",
            Identity = new ApprovedMorphology([new ApprovedMorph("allo-" + key, "gram-" + key, null, [key])])
            { SourceAnalysisId = CanonicalId.FromGuid(id).Value },
        };

    private static StoredSelectionFixture Fixture(params (Guid Text, Place[] Words)[] texts)
    {
        var analyses = new[] { Analysis("a", AnalysisA), Analysis("b", AnalysisB) };
        var wordforms = new Dictionary<string, Guid>();
        var projectedTexts = texts.Select(text => new TextWordsProjectedText(text.Text, "Text " + text.Text.ToString()[^1],
            [new TextWordsProjectedLine(1, string.Join(" ", text.Words.Select(word => word.Form)),
                text.Words.Select((word, offset) =>
                {
                    if (!wordforms.TryGetValue(word.Form, out var wordform)) wordforms.Add(word.Form, wordform = Guid.NewGuid());
                    var analysis = analyses.FirstOrDefault(candidate => candidate.AnalysisId == word.Analysis);
                    return new TextWordsProjectedToken(word.Form, [new WritingSystemText(word.Form, "sw")], wordform,
                        word.Status, analysis?.Key, null, null, new FieldWorksLinkTarget("analyses", wordform), offset,
                        word.Analysis) { TextWritingSystem = "sw" };
                }).ToArray(), Guid.Parse($"30000000-0000-0000-0000-{1:D12}"), Guid.Parse($"40000000-0000-0000-0000-{1:D12}"), true)],
            analyses)).ToArray();
        return new StoredSelectionFixture(new TextWordsProjection(projectedTexts, wordforms.Select(pair =>
            new TextWordsProjectedWordform(pair.Value, [], [], analyses.Length, false, analyses)).ToArray()));
    }

    private static async Task<(ResultsInTextViewModel InText, ChangesViewModel Changes, FakeCommandClient Fake,
        SelectionModelFixture Models, StoredSelectionFixture Store)> OpenAsync(StoredSelectionFixture store, params Guid[] textIds)
    {
        var client = new FakeCommandClient();
        var models = new SelectionModelFixture(client);
        var selection = new SelectionViewModel(client);
        var changes = new ChangesViewModel(client);
        await changes.OpenProjectAsync(store.ProjectPath);
        var words = new TextWordsViewModel(client, selection, client.ReaderOwner);
        var inText = new ResultsInTextViewModel(words, new AssessViewModel(client, selection), _ => { }, changes, client,
            client.ReaderOwner);
        client.SelectionReaderHandler = store.OpenAsync;
        await client.ReaderOwner.ReloadAsync(store.ProjectPath, textIds, []);
        await inText.SelectionRefresh;
        return (inText, changes, client, models, store);
    }

    private static async Task CloseAsync(ResultsInTextViewModel inText, FakeCommandClient client, SelectionModelFixture models,
        StoredSelectionFixture store)
    {
        await inText.StopAsync();
        await client.ReaderOwner.StopAsync();
        await models.DisposeAsync();
        store.Dispose();
    }

    private static IEnumerable<OccurrenceAnchor> Approves(ChangesViewModel changes) =>
        changes.Items.Where(change => change.Kind == ChangeKinds.Approve).Select(change => change.Occurrence!);

    [Fact]
    public void ShiftEnterKeepsTheSameAnalysisAndSkipsHomographsAndApprovedAndStagedPlaces()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var store = Fixture((StoryId,
            [
                new Place("wa", "unapproved", AnalysisA),   // 0: the focused place
                new Place("wa", "unapproved", AnalysisA),   // 1: eligible
                new Place("wa", "unapproved", AnalysisB),   // 2: a homograph with another analysis
                new Place("wa", "approved", AnalysisA),     // 3: already approved
                new Place("wa", "unapproved", AnalysisA),   // 4: staged first by Enter
                new Place("wa", "unapproved", AnalysisA),   // 5: eligible
            ]));
            var (inText, changes, client, models, _) = await OpenAsync(store, StoryId);
            try
            {
                Assert.Equal(PassageKeepResultKind.Handled, Kind(await inText.KeepAnalysisAsync(AnchorOf(StoryId, 1, 4))));
                Assert.Equal([AnchorOf(StoryId, 1, 4)], Approves(changes));

                var result = await inText.KeepAllLikeThisAsync(AnchorOf(StoryId, 1, 0));

                Assert.True(result.Handled);
                Assert.Equal([AnchorOf(StoryId, 1, 0), AnchorOf(StoryId, 1, 1), AnchorOf(StoryId, 1, 5)],
                    result.Staged.OrderBy(anchor => anchor.Index));
                Assert.Equal([0, 1, 4, 5], Approves(changes).Select(anchor => anchor.Index).Order());
                // The homograph and the approved place got nothing, and nothing was written twice.
                Assert.DoesNotContain(client.PendingPutRequests, request => request.Change.Occurrence is { Index: 2 or 3 });
                Assert.Equal(4, client.PendingPutRequests.Count);

                var again = await inText.KeepAllLikeThisAsync(AnchorOf(StoryId, 1, 0));
                Assert.Empty(again.Staged);
                Assert.Equal(MotifAppStrings.OccurrencesNothingToKeepStatus, again.Message);
                Assert.Equal(4, client.PendingPutRequests.Count);
            }
            finally { await CloseAsync(inText, client, models, store); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ShiftEnterWithAPanelChoiceStagesThatReadingAtTheFocusedAndTickedPlacesOnly()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var store = Fixture((StoryId,
            [
                new Place("wa", "unapproved", AnalysisA),   // 0: focused, guess A
                new Place("wa", "unapproved", AnalysisA),   // 1: shares the focused guess but is not ticked
                new Place("wa", "unapproved", AnalysisB),   // 2: ticked in the panel
                new Place("wa", "approved", AnalysisB),     // 3: ticked, but already approved
                new Place("wa", "unapproved", AnalysisA),   // 4: ticked
                new Place("wa", "unapproved", AnalysisB),   // 5: not ticked
            ]));
            var (inText, changes, client, models, _) = await OpenAsync(store, StoryId);
            try
            {
                OccurrenceAnchor At(int offset) => AnchorOf(StoryId, 1, offset);
                var focused = Assert.IsType<ResultsTokenViewModel>(await inText.ReadOccurrenceAsync(At(0)));
                inText.SelectToken(focused);
                var panel = Assert.IsType<WordOccurrencesViewModel>(inText.OccurrencePanel);
                await panel.LoadPageAsync(0);
                var b = CanonicalId.FromGuid(AnalysisB).Value;
                panel.ChosenReading = panel.Readings.Single(reading => reading.Id == b);
                foreach (var offset in new[] { 2, 3, 4 }) panel.SetChecked(At(offset), true);

                var result = await inText.KeepAllLikeThisAsync(At(0), CancellationToken.None, focused);

                Assert.True(result.Handled, result.Message);
                // Reading B is what is staged, so it goes where the person focused and ticked, not to every guess A.
                Assert.Equal([0, 2, 4], result.Staged.Select(anchor => anchor.Index).Order());
                Assert.Equal([0, 2, 4], Approves(changes).Select(anchor => anchor.Index).Order());
                Assert.All(client.PendingPutRequests, request => Assert.Equal(b, request.Change.StoredAnalysisId));
            }
            finally { await CloseAsync(inText, client, models, store); }
        }, TimeSpan.FromMinutes(2));
    }

    private enum PassageKeepResultKind { Handled, Refused }

    private static PassageKeepResultKind Kind(PassageKeepResult result) =>
        result.Handled ? PassageKeepResultKind.Handled : PassageKeepResultKind.Refused;

    [Fact]
    public void TabSkipsApprovedAndStagedWordsAndFallsThroughAtTheEndOfTheText()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var store = Fixture(
                (StoryId,
                [
                    new Place("one", "approved", AnalysisA),      // 0
                    new Place("two", "unapproved", AnalysisA),    // 1: a guess, gap until staged
                    new Place("three", "approved", AnalysisB),    // 2
                    new Place("four", "unanalysed", null),        // 3: needs an analysis
                    new Place("five", "unapproved", AnalysisB),   // 4: the last gap of this Text
                ]),
                (LetterId, [new Place("six", "unanalysed", null)]));
            var (inText, changes, client, models, _) = await OpenAsync(store, StoryId, LetterId);
            try
            {
                OccurrenceAnchor At(int offset) => AnchorOf(StoryId, 1, offset);
                Assert.Equal(At(1), inText.NextGapAfter(At(0)));
                Assert.Equal(At(3), inText.NextGapAfter(At(1)));

                await inText.KeepAnalysisAsync(At(1));
                Assert.Equal([At(1)], Approves(changes));
                Assert.Equal(At(3), inText.NextGapAfter(At(0)));    // the staged guess is skipped too
                Assert.Equal(At(4), inText.NextGapAfter(At(3)));

                // No gap follows the last gap of the Text, even though another Text has one: Tab falls through.
                Assert.Null(inText.NextGapAfter(At(4)));
            }
            finally { await CloseAsync(inText, client, models, store); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void AReopenedWordPanelShowsPlacesStagedByEnterAndNeverStagesThemTwice()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var store = Fixture((StoryId,
            [
                new Place("wa", "unapproved", AnalysisA),
                new Place("wa", "unapproved", AnalysisA),
                new Place("wa", "unapproved", AnalysisA),
            ]));
            var (inText, changes, client, models, _) = await OpenAsync(store, StoryId);
            try
            {
                OccurrenceAnchor At(int offset) => AnchorOf(StoryId, 1, offset);
                var first = Assert.IsType<ResultsTokenViewModel>(await inText.ReadOccurrenceAsync(At(0)));
                var second = Assert.IsType<ResultsTokenViewModel>(await inText.ReadOccurrenceAsync(At(1)));

                inText.SelectToken(first);
                Assert.NotNull(inText.OccurrencePanel);
                Assert.True((await inText.KeepAnalysisAsync(At(0), CancellationToken.None, first)).Handled);
                Assert.Equal([At(0)], Approves(changes));

                // Opening another token of the same word builds a new panel, which reads what Changes holds.
                inText.SelectToken(second);
                var panel = Assert.IsType<WordOccurrencesViewModel>(inText.OccurrencePanel);
                await panel.LoadPageAsync(0);
                var staged = Assert.Single(panel.Page, place => place.Anchor == At(0));
                Assert.True(staged.IsStaged);
                Assert.False(staged.CanCheck);
                panel.CheckShownGaps();
                Assert.Equal(2, panel.SelectedCount);
                panel.ChosenReading = panel.Readings.Single(reading =>
                    reading.Id == CanonicalId.FromGuid(AnalysisA).Value);

                await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)panel.ApproveCommand).ExecuteAsync(null);

                Assert.True(panel.StagedAnchors.Count == 3, $"status='{panel.StatusSentence}' refusal={changes.LastRefusal}");
                Assert.Equal([0, 1, 2], client.PendingPutRequests.Select(request => request.Change.Occurrence!.Index).Order());
                Assert.Equal(3, Approves(changes).Count());
            }
            finally { await CloseAsync(inText, client, models, store); }
        }, TimeSpan.FromMinutes(2));
    }
}
