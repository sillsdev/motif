using System.Linq;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class WarningAttributionStateTests
{
    [Fact]
    public void MissingSubjectDoesNotHideAResolvedRouteAwaitingWordEvidence()
    {
        var finding = Finding(
            new WarningReach(WarningWordsPath.MissingObject) { Reason = WarningAttributionReason.StaleGuid },
            new WarningReach(WarningWordsPath.Uses) { AllomorphIds = ["form"] });
        Assert.Equal(WarningAttributionState.EvidenceUnavailable, finding.AttributionState);
        Assert.Null(finding.AttributionReason);
    }

    [Fact]
    public void ExactWordUsesDoNotInheritAnotherSubjectsMissingIdentityReason()
    {
        var finding = Finding(new WarningReach(WarningWordsPath.MissingObject)
            { Reason = WarningAttributionReason.StaleGuid }) with
        {
            YourWords = new WarningWords(WarningWordsMatch.Identity,
                [new ObjectUseWord(new WordRow("synthetic", WordRowOutcome.NoParse, "Lost", WordRowTone.Problem))], []),
        };
        Assert.Equal(WarningAttributionState.ExactUses, finding.AttributionState);
        Assert.Null(finding.AttributionReason);
    }

    [Fact]
    public void UnnamedFindingIsUnresolvedEvenBeforeAnAssessment()
    {
        var finding = Finding();
        Assert.Equal(WarningAttributionState.UnresolvedIdentity, finding.AttributionState);
        Assert.Equal(WarningAttributionReason.NoSubject, finding.AttributionReason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RollupsKeepKnownMatchesAndUnfollowedRouteLimits(int knownMatches)
    {
        var finding = Finding(
            new WarningReach(WarningWordsPath.Uses),
            new WarningReach(WarningWordsPath.UnresolvedIdentity) { Reason = WarningAttributionReason.UnsupportedKind }) with
        {
            Code = "mixed",
            YourWords = new WarningWords(WarningWordsMatch.Identity,
                System.Linq.Enumerable.Range(0, knownMatches).Select(index =>
                    new ObjectUseWord(new WordRow("word-" + index, WordRowOutcome.NoParse, "Lost", WordRowTone.Problem)))
                    .ToArray(), []),
        };
        var touched = SIL.Motif.Commands.Queries.WarningWordsQuery.Touched([finding]);
        var summary = SIL.Motif.Commands.Catalog.WarningsCommand.FromCheck(new GrammarCheckResponse([finding], true));
        var json = System.Text.Json.JsonSerializer.SerializeToElement(summary);
        Assert.Equal(knownMatches, touched!.Words);
        Assert.True(json.GetProperty("YourWords").TryGetProperty("IsComplete", out var complete));
        Assert.False(complete.GetBoolean());
        Assert.Equal("unsupported_kind", json.GetProperty("YourWords").GetProperty("AttributionLimits")[0].GetString());
        Assert.False(json.GetProperty("ByKind")[0].GetProperty("WordAttributionComplete").GetBoolean());
    }

    [Fact]
    public void AllUnfollowedRoutesCannotReportACompleteZero()
    {
        var finding = Finding(new WarningReach(WarningWordsPath.UnresolvedIdentity)
            { Reason = WarningAttributionReason.UnsupportedKind });
        finding = finding with { YourWords = SIL.Motif.Commands.Queries.WarningWordsQuery.YourWordsOf(finding, [], []) };
        var touched = SIL.Motif.Commands.Queries.WarningWordsQuery.Touched([finding]);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(touched);
        Assert.Equal(0, touched!.Words);
        Assert.True(json.TryGetProperty("IsComplete", out var complete));
        Assert.False(complete.GetBoolean());
    }

    [Fact]
    public void MissingEvidenceDoesNotDiscardOtherFindingsKnownMatches()
    {
        var known = Finding(new WarningReach(WarningWordsPath.Uses)) with
        {
            YourWords = new WarningWords(WarningWordsMatch.Identity,
                [new ObjectUseWord(new WordRow("known", WordRowOutcome.NoParse, "Lost", WordRowTone.Problem))], []),
        };
        var unavailable = Finding(new WarningReach(WarningWordsPath.Uses));
        var touched = SIL.Motif.Commands.Queries.WarningWordsQuery.Touched([known, unavailable]);
        Assert.Equal(1, touched!.Words);
        Assert.False(touched.IsComplete);
        Assert.Equal(1, touched.UnavailableFindingCount);
        Assert.Empty(touched.AttributionLimits);
    }

    [Fact]
    public void FollowedEmptyRoutesAndNoFindingsGiveCompleteZero()
    {
        var supported = Finding(new WarningReach(WarningWordsPath.Uses)) with
            { YourWords = new WarningWords(WarningWordsMatch.Identity, [], []) };
        foreach (var findings in new[] { new[] { supported }, System.Array.Empty<GrammarWarning>() })
        {
            var touched = SIL.Motif.Commands.Queries.WarningWordsQuery.Touched(findings);
            Assert.Equal(0, touched!.Words);
            Assert.True(touched.IsComplete);
            Assert.Empty(touched.AttributionLimits);
        }
    }

    private static GrammarWarning Finding(params WarningReach[] reaches) =>
        new(GrammarDiagnosticLevel.Warning, "finding",
            System.Array.ConvertAll(reaches, reach => new GrammarWarningPart("subject", GrammarWarningPartRole.Object,
                "id", "MoForm") { Reach = reach }), [], "finding");
}
