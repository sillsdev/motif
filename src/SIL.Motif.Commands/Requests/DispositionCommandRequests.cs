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
/// <param name="Page">The one-based page. The default is 1.</param>
public sealed record ListDispositionsRequest(
    string FwDataPath, string ProductVersion, string? Category = null, int Limit = 50, int Page = 1);
