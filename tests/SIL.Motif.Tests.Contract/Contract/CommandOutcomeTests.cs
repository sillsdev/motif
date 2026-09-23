using System.Collections.Generic;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract;

/// <summary>Pins <see cref="CommandOutcome{T}"/> and <see cref="Refusal"/> as a closed, branchable carrier.</summary>
public sealed class CommandOutcomeTests
{
    [Fact]
    public void SucceededOutcomeCarriesTheValueAndNoRefusal()
    {
        var outcome = CommandOutcome<string>.Success("proposal/abc");

        Assert.True(outcome.Succeeded);
        Assert.Equal("proposal/abc", outcome.Value);
        Assert.Null(outcome.Refusal);
    }

    [Fact]
    public void RefusedOutcomeCarriesBranchableFacts()
    {
        var refusal = new Refusal("proposal.not-found", FailureReason.NotFound,
            "Proposal proposal/absent was not found.",
            new Dictionary<string, string> { ["proposalId"] = "proposal/absent" });

        var outcome = CommandOutcome<string>.Refused(refusal);

        Assert.False(outcome.Succeeded);
        Assert.Null(outcome.Value);
        Assert.Equal("proposal/absent", outcome.Refusal!.Facts["proposalId"]);
    }

    [Fact]
    public void ASuccessOutcomeCannotCarryANullValue()
    {
        // A null "success" would read as a refusal, since the carrier discriminates on `value is null`.
        Assert.Throws<System.ArgumentException>(() => CommandOutcome<string>.Success(null!));
    }

    [Fact]
    public void ARefusalRequiresANonblankCode()
    {
        Assert.Throws<System.ArgumentException>(() =>
            new Refusal(" ", FailureReason.Refused, "message"));
    }

    [Fact]
    public void ARefusalRequiresANonblankMessage()
    {
        Assert.Throws<System.ArgumentException>(() =>
            new Refusal("proposal.invalid-id", FailureReason.InvalidArgument, " "));
    }

    [Fact]
    public void ARefusalsFactsAreImmutableAgainstItsSourceDictionary()
    {
        var source = new Dictionary<string, string> { ["field"] = "status" };
        var refusal = new Refusal("draft.invalid", FailureReason.InvalidArgument, "message", source);

        source["field"] = "changed after construction";

        Assert.Equal("status", refusal.Facts["field"]);
    }

    [Fact]
    public void ARefusalWithNoFactsHasAnEmptyMap()
    {
        var refusal = new Refusal("apply.not-ready", FailureReason.Refused, "message");

        Assert.Empty(refusal.Facts);
    }

    [Fact]
    public void ARefusalRoundTripsThroughJson()
    {
        var refusal = new Refusal("operation.slot-collision", FailureReason.Refused,
            "Slot already carries an operation.",
            new Dictionary<string, string> { ["slot"] = "MoInflAffixSlot/1" });

        var rendered = ProjectionJson.Serialize(refusal);
        var bound = ProjectionJson.Deserialize<Refusal>(rendered);

        Assert.NotNull(bound);
        Assert.Equal(refusal.Code, bound!.Code);
        Assert.Equal(refusal.Reason, bound.Reason);
        Assert.Equal(refusal.Message, bound.Message);
        Assert.Equal(refusal.Facts["slot"], bound.Facts["slot"]);
    }
}
