namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string GrammarCheckCancelled = "grammarcheck.cancelled";
    public const string GrammarCheckFailed = "grammarcheck.failed";
    public const string GrammarCheckMalformedFindings = "grammarcheck.malformed-findings";
    public const string GrammarCheckParserRefused = "grammarcheck.parser-refused";
    public const string GrammarCheckParserUnavailable = "grammarcheck.parser-unavailable";
    public const string GrammarCheckTimedOut = "grammarcheck.timed-out";
    public const string GrammarCheckUnsupportedSchema = "grammarcheck.unsupported-schema";
    /// <summary>No grammar check could run, so there are no findings to report.</summary>
    public const string GrammarCheckNotRun = "grammarcheck.not-run";
}
