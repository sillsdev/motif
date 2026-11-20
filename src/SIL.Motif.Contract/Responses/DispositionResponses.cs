using SIL.Motif.Contract.Dispositions;

namespace SIL.Motif.Contract.Responses;

/// <summary>What one form of a <c>wordform dispose</c> call came to.</summary>
/// <param name="Form">The form as it was given.</param>
/// <param name="Key">The word type key the form was recorded under.</param>
/// <param name="Result"><c>recorded</c>, <c>replaced</c> (another disposition ended and this one took its place), <c>already-recorded</c> (the type already stands with this category and intended form and holds this evidence, and nothing was written) or <c>evidence-added</c> (it stands as asked and the new evidence was appended to its evidence).</param>
/// <param name="Disposition">The disposition that stands for the type after the call.</param>
public sealed record DisposeFormResult(string Form, string Key, string Result, WordTypeDisposition Disposition);

/// <summary>The outcome of one <c>wordform dispose</c> call, which writes every form or none.</summary>
/// <param name="Results">One entry per distinct word type, in the order first given.</param>
/// <param name="Recorded">How many types gained a disposition.</param>
/// <param name="Replaced">How many types had another disposition replaced.</param>
/// <param name="AlreadyRecorded">How many types already stood as asked, whether or not they gained new evidence.</param>
public sealed record DisposeFormsResponse(
    IReadOnlyList<DisposeFormResult> Results, int Recorded, int Replaced, int AlreadyRecorded);

/// <summary>What one form of a <c>wordform undispose</c> call came to.</summary>
/// <param name="Form">The form as it was given.</param>
/// <param name="Key">The word type key.</param>
/// <param name="Result"><c>cleared</c> or <c>not-recorded</c> (the type had no standing disposition, and nothing changed).</param>
/// <param name="Cleared">The disposition that was removed, or null when none stood.</param>
public sealed record UndisposeFormResult(string Form, string Key, string Result, WordTypeDisposition? Cleared);

/// <summary>The outcome of one <c>wordform undispose</c> call.</summary>
/// <param name="Results">One entry per distinct word type, in the order first given.</param>
/// <param name="Cleared">How many dispositions were removed; each stays in the type's history.</param>
/// <param name="NotRecorded">How many types had none.</param>
public sealed record UndisposeFormsResponse(IReadOnlyList<UndisposeFormResult> Results, int Cleared, int NotRecorded);

/// <summary>One word type's disposition and its history.</summary>
/// <param name="Form">The form as it was asked for.</param>
/// <param name="Key">The word type key.</param>
/// <param name="Current">The standing disposition, or null when the type has none.</param>
/// <param name="History">Dispositions that no longer stand, newest first.</param>
/// <param name="Scope">Always <c>word-wide</c>: a disposition covers every occurrence of the type, and none is stored for one occurrence.</param>
public sealed record ShowDispositionResponse(
    string Form, string Key, WordTypeDisposition? Current, IReadOnlyList<EndedWordTypeDisposition> History, string Scope);

/// <summary>How many word types stand in one category.</summary>
/// <param name="Category">The wire name of the category.</param>
/// <param name="Types">How many word types stand in it.</param>
public sealed record DispositionCategoryCount(string Category, int Types);

/// <summary>A page of standing dispositions in word type key order.</summary>
/// <param name="Items">The dispositions on this page.</param>
/// <param name="Total">How many dispositions match, across every page.</param>
/// <param name="Page">The one-based page number.</param>
/// <param name="Pages">How many pages there are at this page size.</param>
/// <param name="Limit">The page size used.</param>
/// <param name="Category">The category asked for, or null for all.</param>
/// <param name="ByCategory">How many types stand in each category across the whole store, ignoring the category filter.</param>
public sealed record ListDispositionsResponse(
    IReadOnlyList<WordTypeDisposition> Items, int Total, int Page, int Pages, int Limit, string? Category,
    IReadOnlyList<DispositionCategoryCount> ByCategory);

/// <summary>A row of a <c>wordform dispose --from-file</c> call that was refused, with where it was.</summary>
/// <param name="Row">The one-based line number in the file, counting the header and blank lines.</param>
/// <param name="Form">The form on the row, or empty when the row gave none.</param>
/// <param name="Code">The refusal code, such as <c>disposition.invalid</c> or <c>disposition.contradiction</c>.</param>
/// <param name="Message">What is wrong with the row.</param>
public sealed record DisposeRowRefusal(int Row, string Form, string Code, string Message);

/// <summary>What one accepted row of a <c>wordform dispose --from-file</c> call came to.</summary>
/// <param name="Row">The one-based line number in the file.</param>
/// <param name="Form">The form as it was given.</param>
/// <param name="Key">The word type key.</param>
/// <param name="Category">The category asked for.</param>
/// <param name="Intended">The intended form of a misspelling, or null.</param>
/// <param name="Result"><c>recorded</c>, <c>replaced</c>, <c>already-recorded</c>, <c>evidence-added</c> or <c>refused</c>.</param>
public sealed record DisposeRowResult(int Row, string Form, string Key, string Category, string? Intended, string Result);

/// <summary>The outcome of one <c>wordform dispose --from-file</c> call: every row is written, or none is unless refused rows were skipped.</summary>
/// <param name="Format">The format the file was read as: <c>tsv</c>, <c>jsonl</c> or <c>ledger</c>.</param>
/// <param name="Rows">How many rows the file held that the call considered.</param>
/// <param name="Added">How many types gained a disposition.</param>
/// <param name="Replaced">How many types had another disposition replaced.</param>
/// <param name="Unchanged">How many types already stood as asked, or repeated an earlier row of the file.</param>
/// <param name="EvidenceExtended">How many of the unchanged types gained evidence.</param>
/// <param name="Refused">How many rows were refused.</param>
/// <param name="Skipped">How many ledger rows were not Disposition rows and were not considered.</param>
/// <param name="EvidenceTruncated">How many ledger rows had evidence cut to the longest a disposition holds.</param>
/// <param name="DryRun">Whether nothing was written because the call only reported.</param>
/// <param name="Written">Whether the store was changed.</param>
/// <param name="Refusals">Every refused row, in file order.</param>
/// <param name="Results">The result of every accepted and refused row; empty unless detail was asked for.</param>
public sealed record DisposeFromFileResponse(
    string Format, int Rows, int Added, int Replaced, int Unchanged, int EvidenceExtended, int Refused, int Skipped,
    int EvidenceTruncated, bool DryRun, bool Written, IReadOnlyList<DisposeRowRefusal> Refusals,
    IReadOnlyList<DisposeRowResult> Results);
