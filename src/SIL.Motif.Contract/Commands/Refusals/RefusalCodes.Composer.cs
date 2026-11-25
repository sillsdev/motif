namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A composer search named both a goal and a Subject, or neither.</summary>
    public const string ComposerGoalOrSubject = "composer.goal-or-subject";
    /// <summary>A composer search asked for a level of detail other than concise or detailed.</summary>
    public const string ComposerInvalidDetail = "composer.invalid-detail";
    /// <summary>A composer search named a Subject that no composer serves.</summary>
    public const string ComposerUnknownSubject = "composer.unknown-subject";
}
