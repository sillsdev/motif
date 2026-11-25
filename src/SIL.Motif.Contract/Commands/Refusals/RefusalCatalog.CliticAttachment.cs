using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsCliticAttachment() =>
    [
        Spec(RefusalCodes.CliticAttachmentNotClitic, FailureReason.Refused),
        Spec(RefusalCodes.CliticAttachmentInvalidCategory, FailureReason.Refused),
        Spec(RefusalCodes.CliticAttachmentStaleSource, FailureReason.Refused),
    ];
}
