using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection.Rendering;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class DryRunOperationRenderingTests
{
    [Fact]
    public void DryRunTextKeepsTheDependencyAndEffectOperationLinkVisible()
    {
        var projection = new DryRunProjection("proposal/one", "intent/one", "Baseline",
            [new("bundle/one", "analysis/wfiMorphBundle/form", [], null)
            {
                OperationIds = ["operation/retarget"],
            }], "effects/one", "footprint/one")
        {
            Operations =
            [
                new("operation/retarget", "analysis/wfiMorphBundle/setMorph", null, null, [], null),
                new("operation/delete", "lexical/lexEntry/deleteAlternateForm", null, null,
                    ["operation/retarget"], null),
            ],
        };

        var text = CommandTextRenderer.Render(projection);

        Assert.Contains("operations (2)", text, StringComparison.Ordinal);
        Assert.Contains("dependsOn: operation/retarget", text, StringComparison.Ordinal);
        Assert.Contains("operations: operation/retarget", text, StringComparison.Ordinal);
        Assert.Contains("field=analysis/wfiMorphBundle/form", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RetirementReviewTextShowsOneProposalCountsReadingEvidenceAndDestinationRefusal()
    {
        const string digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var dryRun = new DryRunProjection("proposal/one", "intent/one", "Baseline",
            [new("bundle/one", "analysis/wfiMorphBundle/form", [new("qaa", "ta", "ra")], null)
            {
                OperationIds = ["operation/retarget"],
            }], "effects/one", "footprint/one")
        {
            Operations =
            [
                new("operation/rule", "grammar/phSegmentRule/setDisabled", null, null, [], null),
                new("operation/retarget", "analysis/wfiMorphBundle/setMorph", null, null,
                    ["operation/rule"], null),
                new("operation/delete", "lexical/lexEntry/deleteAlternateForm", null, null,
                    ["operation/retarget"], null),
            ],
        };
        var statistics = new SIL.Motif.Contract.Retirement.RetirementReviewStatistics(
            new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), 1,
            [new(true, true, 1, 1), new(true, false, 0, 0),
                new(false, true, 0, 0), new(false, false, 0, 0)],
            0, 1, 1, 0, new("finding/one", 1, 1, 0, 1, digest, digest, true), digest);
        var projection = new RetirementProposalReviewProjection("proposal/one", "intent/one", dryRun,
            statistics,
            [new("rule", "New rule and sound class", ["operation/rule"], []),
                new("bundle", "Analyses and bundle text", ["operation/retarget"], [dryRun.Effects[0]]),
                new("other-references", "Other references", [], []),
                new("retired-form", "Deleted forms", ["operation/delete"], [])],
            [new("case/one", "reading/one", "wordform/one", "ata", "qaa", "ata", "ata",
                "approved", "A", "preserved", true, true, true, "traced", "rule/one", digest, null)],
            new("finding/one", 1, 1, 0, 1, digest, digest, true, false),
            new(1, 1, 0, 0, []) { Message = "Declare every replacement destination." }, []);

        var text = CommandTextRenderer.Render(projection);

        Assert.Contains("One Proposal: proposal/one", text, StringComparison.Ordinal);
        Assert.Contains("New rule and sound class", text, StringComparison.Ordinal);
        Assert.Contains("dependsOn: operation/rule", text, StringComparison.Ordinal);
        Assert.Contains("[qaa] \"ta\" -> \"ra\"", text, StringComparison.Ordinal);
        Assert.Contains("A approved  ata: preserved", text, StringComparison.Ordinal);
        Assert.Contains("rule attribution: traced (rule/one)", text, StringComparison.Ordinal);
        Assert.Contains("finding: resolved", text, StringComparison.Ordinal);
        Assert.Contains("disposition: Not suppressed", text, StringComparison.Ordinal);
        Assert.Contains("1 distinct unresolved Approved analyses; 1 unresolved ad hoc rules; 0 other references", text,
            StringComparison.Ordinal);
    }
}
