using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Trait("MotifTestLevel", "Unit")]
public sealed class TraceRuleNamesTests
{
    private static readonly Guid Rule = Guid.Parse("12345678-1234-1234-abcd-123456789abc");

    private static WordTraceResponse FailedWord()
    {
        var failing = new TraceStep("MorphologicalRuleSynthesis", "mrule#2", null, null, "Pattern", [])
        {
            SourceIdentityKind = "morphRule",
            SourceIdentityId = Rule.ToString(),
            SourceIdentityQuality = "authored",
        };
        var root = new TraceStep("WordAnalysis", null, "kinaon", null, null, [failing]);
        return new WordTraceResponse("kinaon", false, true, null, 0, "mrule#2", 0,
            TraceReadingBuilder.Build("kinaon", root, [], []));
    }

    [Fact]
    public void WithoutAProjectNameTheReasonCarriesOnlyTheParsersId()
    {
        var reasons = FailedWord().Reading.NoParseReasons;

        Assert.Contains("mrule#2 does not satisfy the rule's pattern check.", reasons);
    }

    [Fact]
    public void AFailedWordNamesTheRuleAsFieldWorksShowsItWithTheParsersIdAfterIt()
    {
        var named = TraceRuleNames.Apply(FailedWord(), id => id == Rule ? "mu- ‘ACT.PUNC.FUT’" : null);

        Assert.Contains("mu- ‘ACT.PUNC.FUT’ (mrule#2) does not satisfy the rule's pattern check.", named.Reading.NoParseReasons);
        Assert.DoesNotContain("mrule#2 does not satisfy the rule's pattern check.", named.Reading.NoParseReasons);
    }

    [Fact]
    public void ARuleTheProjectCannotNameKeepsTheParsersId()
    {
        var named = TraceRuleNames.Apply(FailedWord(), _ => null);

        Assert.Contains("mrule#2 does not satisfy the rule's pattern check.", named.Reading.NoParseReasons);
    }
}
