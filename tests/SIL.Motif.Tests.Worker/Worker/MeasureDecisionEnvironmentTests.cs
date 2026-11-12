using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>The environment measures decide over plain input records: no session, no SQLite file, no parser.</summary>
public sealed class MeasureDecisionEnvironmentTests
{
    private static MeasureDefinition Measure(string id) => MeasureCatalog.Find(id)!;

    private static MeasureContext Context(IReadOnlyDictionary<string, string>? names = null) =>
        MeasureContext.For("bundle-under-test",
            new MeasureDigestContext(1, 1, ParsimonyEvidenceScopeKind.ProjectApproved, null, null), names);

    // ---- P-env-duplicate

    private static EnvironmentSideFact Environment(string guid, string left, string right, string status = "valid") =>
        new(guid, "", "/ " + guid, status, left, right);

    private static ParsimonyMeasureResult Duplicates(IReadOnlyList<EnvironmentSideFact> environments,
        params EnvironmentUseFact[] uses) => MeasureRunner.DecideDuplicateEnvironments(Measure("P-env-duplicate"),
            new EnvironmentDuplicateInput(environments, uses), Context());

    [Fact]
    public void TwoEnvironmentsWithEqualKeysOnBothSidesFormOneGroup()
    {
        var result = Duplicates([Environment("a", "k1", "k2"), Environment("b", "k1", "k2"),
            Environment("c", "k1", "k3")]);

        Assert.Equal(3, result.Run.EligibleItems);
        Assert.Equal(1, result.Run.FindingItems);
        Assert.Equal(new ParsimonyMeasureNumber(1, 3, "environments"), Assert.Single(result.Findings).Number);
    }

    [Fact]
    public void ThreeCopiesAreOneGroupWithTwoRedundantEnvironments()
    {
        var result = Duplicates([Environment("a", "k1", "k2"), Environment("b", "k1", "k2"),
            Environment("c", "k1", "k2")]);

        Assert.Equal(2, result.Run.FindingItems);
        Assert.Single(result.Findings);
    }

    [Fact]
    public void EqualKeysOnOneSideOnlyAreNotDuplicates()
    {
        Assert.Empty(Duplicates([Environment("a", "k1", "k2"), Environment("b", "k1", "k9"),
            Environment("c", "k8", "k2")]).Findings);
    }

    [Fact]
    public void ASwappedPairIsNotADuplicate()
    {
        Assert.Empty(Duplicates([Environment("a", "k1", "k2"), Environment("b", "k2", "k1")]).Findings);
    }

    [Fact]
    public void EnvironmentsThatDidNotParseAreLeftOutAndCountedInTheLimitation()
    {
        var result = Duplicates([Environment("a", "k1", "k2"), Environment("b", "k1", "k2"),
            Environment("c", "k1", "k2", "invalid"), Environment("d", "k1", "k2", "not_attempted")]);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(1, result.Run.FindingItems);
        Assert.Contains("2 environment(s) that did not parse as valid were left out of the comparison.",
            finding.Limitations);
        Assert.DoesNotContain(finding.Limitations, text => text.Contains("/ c"));
    }

    [Fact]
    public void AValidEnvironmentWithoutSideFactsIsLeftOut()
    {
        Assert.Empty(Duplicates([new EnvironmentSideFact("a", "", "/ a", "valid", null, null),
            new EnvironmentSideFact("b", "", "/ b", "valid", null, null)]).Findings);
    }

    [Fact]
    public void EachCopyNamesItsUsersAndAnUnusedCopySaysSo()
    {
        var result = MeasureRunner.DecideDuplicateEnvironments(Measure("P-env-duplicate"),
            new EnvironmentDuplicateInput([Environment("a", "k1", "k2"), Environment("b", "k1", "k2")],
                [new EnvironmentUseFact("a", "allo-1", "phone"), new EnvironmentUseFact("a", "allo-2", "position")]),
            Context(new Dictionary<string, string> { ["allo-1"] = "pim-", ["allo-2"] = "xa" }));

        var finding = Assert.Single(result.Findings);
        Assert.Contains("Environment \"/ a\" is used by pim-, xa (infix position).", finding.Limitations);
        Assert.Contains("Environment \"/ b\" is not used by any allomorph.", finding.Limitations);
        Assert.Contains(finding.EvidenceRefs, reference => reference.View == ParsimonyViews.AllomorphContext);
    }

    [Fact]
    public void TheFindingIdentityNamesTheMembersNotTheirText()
    {
        var first = Assert.Single(Duplicates([Environment("a", "k1", "k2"), Environment("b", "k1", "k2")]).Findings);
        var second = Assert.Single(Duplicates([Environment("b", "k5", "k6"), Environment("a", "k5", "k6")]).Findings);

        Assert.Equal(first.FindingId, second.FindingId);
        Assert.True(ParsimonyFindingIdentity.GroupIdentityIsConsistent(first));
    }

    [Fact]
    public void NoEnvironmentsIsAComputedZero()
    {
        var result = Duplicates([]);

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(0, result.Run.EligibleItems);
        Assert.Empty(result.Findings);
    }
}
