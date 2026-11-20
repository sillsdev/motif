using SIL.Motif.Contract.Dispositions;

namespace SIL.Motif.Contract.Responses;

/// <summary>What one form of a <c>wordform dispose</c> call came to.</summary>
/// <param name="Form">The form as it was given.</param>
/// <param name="Key">The word type key the form was recorded under.</param>
/// <param name="Result"><c>recorded</c>, <c>replaced</c> (another disposition ended and this one took its place) or <c>already-recorded</c> (the type already stands with this category and intended form, and nothing was written).</param>
/// <param name="Disposition">The disposition that stands for the type after the call.</param>
public sealed record DisposeFormResult(string Form, string Key, string Result, WordTypeDisposition Disposition);

/// <summary>The outcome of one <c>wordform dispose</c> call, which writes every form or none.</summary>
/// <param name="Results">One entry per distinct word type, in the order first given.</param>
/// <param name="Recorded">How many types gained a disposition.</param>
/// <param name="Replaced">How many types had another disposition replaced.</param>
/// <param name="AlreadyRecorded">How many types already stood as asked and were left alone.</param>
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
