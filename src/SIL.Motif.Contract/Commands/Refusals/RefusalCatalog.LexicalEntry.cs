using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsLexicalEntry() =>
    [
        Spec(RefusalCodes.LexicalEntryNoId, FailureReason.InvalidArgument),
        Spec(RefusalCodes.LexicalEntryInvalidId, FailureReason.InvalidArgument),
        Spec(RefusalCodes.LexicalEntryTooManyIds, FailureReason.InvalidArgument),
        Spec(RefusalCodes.LexicalEntryBaselineUnavailable, FailureReason.StoreInconsistent),
    ];

    private static IEnumerable<string> WindowCodesLexicalEntry() =>
    [
        RefusalCodes.LexicalEntryBaselineUnavailable,
        RefusalCodes.LexicalEntryInvalidId,
    ];
}
