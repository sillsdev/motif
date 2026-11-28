namespace SIL.Motif.Commands.Requests;

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="Forms">The word forms to give a disposition, each as it appears in the corpus word list. Spellings that differ only in Unicode composition are one word type.</param>
/// <param name="Category">One of name, loan, other-language, abbreviation, numeral, non-word or misspelling. The word artifact is accepted for non-word.</param>
/// <param name="Evidence">What was seen that justifies the disposition. It cannot be empty.</param>
/// <param name="Intended">For a misspelling, the one word the form was meant to be. Required for misspelling and refused for every other category.</param>
/// <param name="Replace">End a different disposition that already stands and put this one in its place. Without it a different disposition is refused.</param>
/// <param name="RecordedBy">The person or agent recording it; defaults to the signed-in user.</param>
public sealed record DisposeWordFormsRequest(
    string FwDataPath, string ProductVersion, IReadOnlyList<string> Forms, string Category, string Evidence,
    string? Intended = null, bool Replace = false, string? RecordedBy = null);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="Forms">The word forms whose standing disposition to clear.</param>
/// <param name="Reason">Why it is cleared; kept in the history.</param>
/// <param name="RecordedBy">The person or agent clearing it; defaults to the signed-in user.</param>
public sealed record UndisposeWordFormsRequest(
    string FwDataPath, string ProductVersion, IReadOnlyList<string> Forms, string? Reason = null, string? RecordedBy = null);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="Form">The word form to look up, as it appears in the corpus word list.</param>
public sealed record ShowDispositionRequest(string FwDataPath, string ProductVersion, string Form);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="Category">Only this category: name, loan, other-language, abbreviation, numeral, non-word or misspelling. Leave out for all.</param>
/// <param name="Limit">The most dispositions on one page, from 1 to 200. The default is 50.</param>
/// <param name="Cursor">Opaque cursor from a previous response's nextCursor; omit for the first page.</param>
public sealed record ListDispositionsRequest(
    string FwDataPath, string ProductVersion, string? Category = null, int Limit = 50, string? Cursor = null)
{
    /// <summary>The rows to skip that <see cref="Cursor"/> names, or -1 when it is not a cursor this API issued. A method, so it is not a schema property.</summary>
    public int CursorOffset() => SIL.Motif.Contract.Responses.ResultPaging.OffsetOrInvalid(Cursor);
}

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="FilePath">A TSV (form, category, intended, evidence), a JSONL file of the same fields, or a grammar lane ledger whose Disposition rows are read.</param>
/// <param name="InputFormat">tsv, jsonl or ledger; left out, the format is detected from the first line.</param>
/// <param name="Replace">End a different disposition that already stands and put the row's in its place. Without it such a row is refused.</param>
/// <param name="DryRun">Report what would change and write nothing.</param>
/// <param name="SkipRefused">Write the rows that are accepted even when others are refused. Without it any refused row means nothing is written.</param>
/// <param name="Detail">Return a result for every row, not only the counts and refusals.</param>
/// <param name="RecordedBy">The person or agent recording them; defaults to the signed-in user.</param>
public sealed record DisposeFromFileRequest(
    string FwDataPath, string ProductVersion, string FilePath, string? InputFormat = null, bool Replace = false,
    bool DryRun = false, bool SkipRefused = false, bool Detail = false, string? RecordedBy = null);
