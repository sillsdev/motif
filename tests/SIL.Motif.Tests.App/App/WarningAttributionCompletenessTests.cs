using System.Linq;
using SIL.Motif.App.ViewModels;
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
        Assert.Equal("Word count unavailable", zeroRow.ReachSummaryText);
        Assert.Contains("Motif could not follow every named connection", zeroRow.ReachStateText,
            StringComparison.Ordinal);

        table.Load([positive]);
        var positiveRow = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows));
        Assert.Equal(WarningDisplayState.ExactUses, positiveRow.AttributionState);
        Assert.True(positiveRow.IsPartialReach);
        Assert.True(positiveRow.HasPartialCount);
        Assert.Equal("At least 1 of your words", positiveRow.ReachSummaryText);
    }
}
