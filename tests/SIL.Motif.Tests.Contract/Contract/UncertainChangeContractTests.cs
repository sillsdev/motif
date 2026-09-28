using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class UncertainChangeContractTests
{
    [Fact]
    public void ChangeIntentCarriesAnOptionalOccurrenceAnchor()
    {
        var occurrence = new OccurrenceAnchor(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000002"),
            Guid.Parse("30000000-0000-0000-0000-000000000003"), 2);
        var intent = new ChangeIntent("change", "approve", "wordform", "form", Occurrence: occurrence);

        var json = ProjectionJson.Serialize(intent);
        var restored = ProjectionJson.Deserialize<ChangeIntent>(json)!;

        Assert.Contains("\"occurrence\"", json, StringComparison.Ordinal);
        Assert.Equal(occurrence, restored.Occurrence);
    }

    [Fact]
    public void UncertainFitNamesItsStatusAndCarriesBeforeAndAfterTokens()
    {
        var fit = new ChangeFitResult("operation", false, "The sentence changed.", "before-baseline")
        {
            Status = "uncertain",
            Uncertainty = new ChangeUncertainty("The sentence changed.",
                [new OccurrenceWordToken(0, "wordform-before", "before")],
                [new OccurrenceWordToken(0, "wordform-after", "after")]),
        };

        var json = ProjectionJson.Serialize(fit);

        Assert.Contains("\"status\": \"uncertain\"", json, StringComparison.Ordinal);
        Assert.Contains("\"beforeTokens\"", json, StringComparison.Ordinal);
        Assert.Contains("\"afterTokens\"", json, StringComparison.Ordinal);
        Assert.Contains("\"wordformId\": \"wordform-after\"", json, StringComparison.Ordinal);
        Assert.Equal(fit, ProjectionJson.Deserialize<ChangeFitResult>(json));
    }
}
