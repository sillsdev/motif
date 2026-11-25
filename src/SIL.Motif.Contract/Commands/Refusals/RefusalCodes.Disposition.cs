namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A disposition request names no form, a malformed form, an unknown category, no evidence, or a wrong intended form.</summary>
    public const string DispositionInvalid = "disposition.invalid";
    /// <summary>A word type already stands with another disposition and the write did not ask to replace it.</summary>
    public const string DispositionContradiction = "disposition.contradiction";
}
