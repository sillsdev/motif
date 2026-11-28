using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>What the parser reports about a project's grammar as a whole, including import findings.</summary>
/// <param name="Findings">The grammar-health report findings.</param>
/// <param name="HasBaseline">False when the project has no Baseline yet, so there was no grammar to read.</param>
public sealed record GrammarCheckResponse(IReadOnlyList<GrammarWarning> Findings, bool HasBaseline)
{
    /// <summary>Whether optional FST capability inspection completed.</summary>
    public bool FstChecked { get; init; }

    /// <summary>The producer's reason FST inspection did not complete, when supplied.</summary>
    public string? FstCheckError { get; init; }

    /// <summary>Completeness of per-word object timing detail used by this result.</summary>
    public ObjectTimingDetail? ObjectTimingDetail { get; init; }

    public string Locale { get; init; } = string.Empty;

    /// <summary>The number of lexical entries in the Baseline checked by PanGloss, or null when its inventory is unknown.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? CheckedLexemeCount { get; init; }

    /// <summary>The number of grammar rules in the Baseline checked by PanGloss, or null when its inventory is unknown.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? CheckedRuleCount { get; init; }

    /// <summary>Motif advisories about the checked Baseline, separate from PanGloss findings.</summary>
    public IReadOnlyList<GrammarCheckAdvisory> Advisories { get; init; } = [];

    /// <summary>
    /// PanGloss's shadowed-alternates report for this grammar, or null when it was not asked for. When it was asked for but
    /// the installed PanGloss cannot produce it, the report says so instead of the check failing.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShadowedAlternatesReport? ShadowedAlternates { get; init; }

    /// <summary>
    /// The word types of the Baseline's matching Assessment that have more than one reading, grouped by morpheme
    /// pattern; null when the Baseline has no matching Assessment to count. Report-only, with no cap.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MultiReadingTypes? MultiReadingTypes { get; init; }

    /// <summary>The parser's per-code summary rows, used to group findings for display.</summary>
    public IReadOnlyList<GrammarWarningSummary> Summary { get; init; } = [];

    /// <summary>The exact Baseline, Selection, parser, source-save time, and check time recorded with these findings.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public GrammarCheckProvenance? Provenance { get; init; }

    /// <summary>The findings of every level, when <see cref="Findings"/> is one page of them; null when it is all of them.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FindingsTotal { get; init; }

    /// <summary>The error-level findings among all of them, when <see cref="Findings"/> is one page; null otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ErrorTotal { get; init; }

    /// <summary>The warning-level findings among all of them, when <see cref="Findings"/> is one page; null otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WarningTotal { get; init; }

    /// <summary>The information-level findings among all of them, when <see cref="Findings"/> is one page; null otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? InformationTotal { get; init; }

    /// <summary>How many findings precede this page; null when <see cref="Findings"/> is not a page.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FindingsOffset { get; init; }

    /// <summary>The offset that starts the next page of findings; null at the end or when this is not a page.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? NextOffset { get; init; }

    /// <summary>The command that reads the next page with the caller's own flags; null at the end or when not set.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextStep { get; init; }

    /// <summary>This page with the command for the next one, built from its offset; unchanged at the end.</summary>
    public GrammarCheckResponse WithNextStep(System.Func<int, string> step) =>
        NextOffset is { } next ? this with { NextStep = step(next) } : this;

    /// <summary>One page of the findings, keeping the per-level totals of all of them.</summary>
    public GrammarCheckResponse Window(int offset, int limit)
    {
        var ordered = Findings.OrderBy(finding => finding.Severity).ToList();
        var page = ResultPaging.Window(ordered, offset, limit);
        return this with
        {
            Findings = page, FindingsTotal = Findings.Count, FindingsOffset = offset,
            ErrorTotal = Findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Error),
            WarningTotal = Findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Warning),
            InformationTotal = Findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Information),
            NextOffset = ResultPaging.NextOffset(offset, page.Count, ordered.Count),
        };
    }
}

/// <summary>The word types that read more than one way, by morpheme pattern.</summary>
/// <param name="MeasuredTypes">The word types whose readings were counted.</param>
/// <param name="Types">The measured word types with more than one reading, the sum of the groups.</param>
/// <param name="Groups">One group per morpheme pattern, largest first.</param>
public sealed record MultiReadingTypes(int MeasuredTypes, int Types, IReadOnlyList<MultiReadingGroup> Groups);

/// <summary>The word types whose readings share one morpheme pattern.</summary>
/// <param name="Pattern">
/// Each distinct reading as its morphs joined by <c>+</c> (an affix by its gloss, a stem as <c>stem</c>), the
/// readings joined by <c> | </c>, as <c>stem+APPL+CAUS | stem+CAUS+APPL</c>.
/// </param>
/// <param name="Types">How many word types have this pattern.</param>
/// <param name="Examples">Up to five of them, in alphabetical order.</param>
public sealed record MultiReadingGroup(string Pattern, int Types, IReadOnlyList<string> Examples);

/// <summary>A Motif observation about a completed grammar check that is not a parser finding.</summary>
public sealed record GrammarCheckAdvisory(string Code, string Message)
{
    /// <summary>The Baseline's lexical entry count when it is known.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? LexemeCount { get; init; }

    /// <summary>The Baseline's stem lexeme count when it is known.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? StemLexemeCount { get; init; }

    /// <summary>The grammar objects this advisory is about, by id and text; null when it is about the Baseline as a whole.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<GrammarCheckAdvisoryObject>? Objects { get; init; }
}

/// <summary>One grammar object a <see cref="GrammarCheckAdvisory"/> is about.</summary>
/// <param name="Id">The object's canonical id.</param>
/// <param name="Text">The object's text: an environment's pattern, an inflection class's name.</param>
public sealed record GrammarCheckAdvisoryObject(string Id, string Text);

/// <summary>The stable codes Motif uses for grammar-check advisories.</summary>
public static class GrammarCheckAdvisoryCodes
{
    /// <summary>The Baseline contains no lexical entries or stem lexemes.</summary>
    public const string EmptyLexicon = "grammar.empty-lexicon";

    /// <summary>An environment that no allomorph references.</summary>
    public const string OrphanEnvironment = "grammar.orphan-environment";
}
