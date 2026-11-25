namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string HandoffCancelled = "handoff.cancelled";
    public const string HandoffDestinationExists = "handoff.destination-exists";
    public const string HandoffInvocationMismatch = "handoff.invocation-mismatch";
    public const string HandoffInvocationNotFound = "handoff.invocation-not-found";
    public const string HandoffInvocationRequired = "handoff.invocation-required";
    public const string HandoffParserUnavailable = "handoff.parser-unavailable";
    public const string HandoffSourceUnavailable = "handoff.source-unavailable";
    public const string HandoffStatisticsUnavailable = "handoff.statistics-unavailable";
    public const string HandoffTextNotFound = "handoff.text-not-found";
    /// <summary>The selected trace does not record the Baseline that produced it.</summary>
    public const string HandoffTraceBaselineUnavailable = "handoff.trace-baseline-unavailable";
    /// <summary>A trace Handoff does not hold only that trace and its captured Baseline.</summary>
    public const string HandoffTraceUnavailable = "handoff.trace-unavailable";
}
