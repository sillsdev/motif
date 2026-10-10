using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class MeasureEvidenceTests
{
    [Fact]
    public void CanonicalEvidenceAndCapabilityOrderDoNotChangeTheDigest()
    {
        var measure = MeasureCatalog.Find("P-statement-unused")!;
        var first = MeasureEvidence.Compute(measure, "{\"b\":2,\"a\":1}", 1, 1);
        var second = MeasureEvidence.Compute(measure with
            { RequiredCapabilities = measure.RequiredCapabilities.Reverse().ToArray() },
            "{\"a\":1,\"b\":2}", 1, 1);
        Assert.Equal(first, second);
    }

    [Fact]
    public void EverySemanticContractInputChangesTheEvidenceDigest()
    {
        var measure = MeasureCatalog.Find("P-statement-unused")!;
        string Digest(MeasureDefinition definition, string evidence = "{}",
            int facts = 1, int projection = 1, string? scope = null, string? selection = null, string? human = null) =>
            MeasureEvidence.Compute(definition, evidence, facts, projection, scope, selection, human);
        var original = Digest(measure);
        var alternatives = new[]
        {
            Digest(measure with { Id = "different-measure" }),
            Digest(measure with { QueryId = measure.QueryId + "/next" }),
            Digest(measure with { Threshold = new(ParsimonyThresholdOperator.GreaterThan, 1, "2") }),
            Digest(measure with { RequiredCapabilities = ["different-capability"] }),
            Digest(measure, evidence: "{\"references\":1}"),
            Digest(measure, facts: 2), Digest(measure, projection: 2),
            Digest(measure, scope: "selection"), Digest(measure, selection: "different-selection"),
            Digest(measure, human: "different-human-input"),
        };
        Assert.All(alternatives, digest => Assert.NotEqual(original, digest));
        Assert.Equal(alternatives.Length, alternatives.Distinct().Count());
    }
}
