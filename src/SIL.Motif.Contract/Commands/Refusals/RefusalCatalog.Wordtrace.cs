using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsWordtrace() =>
    [
        Spec(RefusalCodes.WordTraceCancelled, FailureReason.Cancelled),
        Spec(RefusalCodes.WordTraceMalformedDiagnostic, FailureReason.Refused),
        Spec(RefusalCodes.WordTraceMalformedOutput, FailureReason.Refused),
        Spec(RefusalCodes.WordTraceNoBaseline, FailureReason.Refused),
        Spec(RefusalCodes.WordTraceParserRefused, FailureReason.Refused),
        Spec(RefusalCodes.WordTraceParserUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.WordTraceDiagnosticUnreadable, FailureReason.NotFound),
    ];

    private static IEnumerable<string> WindowCodesWordtrace() =>
    [
        RefusalCodes.WordTraceCancelled,
        RefusalCodes.WordTraceMalformedDiagnostic,
        RefusalCodes.WordTraceMalformedOutput,
        RefusalCodes.WordTraceNoBaseline,
        RefusalCodes.WordTraceParserRefused,
        RefusalCodes.WordTraceParserUnavailable,
    ];
}
