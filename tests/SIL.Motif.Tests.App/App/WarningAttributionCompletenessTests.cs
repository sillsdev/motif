using System.Linq;
using Avalonia.Input;
using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins the Warnings page's certainty when some named connections have no word route.</summary>
public sealed class WarningAttributionCompletenessTests
{
    private static AssessmentWordResult Word(string form, string allomorphId) =>
        new(form, "analysed", false, "Search completed", 1, null)
        {
            StoredAnalyses =
            [
                new ParserReading(
                    [new ParserReadingMorph("root", "meaning", "n", null, false, null)
                    { AllomorphId = allomorphId }])
                { StoredAnalysisId = "analysis-" + form },
            ],
        };

    private static GrammarWarning MixedFinding(string supportedAllomorphId) => new(
        GrammarDiagnosticLevel.Warning,
        "Mixed routes",
        [
            new GrammarWarningPart("supported form", GrammarWarningPartRole.Object,
                "11111111-1111-1111-1111-111111111111", "MoForm")
            {
                Reach = new WarningReach(WarningWordsPath.Uses) { AllomorphIds = [supportedAllomorphId] },
            },
            new GrammarWarningPart("unsupported template", GrammarWarningPartRole.Object,
                "22222222-2222-2222-2222-222222222222", "MoInflAffixTemplate")
            {
                Reach = new WarningReach(WarningWordsPath.UnresolvedIdentity)
                {
                    Reason = WarningAttributionReason.UnsupportedKind,
                },
            },
        ],
        [],
        "warning: mixed.routes")
    {
        Code = "mixed.routes",
        Origin = GrammarFindingOrigin.Check,
    };

    [Fact]
    public void ZeroAndPositiveIdentityResultsRetainTheUnfollowedRouteLimit()
    {
        var zeroFinding = MixedFinding("no-selected-analysis");
        var zero = zeroFinding with
        {
            YourWords = WarningWordsQuery.YourWordsOf(zeroFinding, [Word("other", "other-allomorph")], []),
        };
        var positiveFinding = MixedFinding("allomorph-used");
        var positive = positiveFinding with
        {
            YourWords = WarningWordsQuery.YourWordsOf(positiveFinding, [Word("match", "allomorph-used")], []),
        };

        Assert.Equal(WarningWordsMatch.Identity, zero.YourWords!.Match);
        Assert.Empty(zero.YourWords.Words);
        Assert.Equal(WarningWordsMatch.Identity, positive.YourWords!.Match);
        Assert.Single(positive.YourWords.Words);

        var table = new GrammarWarningsViewModel();
        table.Load([zero]);
        var zeroRow = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));

        Assert.Equal(WarningDisplayState.NoFollowedRouteMatch, zeroRow.AttributionState);
        Assert.Equal(string.Empty, zeroRow.ReachSummaryText);
        Assert.Equal(string.Empty, zeroRow.TableNamedWordsText);
        Assert.Equal(MotifAppStrings.WarningsReachUnavailable, zeroRow.TableNamedWordsNote);
        Assert.Contains("Motif could not follow every named connection", zeroRow.ReachStateText,
            StringComparison.Ordinal);

        table.Load([positive]);
        var positiveRow = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));
        Assert.Equal(WarningDisplayState.ExactUses, positiveRow.AttributionState);
        Assert.True(positiveRow.IsPartialReach);
        Assert.True(positiveRow.HasPartialCount);
        Assert.Equal(string.Empty, positiveRow.ReachSummaryText);
        Assert.Equal(string.Empty, positiveRow.TableNamedWordsText);
    }

    [Fact]
    public void TheInspectorWordCountRowGivesTheSameReasonWhenItsCountIsBlank()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var context = new WorkspaceContext(selection, new AssessViewModel(fake, selection),
            new ChangesViewModel(fake), fake, new NoFolderPicker(), new NoDragSource(), new BaselineViewModel(fake));
        var page = new WarningsPageModel(context);
        var finding = MixedFinding("no-selected-analysis");
        finding = finding with { YourWords = WarningWordsQuery.YourWordsOf(finding, [Word("other", "other-allomorph")], []) };
        page.Grammar.Warnings.Load([finding]);

        page.Grammar.Warnings.SelectedTableEntry = Assert.Single(page.Grammar.Warnings.Rows);

        Assert.Equal(string.Empty, page.SelectedWarningWordCount);
        Assert.Equal(MotifAppStrings.WarningsReachUnavailable, page.SelectedWarningWordCountNote);
    }

    [Theory]
    [InlineData(0, "none")]
    [InlineData(1, "1 word")]
    [InlineData(2, "2 words")]
    public void CompleteIdentityEvidenceShowsItsExactCount(int count, string expected)
    {
        var mixed = MixedFinding("allomorph-used");
        var finding = mixed with { Subject = [mixed.Subject[0]] };
        var words = Enumerable.Range(0, count).Select(index => Word("match-" + index, "allomorph-used")).ToArray();
        finding = finding with { YourWords = WarningWordsQuery.YourWordsOf(finding, words, []) };
        var table = new GrammarWarningsViewModel();
        table.Load([finding]);
        var row = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));
        Assert.False(row.IsPartialReach);
        Assert.Equal(expected, row.TableNamedWordsText);
    }


    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MissingEvidenceInAnotherFindingDoesNotBecomeAnExactCount(int count)
    {
        var mixed = MixedFinding("allomorph-used");
        var finding = mixed with { Subject = [mixed.Subject[0]] };
        var words = Enumerable.Range(0, count).Select(index => Word("match-" + index, "allomorph-used")).ToArray();
        finding = finding with { YourWords = WarningWordsQuery.YourWordsOf(finding, words, []) };
        var unavailable = finding with { YourWords = null };
        var row = new GrammarWarningRowViewModel([finding, unavailable]);
        Assert.False(row.IsPartialReach);
        Assert.Equal(string.Empty, row.TableNamedWordsText);
    }


    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoDragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths,
            DragDropEffects allowedEffects) => Task.FromResult(allowedEffects);
    }
}
