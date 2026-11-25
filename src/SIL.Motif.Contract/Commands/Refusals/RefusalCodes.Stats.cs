namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string StatsCancelled = "stats.cancelled";
    public const string StatsInvalidEvidence = "stats.invalid-evidence";
    public const string StatsNoAssessment = "stats.no-assessment";
    public const string StatsNoCache = "stats.no-cache";
    public const string StatsNoEvidence = "stats.no-evidence";
    public const string StatsParserRefused = "stats.parser-refused";
    public const string StatsParserUnavailable = "stats.parser-unavailable";
    public const string StatsTimedOut = "stats.timed-out";
    /// <summary>Forwarded arguments already name a format that the JSON request would override.</summary>
    public const string StatsFormatConflict = "stats.format-conflict";
    /// <summary>The named Assessment is not part of the Proposal's Trial.</summary>
    public const string StatsWrongAssessment = "stats.wrong-assessment";
    /// <summary>The named Assessment did not collect per-object statistics.</summary>
    public const string StatsWrongKind = "stats.wrong-kind";
}
