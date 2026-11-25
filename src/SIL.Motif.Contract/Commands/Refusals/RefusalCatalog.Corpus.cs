using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsCorpus() =>
    [
        Spec(RefusalCodes.CorpusBundleInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.CorpusDocumentInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.CorpusDocumentNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.CorpusInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.CorpusNotFound, FailureReason.NotFound),
    ];
}
