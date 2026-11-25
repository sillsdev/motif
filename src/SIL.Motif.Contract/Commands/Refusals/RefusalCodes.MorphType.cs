namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>The form's morph type changed since the edit was composed.</summary>
    public const string MorphTypeStaleSource = "morph-type.stale-source";
    /// <summary>The morph-type edit would leave the supported stem family.</summary>
    public const string MorphTypeIncompatibleFamily = "morph-type.incompatible-family";
}
