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
        var restored = ProjectionJson.Deserialize<ChangeFitResult>(json)!;
        Assert.Equal(fit.OperationId, restored.OperationId);
        Assert.Equal(fit.StillFits, restored.StillFits);
        Assert.Equal(fit.Reason, restored.Reason);
        Assert.Equal(fit.BaselineToken, restored.BaselineToken);
        Assert.Equal(fit.Status, restored.Status);
        Assert.Equal(fit.Uncertainty!.Reason, restored.Uncertainty!.Reason);
        Assert.Equal(fit.Uncertainty.BeforeTokens, restored.Uncertainty.BeforeTokens);
        Assert.Equal(fit.Uncertainty.AfterTokens, restored.Uncertainty.AfterTokens);
    }

    [Fact]
    public void StillFitsIsDerivedFromTheMachineReadableStatus()
    {
        var fitting = new ChangeFitResult("operation", false, "Still fits.", "baseline")
        {
            Status = ChangeFitStatus.Fits,
        };

        Assert.True(fitting.StillFits);
    }
}
