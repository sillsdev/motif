using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Parsimony.RuleAttempts;
using SIL.Motif.Contract.Review;
using Xunit;

namespace SIL.Motif.Tests.Contract.Parsimony.RuleAttempts;

public sealed class RuleAttemptSummaryTests
{
    private sealed class FullLedger : IRuleAttemptLedger
    {
        public RuleAttempt? Find(string shapeKey) => new(shapeKey, "rule", "tested", "why");
    }

    private static List<AddedObjectShape> Shapes(int n) =>
        [.. Enumerable.Range(0, n).Select(i =>
            new AddedObjectShape($"v{i}", "free", ["e"], [], StringCorrespondence.Of("kata" + i + "a", "kata" + i + "-")))];

    [Fact]
    public void FiveSameShapeVariantsAreReportedAdvisoryByDefault()
    {
        var summary = RuleAttemptSummary.Create(Shapes(5), NoAttempts.Instance, []);
        Assert.Equal(5, Assert.Single(summary.Groups).Count);
        Assert.False(summary.OptedIn);
        Assert.False(summary.Hold);
    }

    [Fact]
    public void OptingInHoldsUntilAnAttemptIsRecorded()
    {
        string[] optIn = [BlockingPolicy.RuleAttemptMeasureId];
        Assert.True(RuleAttemptSummary.Create(Shapes(5), NoAttempts.Instance, optIn).Hold);
        Assert.False(RuleAttemptSummary.Create(Shapes(4), NoAttempts.Instance, optIn).Hold);
        var answered = RuleAttemptSummary.Create(Shapes(5), new FullLedger(), optIn);
        Assert.False(answered.Hold);
        Assert.True(Assert.Single(answered.Groups).AttemptRecorded);
    }

    [Theory]
    [InlineData("alternation-family", ParsimonyGroupKind.AlternationFamily)]
    [InlineData("allomorph-duplicate-forms", ParsimonyGroupKind.AllomorphDuplicateForms)]
    [InlineData("exception-feature", ParsimonyGroupKind.ExceptionFeature)]
    [InlineData("duplicate-entries", ParsimonyGroupKind.DuplicateEntries)]
    [InlineData("tag-polarity", ParsimonyGroupKind.TagPolarity)]
    [InlineData("variant-same-shape", ParsimonyGroupKind.VariantSameShape)]
    public void GroupKindNamesWrittenBeforeTheNewKindsStillReadAndTheNewOnesRoundTrip(string name, ParsimonyGroupKind kind)
    {
        // New kinds are appended with explicit names, so an older report's text and ordinals are unchanged.
        Assert.Equal(kind, JsonSerializer.Deserialize<ParsimonyGroupKind>($"\"{name}\"", MotifJson.CreateOptions()));
        Assert.Equal($"\"{name}\"", JsonSerializer.Serialize(kind, MotifJson.CreateOptions()));
        Assert.True((int)ParsimonyGroupKind.DuplicateEntries < (int)ParsimonyGroupKind.TagPolarity);
    }
}
