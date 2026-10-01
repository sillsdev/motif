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

    private static GrammarWarning Finding(params WarningReach[] reaches) =>
        new(GrammarDiagnosticLevel.Warning, "finding",
            System.Array.ConvertAll(reaches, reach => new GrammarWarningPart("subject", GrammarWarningPartRole.Object,
                "id", "MoForm") { Reach = reach }), [], "finding");
}
