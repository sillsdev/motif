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

        Assert.Throws<ContractParseException>(() => AnalysisOpinionSlotValidator.Validate(
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

        AnalysisOpinionSlotValidator.Validate(new Proposal(
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

        Assert.Throws<ContractParseException>(() => AnalysisOpinionSlotValidator.Validate(
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

        AnalysisOpinionSlotValidator.Validate(new Proposal(
            new Dictionary<string, string> { ["analysis"] = "1.0" }, CanonicalId.Mint(), null, operations));
    }
}
