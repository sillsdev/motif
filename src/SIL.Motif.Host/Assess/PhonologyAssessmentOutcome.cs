using SIL.Motif.Contract.Phonology;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Host.Assess;

/// <summary>Separates validated producer rows from an invocation that supplied no measurement.</summary>
public abstract record PhonologyAssessmentOutcome
{
    private PhonologyAssessmentOutcome() { }

    public sealed record Produced(PhonologyMeasurement Measurement) : PhonologyAssessmentOutcome;
    public sealed record Unavailable(PhonologyInvocationReason Reason, string Detail) : PhonologyAssessmentOutcome;

    public static PhonologyAssessmentOutcome From(PanGlossOutcome outcome) => outcome switch
    {
        PanGlossOutcome.Completed { Phonology: { } measurement } => new Produced(measurement),
        PanGlossOutcome.Unavailable unavailable => new Unavailable(unavailable.ExecutableMissing
            ? PhonologyInvocationReason.AbsentProducer : PhonologyInvocationReason.ProducerUnavailable, unavailable.Detail),
        PanGlossOutcome.Refused refused => new Unavailable(PhonologyInvocationReason.InvocationRefused, refused.Detail),
        PanGlossOutcome.TimedOut timedOut => new Unavailable(PhonologyInvocationReason.InvocationTimedOut, timedOut.Detail),
        PanGlossOutcome.Cancelled => new Unavailable(PhonologyInvocationReason.Cancelled, outcome.Message),
        PanGlossOutcome.Incomplete { Cause: PanGlossIncompleteCause.Infrastructure } =>
            throw new PhonologyInfrastructureException(outcome.Message),
        _ => throw new InvalidDataException("The phonology invocation supplied invalid or missing evidence: " + outcome.Message),
    };
}

/// <summary>Invocation infrastructure failed without a confirmed producer result.</summary>
public sealed class PhonologyInfrastructureException(string message) : IOException(message);
