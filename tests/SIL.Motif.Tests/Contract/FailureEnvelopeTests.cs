using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class FailureEnvelopeTests
{
    [Fact]
    public void CancelledUsesTheNonRetryableCommandExitCode()
    {
        Assert.Equal(2, FailureEnvelope.ExitCodeFor(FailureReason.Cancelled));
    }
}
