namespace SIL.Motif.Contract.Requests;

/// <summary>
/// Plans an edited record file against the project and lowers it to operations. Without a Draft name it is a dry run
/// that writes nothing; with one it stages the operations in a new Draft. It never applies.
/// </summary>
/// <param name="ProjectPath">The FieldWorks project the file was exported from.</param>
/// <param name="FilePath">The record file to import; null when <paramref name="DirPath"/> names a folder instead.</param>
/// <param name="DraftName">The new Draft to stage the operations in, or <see langword="null"/> for a dry run.</param>
/// <param name="Page">The page of changes, operations and refusals to list, from 1; each page holds <see cref="RecordImportPaging.PageSize"/> of each.</param>
/// <param name="Strict">Refuse to stage when any change is refused, instead of staging the changes that are not. For a folder, any refused file or change.</param>
/// <param name="DirPath">A folder whose <c>*.fw.md</c> files, searched recursively, are imported as one Proposal; exclusive with <paramref name="FilePath"/>.</param>
/// <param name="Rows">For a folder, list the file rows of <paramref name="Page"/> (<see cref="RecordImportPaging.RowsPerPage"/> to a page) instead of only the summary.</param>
/// <param name="RefusedOnly">For a folder, list only the refused rows.</param>
/// <param name="ReportPath">For a folder, a JSONL file to write every row to; the command line only.</param>
/// <param name="Source">Where a folder's records came from, kept with the Proposal.</param>
/// <param name="Revision">The revision of <paramref name="Source"/>, kept with the Proposal.</param>
/// <param name="Licence">The licence of <paramref name="Source"/>, kept with the Proposal.</param>
public sealed record RecordImportRequest(string ProjectPath, string? FilePath, string? DraftName = null, int Page = 1,
    bool Strict = false, string? DirPath = null, bool? Rows = null, bool? RefusedOnly = null, string? ReportPath = null,
    string? Source = null, string? Revision = null, string? Licence = null);

/// <summary>How a record import response pages its lists, so the summary stays within <see cref="SummaryBytes"/>.</summary>
public static class RecordImportPaging
{
    /// <summary>The changes, and separately the operations and the refusals, listed on one page.</summary>
    public const int PageSize = 3;

    /// <summary>The longest text kept in a listed refusal reason, class, field or operation kind, before the response is fitted to <see cref="SummaryBytes"/>.</summary>
    public const int TextLength = 90;

    /// <summary>The size a serialized response must stay within.</summary>
    public const int SummaryBytes = 2048;

    /// <summary>The file rows of a folder import listed on one page.</summary>
    public const int RowsPerPage = 200;

    /// <summary>The refused rows a folder import's summary lists.</summary>
    public const int SummaryRefusedRows = 5;

    /// <summary>The renumbered entries, and separately the back-referenced entries, a folder summary lists.</summary>
    public const int SummaryReviewRows = 3;

    /// <summary>The most files one folder import takes.</summary>
    public const int MaxFiles = 10_000;

    /// <summary>The longest <c>--source</c>, <c>--revision</c> or <c>--licence</c> text.</summary>
    public const int MaxOptionLength = 200;

    /// <summary>The most operations one folder import takes.</summary>
    public const int MaxOperations = 250_000;
}
