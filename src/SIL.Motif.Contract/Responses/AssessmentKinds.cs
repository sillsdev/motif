namespace SIL.Motif.Contract.Responses;

/// <summary>
/// The wire spelling of each measurement an <c>assess</c> run returns, as <see cref="ProducedAssessmentReference.Kind"/>
/// carries it. A reader that wants one measurement of a run names its kind here rather than spelling a literal of
/// its own, so every reader looks for the same word. Each value is the kind's stored spelling in the Motif store.
/// </summary>
public static class AssessmentKinds
{
    /// <summary>How long each word of the Selection took to parse, and how each search ended.</summary>
    public const string ParseTime = "ParseTime";

    /// <summary>The parser's own per-rule and per-morpheme counters over the same words.</summary>
    public const string ObjectTiming = "ObjectTiming";

    /// <summary>How the parser's readings compare with the analyses the project approves.</summary>
    public const string Correctness = "Correctness";
}
