namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A why-lost request names no word or no Proposal id, or a word or cap Motif cannot trace.</summary>
    public const string WhyLostRequestInvalid = "why-lost.request-invalid";
    /// <summary>The Baseline and Trial captures were made with grammar hashes that do not compare, so no diff is read.</summary>
    public const string WhyLostIncomparable = "why-lost.incomparable";
}
