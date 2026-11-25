using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsPhonologyExamples() =>
    [
        Spec(RefusalCodes.PhonologyExamplesAssessmentNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.PhonologyExamplesBaselineChanged, FailureReason.Refused),
        Spec(RefusalCodes.PhonologyExamplesEntryNotFound, FailureReason.NotFound,
            Command("baseline capture", Caller("project"))),
        Spec(RefusalCodes.PhonologyExamplesExampleNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.PhonologyExamplesInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.PhonologyExamplesNoBaseline, FailureReason.Refused,
            Command("baseline capture", Caller("project"))),
        Spec(RefusalCodes.PhonologyExamplesProposalNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.PhonologyExamplesRevisionConflict, FailureReason.Refused),
        Spec(RefusalCodes.PhonologyExamplesRevisionNotFound, FailureReason.NotFound),
    ];
}
