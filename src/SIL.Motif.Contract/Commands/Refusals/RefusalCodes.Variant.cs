namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>The selected object is not a variant reference owned by an entry.</summary>
    public const string VariantInvalidReference = "variant.invalid-reference";
    /// <summary>The selected component is not an entry or sense.</summary>
    public const string VariantInvalidComponent = "variant.invalid-component";
    /// <summary>The selected type is not a variant type.</summary>
    public const string VariantInvalidType = "variant.invalid-type";
    /// <summary>A variant cannot refer to its own entry or sense.</summary>
    public const string VariantSelfLink = "variant.self-link";
    /// <summary>The link would create a cycle of variants.</summary>
    public const string VariantCycle = "variant.cycle";
    /// <summary>The entry to make a variant is not an entry.</summary>
    public const string VariantInvalidEntry = "variant.invalid-entry";
    /// <summary>The entry already has a variant reference to that component.</summary>
    public const string VariantAlreadyLinked = "variant.already-linked";
    /// <summary>The variant reference changed after the link was composed.</summary>
    public const string VariantStaleReference = "variant.stale-reference";
}
