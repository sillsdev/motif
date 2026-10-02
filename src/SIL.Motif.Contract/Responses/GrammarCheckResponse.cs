using System.Collections.Generic;

namespace SIL.Motif.Contract.Responses;

/// <summary>What the parser reports about a project's grammar as a whole, including import findings.</summary>
/// <param name="Findings">The grammar-health report findings.</param>
/// <param name="HasBaseline">False when the project has no Baseline yet, so there was no grammar to read.</param>
public sealed record GrammarCheckResponse(IReadOnlyList<GrammarWarning> Findings, bool HasBaseline)
{
    public string Locale { get; init; } = string.Empty;

    /// <summary>The parser's per-code summary rows, used to group findings for display.</summary>
    public IReadOnlyList<GrammarWarningSummary> Summary { get; init; } = [];
}
