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

    // ---- P-env-literal-host

    private static long _node;

    // The compiler writes each environment segment as a literalSegments node with one phoneme node below it.
    private static ContextSideFact EnvSide(string guid, string side, params string[] tokens) =>
        Side("environment", guid, side, 0, tokens);

    private static ContextSideFact Rule(string guid, string side, int ordinal, params string[] tokens) =>
        Side("phonologicalRule", guid, side, ordinal, tokens);

    private static ContextSideFact Side(string ownerKind, string guid, string side, int ordinal, string[] tokens)
    {
        var nodes = new List<ContextNodeFact>();
        foreach (var token in tokens)
            switch (token)
            {
                case "#L": nodes.Add(new ContextNodeFact(++_node, null, nodes.Count, "leftAnchor", null)); break;
                case "#R": nodes.Add(new ContextNodeFact(++_node, null, nodes.Count, "rightAnchor", null)); break;
                case "[C]": nodes.Add(new ContextNodeFact(++_node, null, nodes.Count, "naturalClass", null)); break;
                case "(V)*": nodes.Add(new ContextNodeFact(++_node, null, nodes.Count, "iteration", null)); break;
                case "+": nodes.Add(new ContextNodeFact(++_node, null, nodes.Count, "boundary", "+")); break;
                default:
                    var parent = ++_node;
                    nodes.Add(new ContextNodeFact(parent, null, nodes.Count, "literalSegments", token));
                    nodes.Add(new ContextNodeFact(++_node, parent, 0, "phoneme", null));
                    break;
            }
        return new ContextSideFact(ownerKind, guid, side, ordinal, nodes);
    }

    private static ParsimonyMeasureResult LiteralHost(IReadOnlyList<ContextSideFact> sides,
        bool rulesRead = true, IReadOnlyList<EnvironmentUseFact>? uses = null,
        IReadOnlyDictionary<string, string>? owners = null, IReadOnlyDictionary<string, string>? names = null) =>
        MeasureRunner.DecideLiteralHost(Measure("P-env-literal-host"),
            new LiteralHostInput(sides, owners ?? new Dictionary<string, string>(), uses ?? [], rulesRead),
            Context(names));

    [Fact]
    public void ThreeLiteralSegmentsAreFlaggedAndTwoWithNoBoundaryAreNot()
    {
        var result = LiteralHost([EnvSide("e1", "left", "p", "i", "m"), EnvSide("e2", "left", "p", "i"),
            EnvSide("e3", "left", "p")]);

        Assert.Equal(3, result.Run.EligibleItems);
        var finding = Assert.Single(result.Findings);
        Assert.Contains("e1", finding.AttachesTo.Identity);
        Assert.Equal(new ParsimonyMeasureNumber(1, 3, "context sides"), finding.Number);
        Assert.Equal(ParsimonyGroupKind.LiteralContext, finding.AttachesTo.GroupKind);
        Assert.True(ParsimonyFindingIdentity.GroupIdentityIsConsistent(finding));
    }

    [Fact]
    public void TwoSegmentsNextToAWordAnchorAreFlaggedWhetherTheAnchorIsOnTheSideOrAcrossTheTarget()
    {
        var anchored = LiteralHost([EnvSide("e1", "left", "#L", "p", "i"),
            EnvSide("e2", "right", "p", "i", "#R")]);
        var acrossTarget = LiteralHost([EnvSide("e3", "left", "p", "i"), EnvSide("e3", "right", "-")]);

        Assert.Equal(2, anchored.Findings.Count);
        var finding = Assert.Single(acrossTarget.Findings);
        Assert.Contains("/left/", finding.AttachesTo.Identity);
    }

    [Fact]
    public void TwoSegmentsBesideAClassAreNotFlaggedButBesideAMorphemeBoundaryAre()
    {
        Assert.Empty(LiteralHost([EnvSide("e1", "left", "p", "i"), EnvSide("e1", "right", "[C]")]).Findings);
        Assert.Single(LiteralHost([Rule("r1", "right", 0, "p", "i", "+")]).Findings);
    }

    [Fact]
    public void AClassVariableOrRepetitionKeepsALongSideFromBeingLiteral()
    {
        var result = LiteralHost([EnvSide("e1", "left", "p", "i", "m", "[C]"),
            Rule("r1", "left", 0, "p", "i", "m", "(V)*")]);

        Assert.Empty(result.Findings);
        Assert.Equal(2, result.Run.EligibleItems);
    }

    [Fact]
    public void ALoneHyphenIsABoundaryAndIsNeverCountedAsASegment()
    {
        var result = LiteralHost([EnvSide("e1", "left", "-"), EnvSide("e2", "left", "-", "p"),
            EnvSide("e3", "left", "p", "-", "i")]);

        var finding = Assert.Single(result.Findings);
        Assert.Contains("e3", finding.AttachesTo.Identity);
        Assert.Contains("2 literal segments", finding.Limitations[0]);
    }

    [Fact]
    public void ASideWithoutSegmentNodesIsReadFromItsTokens()
    {
        var side = new ContextSideFact("environment", "e1", "left", 0,
            [new ContextNodeFact(1, null, 0, "literalSegments", "p i m")]);

        Assert.Single(LiteralHost([side]).Findings);
    }

    [Fact]
    public void AnEnvironmentFindingNamesItsUsersAndAnInfixPositionSaysSo()
    {
        var result = LiteralHost([EnvSide("e1", "left", "#L", "p", "i", "m")],
            uses: [new EnvironmentUseFact("e1", "a1", "phone"), new EnvironmentUseFact("e1", "a2", "position")],
            owners: new Dictionary<string, string> { ["e1"] = "/ # p i m _" },
            names: new Dictionary<string, string> { ["a1"] = "pim-", ["a2"] = "xa" });

        var finding = Assert.Single(result.Findings);
        Assert.Contains("Environment \"/ # p i m _\": its left side is 3 literal segments (# p i m)",
            finding.Limitations[0]);
        Assert.Contains("Environment \"/ # p i m _\" is used by pim-, xa (infix position).", finding.Limitations);
        Assert.Contains(finding.EvidenceRefs, reference => reference.View == ParsimonyViews.StatementUsage);
    }

    [Fact]
    public void ARuleContextFindingNamesTheRuleAndItsRightHandSide()
    {
        var result = LiteralHost([Rule("r1", "right", 1, "p", "i", "m")],
            owners: new Dictionary<string, string> { ["r1"] = "Lengthen before pim" });

        var finding = Assert.Single(result.Findings);
        Assert.Contains("Rule \"Lengthen before pim\", right-hand side 2: its right context is 3 literal segments",
            finding.Limitations[0]);
        Assert.Contains("literal-context/rule/r1/right/1", finding.AttachesTo.Identity);
        Assert.Empty(finding.EvidenceRefs);
    }

    [Fact]
    public void UnreadableRuleContextsLeaveTheRunInconclusiveButKeepTheEnvironmentFindings()
    {
        var result = LiteralHost([EnvSide("e1", "left", "p", "i", "m")], rulesRead: false);

        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, result.Run.Status);
        Assert.NotNull(result.NoteReason);
        Assert.Single(result.Findings);
    }

    [Fact]
    public void NoSidesIsAComputedZero()
    {
        var result = LiteralHost([]);

        Assert.Equal(ParsimonyMeasureStatus.Computed, result.Run.Status);
        Assert.Equal(0, result.Run.EligibleItems);
    }
}
