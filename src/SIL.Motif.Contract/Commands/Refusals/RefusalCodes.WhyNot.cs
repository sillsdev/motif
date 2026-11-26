namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A why-not request names no word, no candidate, or a candidate PanGloss cannot be given, or is over a cap.</summary>
    public const string WhyNotRequestInvalid = "why-not.request-invalid";
    /// <summary>The installed PanGloss does not declare the why-not capability Motif reads, so it cannot answer.</summary>
    public const string WhyNotParserUnsupported = "why-not.parser-unsupported";
    /// <summary>PanGloss rejected the why-not input, such as an unknown morph id, and said why.</summary>
    public const string WhyNotInputRefused = "why-not.input-refused";
    /// <summary>PanGloss wrote a why-not report Motif cannot read or that does not match the candidates asked.</summary>
    public const string WhyNotMalformed = "why-not.malformed";
    /// <summary>The why-not run ended without a complete report, by the wall-clock limit or an unreadable result.</summary>
    public const string WhyNotUnfinished = "why-not.unfinished";
}
