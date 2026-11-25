namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>The target is not a stem analysis of a proclitic, enclitic or clitic entry.</summary>
    public const string CliticAttachmentNotClitic = "clitic-attachment.not-clitic";
    /// <summary>A named category is not a part of speech in the project.</summary>
    public const string CliticAttachmentInvalidCategory = "clitic-attachment.invalid-category";
    /// <summary>The clitic's Attaches to Categories changed since the edit was composed.</summary>
    public const string CliticAttachmentStaleSource = "clitic-attachment.stale-source";
}
