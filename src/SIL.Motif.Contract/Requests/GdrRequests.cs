using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Requests;

/// <summary>Lists the Grammar Guide in the current Baseline.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="ExpectedBaseline">The required Baseline identity; null accepts the current Baseline.</param>
public sealed record GdrListRequest(string ProjectPath, BaselineToken? ExpectedBaseline = null);

/// <summary>Reads one Grammar Guide section's Discussion from the current Baseline.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Id">The record's <c>GDR-n</c> abbreviation or its canonical id.</param>
/// <param name="ExpectedBaseline">The required Baseline identity; null accepts the current Baseline.</param>
/// <param name="AsOf">A Draft name or Proposal id whose edits of this section are shown as if applied; null shows the Baseline.</param>
public sealed record GdrShowRequest(string ProjectPath, string Id, BaselineToken? ExpectedBaseline = null, string? AsOf = null);

/// <summary>Stages an edit of one Grammar Guide section, given as FieldWorks Markdown, in a Draft.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Id">The record's <c>GDR-n</c> abbreviation or its canonical id.</param>
/// <param name="Markdown">The whole record as <c>guide show --format fw-md</c> prints it, or the Discussion body alone.</param>
/// <param name="Draft">The name of an existing Draft to stage into; null stages into the pending changes.</param>
public sealed record GdrEditRequest(string ProjectPath, string Id, string Markdown, string? Draft = null);

/// <summary>Stages a new Grammar Guide section or part, with an optional body, in a Draft (ADR 0005).</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Title">The new item's title, plain text.</param>
/// <param name="Parent">The <c>GDR-n</c> number or canonical id of the part or section it goes under; null for the top of the list.</param>
/// <param name="After">The <c>GDR-n</c> number or canonical id of the sibling it follows; null puts it last under its parent.</param>
/// <param name="Markdown">The Discussion as FieldWorks Markdown, or null for an empty body.</param>
/// <param name="WritingSystem">The title's writing system tag; null uses the project's default analysis writing system.</param>
/// <param name="Draft">The name of an existing Draft to stage into; null stages into the pending changes.</param>
public sealed record GdrAddRequest(string ProjectPath, string Title, string? Parent = null, string? After = null,
    string? Markdown = null, string? WritingSystem = null, string? Draft = null);

/// <summary>Stages one judgement call, "A or B, I am choosing A, because ...", under a section's Decisions heading.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Id">The record's <c>GDR-n</c> abbreviation or its canonical id.</param>
/// <param name="Choosing">The option taken.</param>
/// <param name="Over">The option not taken.</param>
/// <param name="Because">The reason, in one line.</param>
/// <param name="Draft">The name of an existing Draft to stage into; null stages into the pending changes.</param>
public sealed record GdrDecideRequest(string ProjectPath, string Id, string Choosing, string Over, string Because, string? Draft = null);

/// <summary>Finds the object at a project path in the current Baseline, for a link to it.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Path">The FieldWorks Markdown project path, such as <c>/lexicon/entry-1</c>.</param>
/// <param name="ExpectedBaseline">The required Baseline identity, such as the one the linking record was read from; null accepts the current Baseline.</param>
public sealed record GdrLinkToPathRequest(string ProjectPath, string Path, BaselineToken? ExpectedBaseline = null);

/// <summary>
/// Checks that the Grammar Guide are in sync with the grammar in the current Baseline (ADR 0005): every rule and
/// affix process has a section, no section cites a deleted object, and every example still parses as its section says
/// by the latest Trial or Assessment rows already stored.
/// </summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="ExpectedBaseline">The required Baseline identity; null accepts the current Baseline.</param>
/// <param name="RunBlocks">Whether to run the Guide's <c>motif-query</c> blocks read-only; false lists them as skipped.</param>
/// <param name="BlockTimeoutSeconds">The longest one block may run before it is reported as failed.</param>
/// <param name="BuildIndex">Whether to build the corpus parse index a block needs when it is missing or stale (once per distinct shape); the timeout then bounds the query, not the build.</param>
public sealed record GdrCheckRequest(string ProjectPath, BaselineToken? ExpectedBaseline = null, bool RunBlocks = true, int BlockTimeoutSeconds = 60,
    bool BuildIndex = false);


/// <summary>Stages the saved tables of a Grammar Guide section's query blocks, or of every section's, in a Draft (ADR 0005).</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Id">The <c>GDR-n</c> number or canonical id of one section; null refreshes every section that has a query block.</param>
/// <param name="Draft">The name of an existing Draft to stage into; null stages into the pending changes.</param>
/// <param name="QueryId">The <c>id</c> of one query block to refresh; null refreshes every block of the section.</param>
public sealed record GdrRefreshTablesRequest(string ProjectPath, string? Id = null, string? Draft = null, string? QueryId = null);
