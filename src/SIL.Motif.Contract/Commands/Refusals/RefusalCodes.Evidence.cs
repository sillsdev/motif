namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string EvidenceInvalidPage = "evidence.invalid-page";
    public const string EvidenceUnknownTier = "evidence.unknown-tier";
    /// <summary>An export cursor is not one an earlier page of the same export returned.</summary>
    public const string EvidenceInvalidCursor = "evidence.invalid-cursor";
    /// <summary>The export file could not be written, so nothing was left at the path.</summary>
    public const string EvidenceOutUnwritable = "evidence.out-unwritable";
    /// <summary>A decision evidence record was staged without its decisionEvidence entry.</summary>
    public const string EvidenceMissing = "evidence.missing";
    /// <summary>A decision evidence record names something that is not an existing wordform or analysis.</summary>
    public const string EvidenceWrongTarget = "evidence.wrong-target";
}
