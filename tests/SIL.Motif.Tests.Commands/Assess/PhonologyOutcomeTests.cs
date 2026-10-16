using SIL.Motif.Contract.Phonology;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Assess;

public sealed class PhonologyOutcomeTests
{
    [Fact]
    public void InvocationReasonsStayTypedAndInvalidEvidenceIsNeverUnavailableAgreement()
    {
        var outcomes = new (PanGlossOutcome Outcome, PhonologyInvocationReason Reason)[]
        {
            (new PanGlossOutcome.Unavailable("absent") { ExecutableMissing = true }, PhonologyInvocationReason.AbsentProducer),
            (new PanGlossOutcome.Unavailable("unsupported"), PhonologyInvocationReason.ProducerUnavailable),
            (new PanGlossOutcome.Refused(1, "stderr", "", "refused"), PhonologyInvocationReason.InvocationRefused),
            (new PanGlossOutcome.TimedOut(TimeSpan.FromSeconds(1), "timeout"), PhonologyInvocationReason.InvocationTimedOut),
            (new PanGlossOutcome.Cancelled(), PhonologyInvocationReason.Cancelled),
        };
        foreach (var (outcome, reason) in outcomes)
        {
            var unavailable = Assert.IsType<PhonologyAssessmentOutcome.Unavailable>(PhonologyAssessmentOutcome.From(outcome));
            Assert.Equal(reason, unavailable.Reason);
        }
        Assert.Throws<InvalidDataException>(() => PhonologyAssessmentOutcome.From(new PanGlossOutcome.Completed("", "", TimeSpan.Zero)));
        Assert.Throws<InvalidDataException>(() => PhonologyAssessmentOutcome.From(new PanGlossOutcome.Incomplete("bad", "")));
        Assert.Throws<PhonologyInfrastructureException>(() => PhonologyAssessmentOutcome.From(
            new PanGlossOutcome.Incomplete("unconfirmed stop", "") { Cause = PanGlossIncompleteCause.Infrastructure }));
    }
}
