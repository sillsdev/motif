using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsTiming() =>
    [
        Spec(RefusalCodes.TimingInvalidOverride, FailureReason.NotFound),
        Spec(RefusalCodes.TimingNoAssessment, FailureReason.NotFound),
        Spec(RefusalCodes.TimingNoBaseline, FailureReason.NotFound),
        Spec(RefusalCodes.TimingOverrideNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.TimingWordSetNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.TimingWrongKind, FailureReason.NotFound),
        Spec(RefusalCodes.TimingInvalidRequest, FailureReason.InvalidArgument),
        Spec(RefusalCodes.TimingInvalidWordSet, FailureReason.InvalidArgument),
        Spec(RefusalCodes.TimingWrongAssessment, FailureReason.NotFound),
    ];

    private static IEnumerable<string> WindowCodesTiming() =>
    [
        RefusalCodes.TimingInvalidOverride,
        RefusalCodes.TimingNoAssessment,
        RefusalCodes.TimingNoBaseline,
        RefusalCodes.TimingOverrideNotFound,
        RefusalCodes.TimingWordSetNotFound,
        RefusalCodes.TimingWrongKind,
    ];
}
