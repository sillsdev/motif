namespace SIL.Motif.Contract.Requests;

/// <summary>
/// Plans an edited record file against the project and lowers it to operations. Without a Draft name it is a dry run
/// that writes nothing; with one it stages the operations in a new Draft. It never applies.
/// </summary>
/// <param name="ProjectPath">The FieldWorks project the file was exported from.</param>
/// <param name="FilePath">The record file to import.</param>
/// <param name="DraftName">The new Draft to stage the operations in, or <see langword="null"/> for a dry run.</param>
/// <param name="Page">The page of changes, operations and refusals to list, from 1; each page holds <see cref="RecordImportPaging.PageSize"/> of each.</param>
/// <param name="Strict">Refuse to stage when any change is refused, instead of staging the changes that are not.</param>
public sealed record RecordImportRequest(string ProjectPath, string FilePath, string? DraftName = null, int Page = 1,
    bool Strict = false);

/// <summary>How a record import response pages its lists, so the summary stays within <see cref="SummaryBytes"/>.</summary>
public static class RecordImportPaging
{
    /// <summary>The changes, and separately the operations and the refusals, listed on one page.</summary>
    public const int PageSize = 3;

    /// <summary>The longest text kept in a listed refusal reason, class, field or operation kind, before the response is fitted to <see cref="SummaryBytes"/>.</summary>
    public const int TextLength = 90;

    /// <summary>The size a serialized response must stay within.</summary>
    public const int SummaryBytes = 2048;
}
