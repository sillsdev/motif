namespace SIL.Motif.Contract.Commands;

/// <summary>
/// Names of <see cref="Refusal.Facts"/> entries that a caller branches on, not only displays.
/// </summary>
public static class RefusalFactNames
{
    /// <summary>
    /// Present, with the value <c>true</c>, on a parser-unavailable refusal whose cause is that no PanGloss
    /// executable was found. Absent when PanGloss was found but could not be run.
    /// </summary>
    public const string ParserNotFound = "parserNotFound";

    /// <summary>The full path of a refused Motif store file, on a <see cref="RefusalCodes.StoreOtherVersion"/> refusal.</summary>
    public const string StorePath = "storePath";
}
