namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A Guide query's run line is not one well-formed <c>motif query</c> or <c>motif group</c> command.</summary>
    public const string GuideQueryRunInvalid = "guide-query.run-invalid";
    /// <summary>A corpus run line names a file by an absolute path or one that leaves the project's folder; a shared Guide may not read other files.</summary>
    public const string GuideQueryRunPathOutsideProject = "guide-query.run-path-outside-project";
    /// <summary>A Guide query's run line names a command that can change something or is not a query.</summary>
    public const string GuideQueryRunNotReadOnly = "guide-query.run-not-read-only";
}
