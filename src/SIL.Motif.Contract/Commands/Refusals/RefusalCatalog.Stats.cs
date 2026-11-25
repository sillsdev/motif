using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsStats() =>
    [
        Spec(RefusalCodes.StatsCancelled, FailureReason.Refused),
        Spec(RefusalCodes.StatsInvalidEvidence, FailureReason.Refused),
        Spec(RefusalCodes.StatsNoAssessment, FailureReason.InvalidArgument),
        Spec(RefusalCodes.StatsNoCache, FailureReason.Refused),
        Spec(RefusalCodes.StatsNoEvidence, FailureReason.Refused),
        Spec(RefusalCodes.StatsParserRefused, FailureReason.Refused),
        Spec(RefusalCodes.StatsParserUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.StatsTimedOut, FailureReason.Refused),
        Spec(RefusalCodes.StatsFormatConflict, FailureReason.InvalidArgument),
        Spec(RefusalCodes.StatsWrongAssessment, FailureReason.InvalidArgument),
        Spec(RefusalCodes.StatsWrongKind, FailureReason.InvalidArgument),
    ];

    private static IEnumerable<string> WindowCodesStats() =>
    [
        RefusalCodes.StatsCancelled,
        RefusalCodes.StatsInvalidEvidence,
        RefusalCodes.StatsNoAssessment,
        RefusalCodes.StatsNoCache,
        RefusalCodes.StatsNoEvidence,
        RefusalCodes.StatsParserRefused,
        RefusalCodes.StatsParserUnavailable,
        RefusalCodes.StatsTimedOut,
    ];
}
