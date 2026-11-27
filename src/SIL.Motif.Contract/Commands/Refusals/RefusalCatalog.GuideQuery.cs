using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsGuideQuery() =>
    [
        Spec(RefusalCodes.GuideQueryRunInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.GuideQueryRunNotReadOnly, FailureReason.Refused),
        Spec(RefusalCodes.GuideQueryRunPathOutsideProject, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesGuideQuery() =>
    [
        RefusalCodes.GuideQueryRunInvalid,
        RefusalCodes.GuideQueryRunNotReadOnly,
        RefusalCodes.GuideQueryRunPathOutsideProject,
    ];
}
