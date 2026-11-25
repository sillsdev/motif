namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>The project has several definitions of the reserved judgment field.</summary>
    public const string JudgmentFieldAmbiguous = "judgment.field-ambiguous";
    /// <summary>A reserved judgment field has an incompatible schema.</summary>
    public const string JudgmentFieldIncompatible = "judgment.field-incompatible";
    /// <summary>The project holds judgments in an old format that Motif no longer reads.</summary>
    public const string JudgmentOldFormat = "judgment.old-format";
}
