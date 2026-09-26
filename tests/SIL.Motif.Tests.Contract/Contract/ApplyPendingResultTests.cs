using System.Text.Json;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class ApplyPendingResultTests
{
    private static readonly JsonSerializerOptions ReaderOptions =
        new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void NothingPendingIsExactlyASuccessThatAppliedNothing()
    {
        var rendered = ProjectionJson.Serialize(ApplyPendingResult.NothingPending);

        using var document = JsonDocument.Parse(rendered);
        Assert.Equal("{\"ok\":true,\"applied\":false}", JsonSerializer.Serialize(document.RootElement));
    }

    [Fact]
    public void AnAppliedResultCarriesItsReceiptUnderOk()
    {
        var rendered = ProjectionJson.Serialize(ApplyPendingResult.AppliedWith(Receipt()));

        using var document = JsonDocument.Parse(rendered);
        var root = document.RootElement;
        Assert.Equal(["ok", "applied", "receipt"], root.EnumerateObject().Select(property => property.Name));
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.True(root.GetProperty("applied").GetBoolean());
        Assert.Equal("proposal/one", root.GetProperty("receipt").GetProperty("proposalId").GetString());
    }

    [Fact]
    public void BothShapesBindFromTheirOwnRenderedJson()
    {
        var nothing = JsonSerializer.Deserialize<ApplyPendingResult>(
            ProjectionJson.Serialize(ApplyPendingResult.NothingPending), ReaderOptions);
        var applied = JsonSerializer.Deserialize<ApplyPendingResult>(
            ProjectionJson.Serialize(ApplyPendingResult.AppliedWith(Receipt())), ReaderOptions);

        Assert.False(nothing!.Applied);
        Assert.Null(nothing.Receipt);
        Assert.True(applied!.Applied);
        Assert.Equal("proposal/one", applied.Receipt!.ProposalId);
    }

    [Theory]
    [InlineData("{\"ok\":true,\"applied\":true}")]
    [InlineData("{\"ok\":true,\"applied\":false,\"receipt\":{\"proposalId\":\"proposal/one\"}}")]
    public void AResultWhoseReceiptDisagreesWithAppliedIsRefused(string json) =>
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<ApplyPendingResult>(json, ReaderOptions));

    private static ApplyProjection Receipt() => new("proposal/one", false, "Applied", [], "sha256:effect",
        new AppliedLogEntrySummary("proposal/one", "2026-01-01", "Motif", "sha256:intent"));
}
