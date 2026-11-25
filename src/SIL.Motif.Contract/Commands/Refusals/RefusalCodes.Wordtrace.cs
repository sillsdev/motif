namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string WordTraceCancelled = "wordtrace.cancelled";
    public const string WordTraceMalformedDiagnostic = "wordtrace.malformed-diagnostic";
    public const string WordTraceMalformedOutput = "wordtrace.malformed-output";
    public const string WordTraceNoBaseline = "wordtrace.no-baseline";
    public const string WordTraceParserRefused = "wordtrace.parser-refused";
    public const string WordTraceParserUnavailable = "wordtrace.parser-unavailable";
    /// <summary>A saved trace file could not be read.</summary>
    public const string WordTraceDiagnosticUnreadable = "wordtrace.diagnostic-unreadable";
}
