using System.Text.Json;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class RetirementProposalReviewProjectionTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void ProjectionKeepsOperationLinksOpinionSurfaceVerificationAndFindingDispositionSeparate()
    {
        var value = new RetirementProposalReviewProjection("proposal/one", "intent/one",
            new DryRunProjection("proposal/one", "intent/one", "Baseline", [], "effects/one", "footprint/one")
            {
                Operations = [new("op/rule", "grammar/phSegmentRule/setDisabled", null, null, [], null)],
            }, Statistics(), [new("rule", "New rule and sound class", ["op/rule"], []),
                new("bundle", "Analyses and bundle text", ["op/bundle"], []),
                new("other-references", "Other references", ["op/reference"], []),
                new("retired-form", "Deleted forms", ["op/delete"], [])],
            [new("case/one", "reading/one", "wordform/one", "ata", "qaa", "ata", "ata", "approved", "A",
                "preserved", true, true, true, "traced", "rule/one", Digest, null)],
            new("finding/one", 2, 2, 0, 1, Digest, Digest, true, false),
            new(1, 1, 0, 0, []), []);

        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var read = JsonSerializer.Deserialize<RetirementProposalReviewProjection>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal("op/rule", Assert.Single(read.Parts, part => part.Id == "rule").OperationIds[0]);
        var reading = Assert.Single(read.AffectedReadings);
        Assert.Equal("approved", reading.Opinion);
        Assert.Equal("A", reading.OpinionGlyph);
        Assert.True(reading.SurfaceUnchanged);
        Assert.Equal(reading.SurfaceBefore, reading.SurfaceAfter);
        Assert.Equal("traced", reading.RuleAttributionStatus);
        Assert.True(read.Finding.Resolved);
        Assert.False(read.Finding.Suppressed);
        Assert.Equal(1, read.UnresolvedReferences.UnresolvedApprovedAnalyses);
        Assert.Equal(1, read.UnresolvedReferences.UnresolvedAdhocRules);
    }

    private static RetirementReviewStatistics Statistics() => new(
        new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), 1, [], 0, 1, 1, 0,
        new("finding/one", 2, 2, 0, 1, Digest, Digest, true), Digest);
}
