using System.Text.Json;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class ReviewContractTests
{
    private static readonly JsonSerializerOptions ReaderOptions =
        new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void ReviewNumbersCarryTheWordsThatWouldLoseAnApprovedAnalysis()
    {
        var numbers = new ReviewNumbersResponse(ReviewComparability.Compared, 2, 2, 1, 2, 1, true)
        {
            WordsLosingApprovedAnalysis = ["cat"],
        };

        var bound = JsonSerializer.Deserialize<ReviewNumbersResponse>(ProjectionJson.Serialize(numbers), ReaderOptions);

        Assert.Equal(["cat"], bound?.WordsLosingApprovedAnalysis);
    }
}
