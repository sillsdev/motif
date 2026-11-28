using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsEvidence() =>
    [
        Spec(RefusalCodes.EvidenceUnknownTier, FailureReason.InvalidArgument, NextStep.Repeat()),
        Spec(RefusalCodes.EvidenceOutUnwritable, FailureReason.InvalidArgument),
        Spec(RefusalCodes.EvidenceMissing, FailureReason.Refused),
        Spec(RefusalCodes.EvidenceWrongTarget, FailureReason.Refused),
    ];
}
