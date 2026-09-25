using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class AnalysisChangeSlotTests
{
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
