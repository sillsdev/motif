namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string TimingInvalidOverride = "timing.invalid-override";
    public const string TimingNoAssessment = "timing.no-assessment";
    public const string TimingNoBaseline = "timing.no-baseline";
    public const string TimingOverrideNotFound = "timing.override-not-found";
    public const string TimingWordSetNotFound = "timing.word-set-not-found";
    public const string TimingWrongKind = "timing.wrong-kind";
    /// <summary>A timing request lacks a valid grouping or a positive top count.</summary>
    public const string TimingInvalidRequest = "timing.invalid-request";
    /// <summary>A timing word set is not valid.</summary>
    public const string TimingInvalidWordSet = "timing.invalid-word-set";
    /// <summary>The named Assessment is not part of the Proposal's Trial.</summary>
    public const string TimingWrongAssessment = "timing.wrong-assessment";
}
