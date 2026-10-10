using System.Text.Json;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Composers;
using Xunit;

namespace SIL.Motif.Tests.Composers;

public sealed class RecordReviewedNegativeIntentParserTests
{
    [Fact]
    public void ParsesAContextualSurfaceNegativeWithoutAWordformOrGrammarOwner()
    {
        using var document = JsonDocument.Parse(SurfaceIntent());

        var intent = RecordReviewedNegativeIntentParser.Parse(document.RootElement);

        Assert.Equal("qaa", intent.WritingSystem);
        Assert.Equal("tará", intent.Form);
        Assert.Equal("standard dialect", intent.Context);
        Assert.IsType<SurfaceNegativeTarget>(intent.Target);
        Assert.Null(intent.WordformId);
        Assert.Null(intent.AnalysisId);
    }

    [Fact]
    public void ReadingIdentityPreservesOrderedFormMsaInflTypeTriples()
    {
        using var document = JsonDocument.Parse("""
            {
              "recordTypeId":"cmpossibility_0000000000000000000001",
              "caseId":"case_0000000000000000000002",
              "writingSystem":"qaa",
              "form":"tará",
              "context":"standard dialect",
              "target":{"kind":"reading","morphs":[
                {"identity":{"form":"moaffixallomorph_0000000000000000000003","msa":"moinflaffmsa_0000000000000000000004","inflType":"moinflclass_0000000000000000000005","guessedString":null},"guessedWritingSystem":null,"formCaption":"-ra","msaCaption":"plural"},
                {"identity":{"form":"mostemallomorph_0000000000000000000006","msa":"mostemmsa_0000000000000000000007","inflType":null,"guessedString":null},"guessedWritingSystem":null,"formCaption":"tá","msaCaption":"stem"}
              ]}
            }
            """);

        var intent = RecordReviewedNegativeIntentParser.Parse(document.RootElement);
        var target = Assert.IsType<ReadingNegativeTarget>(intent.Target);

        Assert.Equal(2, target.Morphs.Count);
        Assert.Equal("moaffixallomorph_0000000000000000000003", target.Morphs[0].Identity.Form);
        Assert.Equal("moinflaffmsa_0000000000000000000004", target.Morphs[0].Identity.Msa);
        Assert.Equal("moinflclass_0000000000000000000005", target.Morphs[0].Identity.InflType);
        Assert.Equal("mostemallomorph_0000000000000000000006", target.Morphs[1].Identity.Form);
        Assert.Equal("mostemmsa_0000000000000000000007", target.Morphs[1].Identity.Msa);
    }

    [Fact]
    public void RejectsMissingAuthoritativeIdsAndUnknownIntentFields()
    {
        using var missing = JsonDocument.Parse("""
            {
              "recordTypeId":"cmpossibility_0000000000000000000001",
              "caseId":"case_0000000000000000000002",
              "writingSystem":"qaa",
              "form":"tará",
              "context":"standard dialect",
              "target":{"kind":"reading","morphs":[
                {"identity":{"form":null,"msa":"moinflaffmsa_0000000000000000000004","inflType":null,"guessedString":null},"guessedWritingSystem":null,"formCaption":"-ra","msaCaption":"plural"}
              ]}
            }
            """);
        using var unknown = JsonDocument.Parse(SurfaceIntent().Replace("\"target\"", "\"unexpected\":true,\"target\"", StringComparison.Ordinal));

        Assert.Throws<ContractParseException>(() => RecordReviewedNegativeIntentParser.Parse(missing.RootElement));
        Assert.Throws<ContractParseException>(() => RecordReviewedNegativeIntentParser.Parse(unknown.RootElement));
    }

    private static string SurfaceIntent() => """
        {
          "recordTypeId":"cmpossibility_0000000000000000000001",
          "caseId":"case_0000000000000000000002",
          "writingSystem":"qaa",
          "form":"tará",
          "context":"standard dialect",
          "target":{"kind":"surface"}
        }
        """;
}
