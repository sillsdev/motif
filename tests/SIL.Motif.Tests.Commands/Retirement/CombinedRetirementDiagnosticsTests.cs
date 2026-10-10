using SIL.Motif.Commands.Retirement;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Projection.Retirement;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class CombinedRetirementDiagnosticsTests
{
    [Fact]
    public void EachBundleCoveredByItsOwnRetirementLeavesNothingUnresolved()
    {
        var footprint = Footprint(Bundle(form: 10, bundle: 20), Bundle(form: 11, bundle: 21));

        var combined = RetirementReviewProjectionAssembler.CombineDiagnostics(footprint,
            Intent(Retirement(form: 10, bundle: 20), Retirement(form: 11, bundle: 21)));

        Assert.Empty(combined.UnresolvedReferences);
        Assert.Equal(0, combined.UnresolvedApprovedAnalyses);
    }

    [Fact]
    public void ABundleNoRetirementCoversStaysUnresolved()
    {
        var uncovered = Bundle(form: 11, bundle: 21);
        var footprint = Footprint(Bundle(form: 10, bundle: 20), uncovered);

        var combined = RetirementReviewProjectionAssembler.CombineDiagnostics(footprint,
            Intent(Retirement(form: 10, bundle: 20), Retirement(form: 11, bundle: 99)));

        Assert.Equal([uncovered], combined.UnresolvedReferences);
        Assert.Equal(1, combined.UnresolvedApprovedAnalyses);
    }

    private static AllomorphReference Bundle(int form, int bundle) => new(Id(form), Id(bundle), "WfiMorphBundle",
        "WfiMorphBundle", "Morph", null, "bundle-morph", "rel", "atomic", false, Id(bundle), Id(30 + bundle),
        Id(60 + bundle), Id(8), null, "approved", null, null, null);

    private static AllomorphReferenceFootprint Footprint(params AllomorphReference[] references)
    {
        var digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        return new AllomorphReferenceFootprint(digest, digest, [], references, [], [], [], [],
            new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                new Dictionary<string, int>(StringComparer.Ordinal)), digest);
    }

    private static RetireAllomorphIntent Retirement(int form, int bundle)
    {
        var replacement = Identity(form + 100);
        return new RetireAllomorphIntent(Text(1), AllomorphRetirementScope.Affix, [Identity(form)],
            [new(Text(form), Text(8), null, "whole", replacement)],
            [new(Text(bundle), Text(30 + bundle), Text(60 + bundle), Text(form), Text(8), null, "whole")], []);
    }

    private static ReplaceListedAllomorphsWithRuleIntent Intent(params RetireAllomorphIntent[] retirements) =>
        new(new("create", Text(2), "V", "V", [], null),
            new(Text(3), "rule", [], [], [], [], new("first", null), true), retirements, []);

    private static AllomorphIdentity Identity(int value) =>
        new(Text(value), Text(1), "MoAffixAllomorph", "location", "position", "sha256:" + new string('b', 64), null);

    private static string Text(int value) => CanonicalId.FromGuid(Id(value)).Value;

    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");
}
