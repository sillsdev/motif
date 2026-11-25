namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A Guide query's run line is not one well-formed <c>motif query</c> or <c>motif group</c> command.</summary>
    public const string GuideQueryRunInvalid = "guide-query.run-invalid";
    /// <summary>A Guide query's run line names a command that can change something or is not a query.</summary>
    public const string GuideQueryRunNotReadOnly = "guide-query.run-not-read-only";
}
