using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class ReviewNumbersTextRenderingTests
{
    [Theory]
    [InlineData(ReviewComparability.NoEarlierAssessment, 0, 0, 0,
        "1 of 2 touched words kept their approved analyses. There is no earlier Assessment to compare.")]
    [InlineData(ReviewComparability.DifferentAssessor, 0, 0, 0,
        "These Assessments were made by different Assessors and cannot be compared.")]
    [InlineData(ReviewComparability.NoSharedWords, 0, 0, 0,
        "The regression check could not compare these Assessments because they share no words.")]
    [InlineData(ReviewComparability.Compared, 1, 0, 1, "Among 1 shared word, approved kept: 0 → 1.")]
    [InlineData(ReviewComparability.Compared, 12, 9, 11, "Among 12 shared words, approved kept: 9 → 11.")]
    public void ReviewNumbersRenderAsTheCliSentence(ReviewComparability comparability, int shared, int before,
        int after, string sentence)
    {
        var numbers = new ReviewNumbersResponse(comparability, shared, before, after, 2, 1, true);

        var rendered = CommandTextRenderer.Render(CommandOutcome<ReviewNumbersResponse>.Success(numbers),
            asJson: false);

        Assert.Equal(sentence + Environment.NewLine, rendered.Output);
    }

    [Fact]
    public void AMeasuredPendingRevisionRendersItsNumbersSentence()
    {
        var measured = new MeasurePendingResult("job/one", "revision/one",
            new ReviewNumbersResponse(ReviewComparability.Compared, 1, 0, 1, 1, 1, true));

        var rendered = CommandTextRenderer.Render(CommandOutcome<MeasurePendingResult>.Success(measured),
            asJson: false);

        Assert.Equal("Among 1 shared word, approved kept: 0 → 1." + Environment.NewLine, rendered.Output);
    }

    [Fact]
    public void ReviewNumbersJsonCarriesTheComparabilityByName()
    {
        var numbers = new ReviewNumbersResponse(ReviewComparability.NoSharedWords, 0, 0, 0, 1, 0, false);

        var rendered = CommandTextRenderer.Render(CommandOutcome<ReviewNumbersResponse>.Success(numbers),
            asJson: true);

        Assert.Contains("\"comparability\": \"NoSharedWords\"", rendered.Output, StringComparison.Ordinal);
        Assert.Contains("\"evidenceComplete\": false", rendered.Output, StringComparison.Ordinal);
    }
}
