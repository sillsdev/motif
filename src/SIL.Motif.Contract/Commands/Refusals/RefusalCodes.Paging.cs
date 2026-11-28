namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A paged read's cursor is not the nextCursor of an earlier page of the same read, or its limit is out of range. The one refusal every paged read gives for either.</summary>
    public const string PagingInvalid = "paging.invalid";
}
