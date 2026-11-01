using SIL.Motif.Commands;
using Xunit;

namespace SIL.Motif.Tests.Commands;

// A Difference joins a Trial's words to a Text's words on the key an Assessment comparison uses.
public sealed partial class ProposalDifferenceCommandsTests
{
    [Fact]
    public void ATrialSpellingAndATextSpellingOfOneWordJoinOnTheKeyAComparisonUses()
    {
        const string precomposed = "sit\u00E1";
        const string decomposed = "sita\u0301";
        using var fixture = new Fixture(_root, sitaTrial: decomposed, sitaText: precomposed);

        var made = ProposalDifferenceCommands.Produce(fixture.ProduceRequest());

        Assert.True(made.Succeeded, made.Refusal?.Message);
        Assert.Equal(0, made.Value!.Summary.UnboundOccurrences);
        var word = Assert.Single(made.Value.Words, w => w.Word.Normalize(System.Text.NormalizationForm.FormD) == decomposed);
        Assert.Equal(2, word.Occurrences);
        var key = SIL.Motif.Host.Assess.AssessmentComparer.JoinKey(
            new SIL.Motif.Host.Parser.AssessedWord(precomposed, "analysed", []));
        Assert.Equal(key, word.Word);
    }
}
