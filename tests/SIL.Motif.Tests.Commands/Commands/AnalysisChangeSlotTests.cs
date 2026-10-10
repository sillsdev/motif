using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class AnalysisChangeSlotTests
{
    [Theory]
    [InlineData(LexicalSenseOperationKinds.SetGloss)]
    [InlineData(LexicalSenseOperationKinds.ClearGloss)]
    public void TwoGlossOperationsOnOneSenseAndWritingSystemAreRefused(string laterKind)
    {
        var sense = CanonicalId.Mint();
        var first = new OperationEnvelope(CanonicalId.Mint(), LexicalSenseOperationKinds.SetGloss,
            target: sense, after: JsonSerializer.SerializeToElement(new { ws = "en", text = "first" }));
        var later = new OperationEnvelope(CanonicalId.Mint(), laterKind, target: sense,
            after: laterKind == LexicalSenseOperationKinds.SetGloss
                ? JsonSerializer.SerializeToElement(new { ws = "en", text = "second" })
                : JsonSerializer.SerializeToElement(new { ws = "en" }));

        Assert.Throws<ContractParseException>(() => ProposalOperationSlotValidator.Validate(
            new Proposal(new Dictionary<string, string> { ["lexical"] = "1.0" },
                CanonicalId.Mint(), null, [first, later])));
    }

    [Fact]
    public void GlossOperationsForDifferentWritingSystemsHaveDistinctSlots()
    {
        var sense = CanonicalId.Mint();
        var operations = new[]
        {
            new OperationEnvelope(CanonicalId.Mint(), LexicalSenseOperationKinds.SetGloss,
                target: sense, after: JsonSerializer.SerializeToElement(new { ws = "en", text = "first" })),
            new OperationEnvelope(CanonicalId.Mint(), LexicalSenseOperationKinds.ClearGloss,
                target: sense, after: JsonSerializer.SerializeToElement(new { ws = "fr" })),
        };

        ProposalOperationSlotValidator.Validate(new Proposal(
            new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint(), null, operations));
    }

    [Fact]
    public void TwoSpellingOperationsOnOneWordformAreRefused()
    {
        var wordform = CanonicalId.Mint();
        var operations = new[]
        {
            new OperationEnvelope(CanonicalId.Mint(), WfiWordformSpellingStatusOperationKinds.SetSpellingStatus,
                target: wordform, after: JsonSerializer.SerializeToElement(new { value = 2 })),
            new OperationEnvelope(CanonicalId.Mint(), WfiWordformSpellingStatusOperationKinds.SetSpellingStatus,
                target: wordform, after: JsonSerializer.SerializeToElement(new { value = 2 })),
        };

        Assert.Throws<ContractParseException>(() => ProposalOperationSlotValidator.Validate(
            new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0" },
                CanonicalId.Mint(), null, operations)));
    }

    [Fact]
    public void CreateAndOpinionOperationsUseTheirOwnSlots()
    {
        var wordform = CanonicalId.Mint();
        var firstAnalysis = CanonicalId.Mint();
        var secondAnalysis = CanonicalId.Mint();
        var operations = new[]
        {
            new OperationEnvelope(CanonicalId.Mint(), WfiAnalysisOperationKinds.CreateAnalysis,
                entityId: firstAnalysis, target: wordform),
            new OperationEnvelope(CanonicalId.Mint(), WfiAnalysisOperationKinds.CreateAnalysis,
                entityId: secondAnalysis, target: wordform),
            new OperationEnvelope(CanonicalId.Mint(), WfiAnalysisOperationKinds.AddRefEvaluations,
                target: firstAnalysis),
            new OperationEnvelope(CanonicalId.Mint(), WfiWordformSpellingStatusOperationKinds.SetSpellingStatus,
                target: wordform),
        };

        ProposalOperationSlotValidator.Validate(new Proposal(
            new Dictionary<string, string> { ["analysis"] = "1.0" }, CanonicalId.Mint(), null, operations));
    }

    [Fact]
    public void TwoNotebookTitleWritesToOneSlotConflict()
    {
        var record = CanonicalId.Mint();
        var operations = new[]
        {
            Envelope(RnGenericRecTitleOperationKinds.Set, record, new { ws = "en", text = "first" }),
            Envelope(RnGenericRecTitleOperationKinds.Set, record, new { ws = "en", text = "second" }),
        };

        Assert.NotNull(ProposalOperationSlotValidator.FindConflict(operations));
    }

    [Fact]
    public void NotebookTitleWritesInDifferentWritingSystemsDoNotConflict()
    {
        var record = CanonicalId.Mint();
        var operations = new[]
        {
            Envelope(RnGenericRecTitleOperationKinds.Set, record, new { ws = "en", text = "English" }),
            Envelope(RnGenericRecTitleOperationKinds.Set, record, new { ws = "fr", text = "Francais" }),
        };

        Assert.Null(ProposalOperationSlotValidator.FindConflict(operations));
    }

    [Fact]
    public void NotebookTypeSetAndClearConflict()
    {
        var record = CanonicalId.Mint();
        var operations = new[]
        {
            Envelope(RnGenericRecTypeOperationKinds.SetType, record, new { @ref = CanonicalId.Mint().Value }),
            Envelope(RnGenericRecTypeOperationKinds.ClearType, record, new { }),
        };

        Assert.NotNull(ProposalOperationSlotValidator.FindConflict(operations));
    }

    [Fact]
    public void ParagraphContentsWritesToOneParagraphConflict()
    {
        var paragraph = CanonicalId.Mint();
        var operations = new[]
        {
            Envelope(StTxtParaContentsOperationKinds.Set, paragraph, new { ws = "en", text = "first" }),
            Envelope(StTxtParaContentsOperationKinds.Clear, paragraph, new { ws = "en" }),
        };

        Assert.NotNull(ProposalOperationSlotValidator.FindConflict(operations));
    }

    [Fact]
    public void JudgmentFieldWritesToOneRecordConflict()
    {
        var record = CanonicalId.Mint();
        var operations = new[]
        {
            Envelope(HumanJudgmentCustomFieldOperationKinds.Set, record, new { text = "first" }),
            Envelope(HumanJudgmentCustomFieldOperationKinds.Set, record, new { text = "second" }),
        };

        Assert.NotNull(ProposalOperationSlotValidator.FindConflict(operations));
    }

    [Fact]
    public void ProposalWithTwoNotebookTitleWritesIsRefusedAtFinalize()
    {
        var record = CanonicalId.Mint().Value;
        var proposalJson = $$"""
            {
              "contractVersions": { "system": "1.0" },
              "proposalId": "{{CanonicalId.Mint().Value}}",
              "requires": [],
              "operations": [
                {
                  "operationId": "{{CanonicalId.Mint().Value}}",
                  "kind": "system/rnGenericRec/setTitle",
                  "target": "{{record}}",
                  "after": { "ws": "en", "text": "first" },
                  "extensions": {}
                },
                {
                  "operationId": "{{CanonicalId.Mint().Value}}",
                  "kind": "system/rnGenericRec/setTitle",
                  "target": "{{record}}",
                  "after": { "ws": "en", "text": "second" },
                  "extensions": {}
                }
              ],
              "extensions": {}
            }
            """;

        var proposal = ProposalJsonParser.Parse(proposalJson);

        Assert.Throws<ContractParseException>(() => ProposalOperationSlotValidator.Validate(proposal));
    }

    private static OperationEnvelope Envelope(string kind, CanonicalId target, object after) =>
        new(CanonicalId.Mint(), kind, target: target, after: JsonSerializer.SerializeToElement(after));

    [Theory]
    [InlineData(MoInflAffixTemplatePrefixSlotsOperationKinds.AddRefPrefixSlots,
        MoInflAffixTemplatePrefixSlotsOperationKinds.RemoveRefPrefixSlots)]
    [InlineData(MoInflAffixTemplatePrefixSlotsOperationKinds.AddRefPrefixSlots,
        MoInflAffixTemplatePrefixSlotsOperationKinds.MovePrefixSlots)]
    [InlineData(MoInflAffixTemplatePrefixSlotsOperationKinds.RemoveRefPrefixSlots,
        MoInflAffixTemplatePrefixSlotsOperationKinds.MovePrefixSlots)]
    [InlineData(MoInflAffixTemplateSuffixSlotsOperationKinds.AddRefSuffixSlots,
        MoInflAffixTemplateSuffixSlotsOperationKinds.MoveSuffixSlots)]
    public void OrderedMemberSlotCannotBeAddressedByTwoOperations(string firstKind, string laterKind)
    {
        var template = CanonicalId.Mint();
        var member = CanonicalId.Mint();
        var operations = new[]
        {
            OrderedOperation(firstKind, template, member),
            OrderedOperation(laterKind, template, member),
        };

        Assert.Throws<ContractParseException>(() => ProposalOperationSlotValidator.Validate(new Proposal(
            new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null, operations)));
    }

    [Fact]
    public void OrderedMemberSlotsAreDistinctByOwnerAndMember()
    {
        var member = CanonicalId.Mint();
        var owner = CanonicalId.Mint();
        var operations = new[]
        {
            OrderedOperation(MoInflAffixTemplatePrefixSlotsOperationKinds.AddRefPrefixSlots,
                owner, member),
            OrderedOperation(MoInflAffixTemplatePrefixSlotsOperationKinds.RemoveRefPrefixSlots,
                CanonicalId.Mint(), member),
            OrderedOperation(MoInflAffixTemplatePrefixSlotsOperationKinds.AddRefPrefixSlots,
                owner, CanonicalId.Mint()),
        };

        ProposalOperationSlotValidator.Validate(new Proposal(
            new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null, operations));
    }

    [Fact]
    public void RewriteRuleCreateSlotsUseMemberIdentity()
    {
        var phonology = CanonicalId.Mint();
        var firstRule = CanonicalId.Mint();
        var secondRule = CanonicalId.Mint();
        var firstCreate = new OperationEnvelope(CanonicalId.Mint(),
            PhPhonDataPhonRulesOperationKinds.Create, entityId: firstRule, target: phonology,
            after: JsonSerializer.SerializeToElement(new { @class = "PhRegularRule" }));
        var duplicateCreate = new OperationEnvelope(CanonicalId.Mint(),
            PhPhonDataPhonRulesOperationKinds.Create, entityId: firstRule, target: phonology,
            after: JsonSerializer.SerializeToElement(new { @class = "PhRegularRule" }));

        Assert.Throws<ContractParseException>(() => ProposalOperationSlotValidator.Validate(new Proposal(
            new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
            [firstCreate, duplicateCreate])));

        ProposalOperationSlotValidator.Validate(new Proposal(
            new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
            [firstCreate, new OperationEnvelope(CanonicalId.Mint(),
                PhPhonDataPhonRulesOperationKinds.Create, entityId: secondRule, target: phonology,
                after: JsonSerializer.SerializeToElement(new { @class = "PhRegularRule" }))]));

        var firstRhs = new OperationEnvelope(CanonicalId.Mint(),
            PhRegularRuleRightHandSidesOperationKinds.Create, entityId: CanonicalId.Mint(), target: firstRule,
            after: JsonSerializer.SerializeToElement(new { }));
        var duplicateRhs = new OperationEnvelope(CanonicalId.Mint(),
            PhRegularRuleRightHandSidesOperationKinds.Create, entityId: firstRhs.EntityId, target: firstRule,
            after: JsonSerializer.SerializeToElement(new { }));
        Assert.Throws<ContractParseException>(() => ProposalOperationSlotValidator.Validate(new Proposal(
            new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
            [firstRhs, duplicateRhs])));

        ProposalOperationSlotValidator.Validate(new Proposal(
            new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
            [firstRhs, new OperationEnvelope(CanonicalId.Mint(),
                PhRegularRuleRightHandSidesOperationKinds.Create, entityId: CanonicalId.Mint(), target: firstRule,
                after: JsonSerializer.SerializeToElement(new { }))]));
    }

    [Fact]
    public void RewriteRuleFieldSlotsUseWritingSystemOrFieldIdentity()
    {
        var rule = CanonicalId.Mint();
        var sameNameWritingSystem = new[]
        {
            new OperationEnvelope(CanonicalId.Mint(), PhSegmentRuleNameOperationKinds.SetName,
                target: rule, after: JsonSerializer.SerializeToElement(new { ws = "en", text = "first" })),
            new OperationEnvelope(CanonicalId.Mint(), PhSegmentRuleNameOperationKinds.ClearName,
                target: rule, after: JsonSerializer.SerializeToElement(new { ws = "en" })),
        };
        Assert.Throws<ContractParseException>(() => ProposalOperationSlotValidator.Validate(new Proposal(
            new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
            sameNameWritingSystem)));

        var distinctFields = new[]
        {
            sameNameWritingSystem[0],
            new OperationEnvelope(CanonicalId.Mint(), PhSegmentRuleNameOperationKinds.SetName,
                target: rule, after: JsonSerializer.SerializeToElement(new { ws = "fr", text = "second" })),
            new OperationEnvelope(CanonicalId.Mint(), PhSegmentRuleDirectionOperationKinds.SetDirection,
                target: rule, after: JsonSerializer.SerializeToElement(new { value = 1 })),
            new OperationEnvelope(CanonicalId.Mint(), PhSegmentRuleDisabledOperationKinds.SetDisabled,
                target: rule, after: JsonSerializer.SerializeToElement(new { value = true })),
        };
        ProposalOperationSlotValidator.Validate(new Proposal(
            new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null, distinctFields));

        var repeatedDirection = new[]
        {
            distinctFields[2],
            new OperationEnvelope(CanonicalId.Mint(), PhSegmentRuleDirectionOperationKinds.ClearDirection,
                target: rule, after: JsonSerializer.SerializeToElement(new { })),
        };
        Assert.Throws<ContractParseException>(() => ProposalOperationSlotValidator.Validate(new Proposal(
            new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
            repeatedDirection)));

        var repeatedDisabled = new[]
        {
            distinctFields[3],
            new OperationEnvelope(CanonicalId.Mint(), PhSegmentRuleDisabledOperationKinds.ClearDisabled,
                target: rule, after: JsonSerializer.SerializeToElement(new { })),
        };
        Assert.Throws<ContractParseException>(() => ProposalOperationSlotValidator.Validate(new Proposal(
            new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
            repeatedDisabled)));
    }

    [Fact]
    public void RegularRuleDirectionPayloadIsClosedToDefinedValues()
    {
        foreach (var value in new[] { 0, 1, 2 })
        {
            Assert.Equal(value, PhSegmentRuleDirectionSetPayload.Parse(
                JsonSerializer.SerializeToElement(new { value })));
        }

        Assert.Throws<ContractParseException>(() => PhSegmentRuleDirectionSetPayload.Parse(
            JsonSerializer.SerializeToElement(new { value = 3 })));
        Assert.Throws<ContractParseException>(() => PhSegmentRuleDirectionSetPayload.Parse(
            JsonSerializer.SerializeToElement(new { value = 1, extra = true })));
    }

    private static OperationEnvelope OrderedOperation(string kind, CanonicalId target, CanonicalId member)
    {
        var isMove = kind is MoInflAffixTemplatePrefixSlotsOperationKinds.MovePrefixSlots or
            MoInflAffixTemplateSuffixSlotsOperationKinds.MoveSuffixSlots;
        return new OperationEnvelope(CanonicalId.Mint(), kind, target: target,
            after: JsonSerializer.SerializeToElement(new { member = member.Value }),
            placement: isMove ? new Placement(CanonicalId.Mint(), null) : null);
    }
}
