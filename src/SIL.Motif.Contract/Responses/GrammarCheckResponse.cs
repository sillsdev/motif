using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>What the parser reports about a project's grammar as a whole, including import findings.</summary>
/// <param name="Findings">The grammar-health report findings.</param>
/// <param name="HasBaseline">False when the project has no Baseline yet, so there was no grammar to read.</param>
public sealed record GrammarCheckResponse(IReadOnlyList<GrammarWarning> Findings, bool HasBaseline)
{
    public string Locale { get; init; } = string.Empty;

    /// <summary>The number of lexical entries in the Baseline checked by PanGloss, or null when its inventory is unknown.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? CheckedLexemeCount { get; init; }

    /// <summary>The number of grammar rules in the Baseline checked by PanGloss, or null when its inventory is unknown.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? CheckedRuleCount { get; init; }

    /// <summary>Motif advisories about the checked Baseline, separate from PanGloss findings.</summary>
    public IReadOnlyList<GrammarCheckAdvisory> Advisories { get; init; } = [];

    /// <summary>The parser's per-code summary rows, used to group findings for display.</summary>
    public IReadOnlyList<GrammarWarningSummary> Summary { get; init; } = [];
}

/// <summary>A Motif observation about a completed grammar check that is not a parser finding.</summary>
public sealed record GrammarCheckAdvisory(string Code, string Message)
{
    /// <summary>The Baseline's lexical entry count when it is known.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? LexemeCount { get; init; }

    /// <summary>The Baseline's stem lexeme count when it is known.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? StemLexemeCount { get; init; }
}

/// <summary>The stable codes Motif uses for grammar-check advisories.</summary>
public static class GrammarCheckAdvisoryCodes
{
    /// <summary>The Baseline contains no lexical entries or stem lexemes.</summary>
    public const string EmptyLexicon = "grammar.empty-lexicon";
}
