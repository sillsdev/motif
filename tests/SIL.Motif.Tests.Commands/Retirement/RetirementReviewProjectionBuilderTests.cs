using SIL.Motif.Commands.Retirement;
using SIL.Motif.Projection.Retirement;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class RetirementReviewProjectionBuilderTests
{
    [Fact]
    public void UnresolvedCountsUseDistinctApprovedAnalysesAndAdhocRulesFromTheCensus()
    {
        var approved = Reference("bundle-morph", 1, 2, 3, "approved");
        var duplicateApprovedRow = approved with { TargetForm = Guid.Parse("00000000-0000-0000-0000-000000000004") };
        var adhoc = Reference("adhoc-first-allomorph", 5, null, null, null) with
        {
            Rule = Guid.Parse("00000000-0000-0000-0000-000000000005"),
            Grouped = false,
            Enabled = true,
        };
        var footprint = Footprint([approved, duplicateApprovedRow, adhoc]);
        var diagnostics = new AllomorphReferenceDestinationDiagnostics(
            [approved, duplicateApprovedRow, adhoc], 1, 1, 0, 0,
            "This form has unresolved references.");

        var review = RetirementProposalReviewProjectionBuilder.BuildUnresolvedReview(footprint, diagnostics);

        Assert.Equal(1, review.UnresolvedApprovedAnalyses);
        Assert.Equal(1, review.UnresolvedAdhocRules);
        Assert.Equal(3, review.Rows.Count);
        Assert.Equal(diagnostics.Message, review.Message);
    }

    [Fact]
    public void UnresolvedCountsThatDisagreeWithTheirRowsAreRejected()
    {
        var approved = Reference("bundle-morph", 1, 2, 3, "approved");
        var footprint = Footprint([approved]);
        var diagnostics = new AllomorphReferenceDestinationDiagnostics(
            [approved], 2, 0, 0, 0, "This form has unresolved references.");

        Assert.Throws<InvalidDataException>(() =>
            RetirementProposalReviewProjectionBuilder.BuildUnresolvedReview(footprint, diagnostics));
    }

    private static AllomorphReference Reference(string kind, int source, int? analysis, int? wordform,
        string? opinion) => new(Id(9), Id(source), "WfiMorphBundle", "WfiMorphBundle", "Morph", null,
        kind, "rel", "atomic", false, Id(source), analysis is { } a ? Id(a) : null,
        wordform is { } w ? Id(w) : null, Id(8), null, opinion,
        kind.StartsWith("adhoc-", StringComparison.Ordinal) ? Id(5) : null,
        kind.StartsWith("adhoc-", StringComparison.Ordinal) ? false : null,
        kind.StartsWith("adhoc-", StringComparison.Ordinal) ? true : null);

    private static AllomorphReferenceFootprint Footprint(IReadOnlyList<AllomorphReference> references)
    {
        var digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        return new AllomorphReferenceFootprint(digest, digest, [], references, [], [], [], [],
            new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                new Dictionary<string, int>(StringComparer.Ordinal)), digest);
    }

    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");
}
