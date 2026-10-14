using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class OperationSlotCoverageTests
{
    [Theory]
    [InlineData("grammar/phEnvironment/setName", "{\"ws\":\"en\",\"text\":\"name\"}")]
    [InlineData("lexical/lexSense/setGloss", "{\"ws\":\"en\",\"text\":\"gloss\"}")]
    [InlineData("grammar/moForm/setIsAbstract", "{\"value\":true}")]
    [InlineData("grammar/phSegmentRule/setDirection", "{\"value\":1}")]
    [InlineData("grammar/phSimpleContextNC/setFeatureStructure", "{\"ref\":\"MEMBER\"}")]
    [InlineData("grammar/phNCSegments/addRefSegments", "{\"member\":\"MEMBER\"}")]
    [InlineData("grammar/phSequenceContext/moveMembers", "{\"member\":\"MEMBER\"}")]
    [InlineData("grammar/phPhonData/createEnvironments", "{}")]
    [InlineData("lexical/lexEntry/createLexemeForm", "{\"morphType\":\"MEMBER\",\"ws\":\"en\",\"text\":\"form\"}")]
    [InlineData("analysis/stTxtPara/setContents", "{\"ws\":\"en\",\"text\":\"paragraph\"}")]
    [InlineData("grammar/phEnvironment/setStringRepresentation", "{\"ws\":\"en\",\"text\":\"/ a _\",\"left\":[],\"right\":[]}")]
    [InlineData("lexical/lexEntry/deleteRedundantZeroAffixGraph", "{\"graphDigest\":\"sha256:0000000000000000000000000000000000000000000000000000000000000000\",\"members\":[{\"id\":\"MEMBER\",\"className\":\"LexEntry\",\"owner\":null}],\"optionalSlots\":[]}")]
    public void DuplicateFromEveryEmitterFamilyIsRefused(string kind, string payload)
    {
        var target = CanonicalId.Mint();
        var member = CanonicalId.Mint();
        var after = JsonDocument.Parse(payload.Replace("MEMBER", member.Value)).RootElement.Clone();
        if (kind == RetireRedundantZeroAffixOperationKinds.DeleteGraph)
            _ = RetireRedundantZeroAffixPayload.Parse(after);
        var operations = new[]
        {
            new OperationEnvelope(CanonicalId.Mint(), kind, target: target, entityId: member, after: after),
            new OperationEnvelope(CanonicalId.Mint(), kind, target: target, entityId: member, after: after),
        };

        Assert.NotNull(ProposalOperationSlotValidator.FindConflict(operations));
        Assert.Throws<ContractParseException>(() => ProposalOperationSlotValidator.Validate(Proposal(operations)));
    }

    [Theory]
    [InlineData("grammar/phEnvironment/setName", "grammar/phEnvironment/clearName", "{\"ws\":\"en\",\"text\":\"name\"}", "{\"ws\":\"en\"}")]
    [InlineData("grammar/moForm/setIsAbstract", "grammar/moForm/clearIsAbstract", "{\"value\":true}", "{}")]
    [InlineData("grammar/phSimpleContextNC/setFeatureStructure", "grammar/phSimpleContextNC/clearFeatureStructure", "{\"ref\":\"MEMBER\"}", "{}")]
    [InlineData("grammar/phNCSegments/addRefSegments", "grammar/phNCSegments/removeRefSegments", "{\"member\":\"MEMBER\"}", "{\"member\":\"MEMBER\"}")]
    public void OppositeVerbsShareOneSlot(string firstKind, string secondKind, string firstPayload, string secondPayload)
    {
        var target = CanonicalId.Mint();
        var member = CanonicalId.Mint().Value;
        var first = Envelope(firstKind, target, firstPayload.Replace("MEMBER", member));
        var second = Envelope(secondKind, target, secondPayload.Replace("MEMBER", member));

        Assert.NotNull(ProposalOperationSlotValidator.FindConflict([first, second]));
    }

    [Theory]
    [InlineData("grammar/phEnvironment/setName", "{\"ws\":\"en\",\"text\":\"name\"}", "{\"ws\":\"fr\",\"text\":\"nom\"}")]
    [InlineData("grammar/phNCSegments/addRefSegments", "{\"member\":\"FIRST\"}", "{\"member\":\"SECOND\"}")]
    public void IndependentAlternativesAndMembersAreAllowed(string kind, string firstPayload, string secondPayload)
    {
        var target = CanonicalId.Mint();
        var first = Envelope(kind, target, firstPayload.Replace("FIRST", CanonicalId.Mint().Value));
        var second = Envelope(kind, target, secondPayload.Replace("SECOND", CanonicalId.Mint().Value));

        Assert.Null(ProposalOperationSlotValidator.FindConflict([first, second]));
    }

    [Theory]
    [InlineData("system/rnGenericRec/setTitle", "system/rnGenericRec/clearTitle")]
    [InlineData("analysis/stTxtPara/setContents", "analysis/stTxtPara/clearContents")]
    [InlineData("grammar/phEnvironment/setStringRepresentation", "grammar/phEnvironment/clearStringRepresentation")]
    public void RichStringWritesConflictAcrossPayloadWritingSystems(string setKind, string clearKind)
    {
        var target = CanonicalId.Mint();
        var first = Envelope(setKind, target, "{\"ws\":\"en\",\"text\":\"title\"}");
        var second = Envelope(clearKind, target, "{\"ws\":\"fr\"}");

        Assert.NotNull(ProposalOperationSlotValidator.FindConflict([first, second]));
    }

    [Fact]
    public void EveryHandlerHasOneExplicitSlotPolicy()
    {
        OperationRegistryBootstrap.Initialize();
        Assert.NotEmpty(OperationHandlerRegistry.RegisteredKinds);
        Assert.Equal(OperationHandlerRegistry.RegisteredKinds.Order(), OperationSlotRegistry.RegisteredKinds.Order());
        Assert.All(OperationSlotRegistry.RegisteredKinds, kind => Assert.Null(OperationSlotRegistry.ExemptionReason(kind)));
    }

    [Fact]
    public void UnregisteredKindsCannotEscapeSlotValidation()
    {
        var operation = Envelope("grammar/unknown/setName", CanonicalId.Mint(), "{}");
        Assert.Throws<ContractParseException>(() => ProposalOperationSlotValidator.FindConflict([operation]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EntityDeletionConflictsWithWritesInEitherOrder(bool deleteFirst)
    {
        var target = CanonicalId.Mint();
        var deletion = Envelope("grammar/fsFeatStruc/deleteFeatureSpecs", target, "{}");
        var write = Envelope("grammar/fsFeatureSpecification/setFeature", target,
            "{\"ref\":\"" + CanonicalId.Mint().Value + "\"}");

        Assert.NotNull(ProposalOperationSlotValidator.FindConflict(deleteFirst ? [deletion, write] : [write, deletion]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundMemberCleanupAndGraphDeletionOnItsOwnerDoNotConflict(bool deleteFirst)
    {
        var root = CanonicalId.Mint();
        var member = CanonicalId.Mint();
        var deletion = GraphDeletion(root, member);
        var cleanup = Envelope("grammar/moInflAffMsa/removeRefSlots", member,
            "{\"member\":\"" + CanonicalId.Mint().Value + "\"}");

        Assert.Null(ProposalOperationSlotValidator.FindConflict(deleteFirst ? [deletion, cleanup] : [cleanup, deletion]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GraphDeletionConflictsWithOperationsOnItsDirectTarget(bool deleteFirst)
    {
        var root = CanonicalId.Mint();
        var deletion = GraphDeletion(root, CanonicalId.Mint());
        var write = Envelope("lexical/lexEntry/setCitationForm", root, "{\"ws\":\"en\",\"text\":\"root\"}");

        Assert.NotNull(ProposalOperationSlotValidator.FindConflict(deleteFirst ? [deletion, write] : [write, deletion]));
    }

    private static OperationEnvelope GraphDeletion(CanonicalId root, CanonicalId member)
    {
        var after = JsonSerializer.SerializeToElement(new
        {
            graphDigest = "sha256:" + new string('0', 64),
            members = new[]
            {
                new { id = root.Value, className = "LexEntry", owner = (string?)null },
                new { id = member.Value, className = "MoInflAffMsa", owner = (string?)root.Value },
            },
            optionalSlots = Array.Empty<string>(),
        });
        _ = RetireRedundantZeroAffixPayload.Parse(after);
        return new OperationEnvelope(CanonicalId.Mint(), RetireRedundantZeroAffixOperationKinds.DeleteGraph,
            target: root, after: after);
    }

    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) =>
        new(new Dictionary<string, string> { ["grammar"] = "1.0", ["lexical"] = "1.0", ["system"] = "1.0" },
            CanonicalId.Mint(), null, operations);

    private static OperationEnvelope Envelope(string kind, CanonicalId target, string payload) =>
        new(CanonicalId.Mint(), kind, target: target, after: JsonDocument.Parse(payload).RootElement.Clone());
}
