namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>The saved Text Progress projection has not been published or needs reconciliation.</summary>
    public const string ProgressIndexNotReady = "progress.index-not-ready";
    /// <summary>A Progress cursor is malformed or belongs to another scope or filter.</summary>
    public const string ProgressInvalidCursor = "progress.invalid-cursor";
    /// <summary>A Progress request has an unsupported scope, detail or page size.</summary>
    public const string ProgressInvalidRequest = "progress.invalid-request";
    /// <summary>A Progress page belongs to a revision that has since changed.</summary>
    public const string ProgressRevisionChanged = "progress.revision-changed";
    /// <summary>A requested Text is absent from the saved Progress projection.</summary>
    public const string ProgressTextNotFound = "progress.text-not-found";
}
