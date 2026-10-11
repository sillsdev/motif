using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.LibLcm.Projection;

public sealed class ParsimonyFindingIdentityTests
{
    private const string Entry = "04490809-320d-47c7-9efa-4633fdbe95de";
    private const string First = "f125c7bd-edc3-4282-aa43-c1f48f026caa";
    private const string Second = "5b6f1c52-6a9c-4d0e-8f59-0c1c3a9d2e77";
    private const string Digest64 = "sha256:" + "1111111111111111111111111111111111111111111111111111111111111111";

    private static string Sha(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [Fact]
    public void EveryMintedFormIsByteIdenticalToTheFormatStoredToday()
    {
        var members = new[] { First, Second };
        Assert.Equal("entry/" + Entry, ParsimonyFindingIdentity.EntryGroupKey(Entry));
        Assert.Equal($"entry/{Entry}/duplicate-forms/stem/" + Sha(First + "\n" + Second),
            ParsimonyFindingIdentity.DuplicateFormsIdentity(Entry, "stem", members));
        Assert.Equal("P-x:" + Sha("some/identity"), ParsimonyFindingIdentity.GroupFindingId("P-x", "some/identity"));
        Assert.Equal("adhoc-signature/" + Sha("sig"), ParsimonyFindingIdentity.AdhocSignatureGroupKey("sig"));
        Assert.Equal("theme/adjacency-question/" + First, ParsimonyFindingIdentity.AdjacencyThemeKey("theme", First));
        Assert.Equal("adhoc-slot-order/theme", ParsimonyFindingIdentity.AdhocSlotOrderIdentity("theme"));
        Assert.Equal("B-x:adjacency:" + Sha("theme"),
            ParsimonyFindingIdentity.AdhocSlotOrderFindingId("B-x", true, "theme"));
        Assert.Equal("B-x:theme:" + Sha("theme"),
            ParsimonyFindingIdentity.AdhocSlotOrderFindingId("B-x", false, "theme"));
        Assert.Equal($"statement/environment/{First}", ParsimonyFindingIdentity.StatementIdentity("environment", First));
        Assert.Equal("statement/environment", ParsimonyFindingIdentity.StatementGroupKey("environment"));
        Assert.Equal("alternation-family/" + Sha("family"), ParsimonyFindingIdentity.AlternationFamilyIdentity("family"));
        Assert.Equal($"P-x:{First}:" + Sha("site"), ParsimonyFindingIdentity.SiteFindingId("P-x", First, "site"));
        Assert.Equal($"class/{First}/site/" + Sha("use"), ParsimonyFindingIdentity.ClassSiteGroupKey(First, "use"));
        Assert.Equal("wordform/" + First, ParsimonyFindingIdentity.WordformGroupKey(First));
        Assert.Equal("reviewed-negative/case-1", ParsimonyFindingIdentity.ReviewedNegativeGroupKey("case-1"));
    }

    [Theory]
    [InlineData(ParsimonyAuthoredObjectKind.AdhocProhibition, "MoAlloAdhocProhib")]
    [InlineData(ParsimonyAuthoredObjectKind.AdhocProhibition, "MoMorphAdhocProhib")]
    [InlineData(ParsimonyAuthoredObjectKind.AdhocProhibition, "MoAdhocProhibGr")]
    [InlineData(ParsimonyAuthoredObjectKind.InflectionalMsa, "MoInflAffMsa")]
    [InlineData(ParsimonyAuthoredObjectKind.AffixTemplate, "MoInflAffixTemplate")]
    [InlineData(ParsimonyAuthoredObjectKind.AffixSlot, "MoInflAffixSlot")]
    [InlineData(ParsimonyAuthoredObjectKind.Allomorph, "MoStemAllomorph")]
    [InlineData(ParsimonyAuthoredObjectKind.Allomorph, "MoAffixAllomorph")]
    [InlineData(ParsimonyAuthoredObjectKind.Allomorph, "MoAffixProcess")]
    [InlineData(ParsimonyAuthoredObjectKind.NaturalClass, "PhNCSegments")]
    [InlineData(ParsimonyAuthoredObjectKind.NaturalClass, "PhNCFeatures")]
    [InlineData(ParsimonyAuthoredObjectKind.Environment, "PhEnvironment")]
    public void AnAuthoredObjectRoundTripsThroughItsClassTable(ParsimonyAuthoredObjectKind kind, string className)
    {
        var id = CanonicalId.FromGuid(Guid.Parse(First)).Value;
        var finding = Finding("P-x", new ParsimonyFindingAttachment(ParsimonyAttachmentKind.AuthoredObject, First, kind));

        Assert.True(ParsimonyFindingIdentity.ClassMatches(kind, className));
        Assert.True(ParsimonyFindingIdentity.SubjectMatches(
            new ObjectJudgmentSubject(new JudgmentObject(className, id)), finding, id));
        foreach (var other in Enum.GetValues<ParsimonyAuthoredObjectKind>().Where(item => item != kind))
            Assert.False(ParsimonyFindingIdentity.ClassMatches(other, className));
    }

    [Theory]
    [InlineData("MoStemAllomorph", "MoAffixAllomorph")]
    [InlineData("MoAffixProcess", "MoAffixAllomorph")]
    public void ADuplicateFormGroupRoundTripsMintParseMatch(string firstClass, string secondClass)
    {
        var identity = ParsimonyFindingIdentity.DuplicateFormsIdentity(Entry, "stem", [First, Second]);
        var finding = Finding("P-allo-duplicate-form",
            new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, identity,
                GroupKind: ParsimonyGroupKind.AllomorphDuplicateForms),
            ParsimonyFindingIdentity.EntryGroupKey(Entry), "allomorph-context", First, Second);
        var subject = new GroupJudgmentSubject("P-allo-duplicate-form", JudgmentGroupRole.DuplicatePair,
        [
            new JudgmentObject(firstClass, CanonicalId.FromGuid(Guid.Parse(First)).Value),
            new JudgmentObject(secondClass, CanonicalId.FromGuid(Guid.Parse(Second)).Value),
        ]);

        Assert.True(ParsimonyFindingIdentity.GroupIdentityIsConsistent(finding));
        Assert.True(ParsimonyFindingIdentity.GroupKeyIsEntry(finding, Entry));
        Assert.True(ParsimonyFindingIdentity.SubjectMatches(subject, finding, "project"));
    }

    [Fact]
    public void AnAlternationFamilyGroupRoundTripsMintParseMatch()
    {
        var identity = ParsimonyFindingIdentity.AlternationFamilyIdentity("family");
        var finding = Finding("P-allo-alternation-family",
            new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, identity,
                GroupKind: ParsimonyGroupKind.AlternationFamily), identity, "allomorph-context", First, Second);
        var subject = new GroupJudgmentSubject("P-allo-alternation-family", JudgmentGroupRole.AlternationFamily,
        [
            new JudgmentObject("MoAffixProcess", CanonicalId.FromGuid(Guid.Parse(First)).Value),
            new JudgmentObject("MoStemAllomorph", CanonicalId.FromGuid(Guid.Parse(Second)).Value),
        ]);

        Assert.True(ParsimonyFindingIdentity.GroupIdentityIsConsistent(finding));
        Assert.True(ParsimonyFindingIdentity.SubjectMatches(subject, finding, "project"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASlotOrderGroupRoundTripsMintParseMatch(bool adjacency)
    {
        var theme = adjacency ? ParsimonyFindingIdentity.AdjacencyThemeKey("theme", First) : "theme";
        var finding = Finding("B-adhoc-is-slot-order",
            new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group,
                ParsimonyFindingIdentity.AdhocSlotOrderIdentity(theme),
                GroupKind: ParsimonyGroupKind.AdhocSlotOrder), theme, "adhoc-context", First) with
        {
            FindingId = ParsimonyFindingIdentity.AdhocSlotOrderFindingId("B-adhoc-is-slot-order", adjacency, theme),
        };
        var subject = new GroupJudgmentSubject("B-adhoc-is-slot-order", JudgmentGroupRole.AdhocSlotOrder,
            [new JudgmentObject("MoAlloAdhocProhib", CanonicalId.FromGuid(Guid.Parse(First)).Value)]);

        Assert.True(ParsimonyFindingIdentity.GroupIdentityIsConsistent(finding));
        Assert.Equal(adjacency, ParsimonyFindingIdentity.IsAdjacencyQuestion(finding));
        Assert.Equal(adjacency, ParsimonyFindingIdentity.AdjacencyNamesRule(finding, First));
        Assert.True(ParsimonyFindingIdentity.SubjectMatches(subject, finding, "project"));
    }

    [Theory]
    [InlineData("PhEnvironment")]
    [InlineData("PhNCSegments")]
    [InlineData("PhNCFeatures")]
    public void AnUnusedStatementRoundTripsMintParseMatch(string className)
    {
        var identity = ParsimonyFindingIdentity.StatementIdentity("environment", First);
        var finding = Finding("P-unused", new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, identity,
            GroupKind: ParsimonyGroupKind.UnusedStatement), ParsimonyFindingIdentity.StatementGroupKey("environment"));
        var id = CanonicalId.FromGuid(Guid.Parse(First)).Value;

        Assert.Equal(id, ParsimonyFindingIdentity.LastIdentity(identity));
        Assert.True(ParsimonyFindingIdentity.SubjectMatches(
            new ObjectJudgmentSubject(new JudgmentObject(className, id)), finding, "project"));
        Assert.False(ParsimonyFindingIdentity.SubjectMatches(
            new ObjectJudgmentSubject(new JudgmentObject("MoAffixProcess", id)), finding, "project"));
    }

    [Fact]
    public void ACaptionReadsTheGuidSegmentsWithTheirKindSegments()
    {
        var finding = Finding("P-x", new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group,
            ParsimonyFindingIdentity.StatementIdentity("environment", First),
            GroupKind: ParsimonyGroupKind.UnusedStatement), ParsimonyFindingIdentity.StatementGroupKey("environment"));

        Assert.Equal([("environment", First)], ParsimonyFindingIdentity.GuidSegments(finding));
    }

    [Fact]
    public void EvidenceDigestsMatchWithOrWithoutThePrefix() =>
        Assert.True(ParsimonyFindingIdentity.EvidenceDigestsMatch(Digest64, Digest64[7..]));

    private static ParsimonyFinding Finding(string measureId, ParsimonyFindingAttachment attachment,
        string? groupKey = null, string? view = null, params string[] objectGuids) => new(
        measureId + ":item", measureId, ParsimonyAxis.Parsimony, ParsimonyTier.Static, attachment, groupKey,
        new ParsimonyMeasureNumber(1, 1, "items"),
        new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.RankedOnly, null, "1"), Digest64,
        [.. objectGuids.Select(guid => new ParsimonyEvidenceReference("bundle/one", view!,
            JsonSerializer.SerializeToElement(new { objectGuid = guid })))],
        "P-test", [], ParsimonyVerification.NotRun);
}
