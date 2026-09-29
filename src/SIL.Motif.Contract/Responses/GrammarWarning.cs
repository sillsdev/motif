using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>A grammar-health diagnostic with the description and subjects supplied by PanGloss.</summary>
/// <param name="Severity">The diagnostic level: <c>warning</c> or <c>info</c>.</param>
/// <param name="CodeLabel">A readable name for the diagnostic code.</param>
/// <param name="Subject">The named FieldWorks subjects, including their reported link state.</param>
/// <param name="Problem">The report description, represented as display parts.</param>
/// <param name="Text">A readable form of the level, code, and description.</param>
public sealed record GrammarWarning(
    GrammarDiagnosticLevel Severity,
    [property: JsonPropertyName("kind")] string CodeLabel,
    IReadOnlyList<GrammarWarningPart> Subject,
    IReadOnlyList<GrammarWarningPart> Problem,
    string Text)
{
    /// <summary>The report's human-readable name for this diagnostic code.</summary>
    public string? Group { get; init; }

    /// <summary>The report's stable diagnostic code.</summary>
    public string? Code { get; init; }

    /// <summary>The complete description supplied by PanGloss.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>What a person can do in FieldWorks to address the diagnostic.</summary>
    public string? Guidance { get; init; }

    /// <summary>Whether the finding came from checking the grammar or importing it.</summary>
    public GrammarFindingOrigin Origin { get; init; }
}

/// <summary>One summary row grouping diagnostics by their stable code.</summary>
/// <param name="Code">The diagnostic code used to match this row to its findings.</param>
/// <param name="GroupName">The name shown for this kind of diagnostic.</param>
/// <param name="Level">The report level for this kind of diagnostic.</param>
/// <param name="Count">How many diagnostics of this kind the report contains.</param>
public sealed record GrammarWarningSummary(string Code, string? GroupName, GrammarDiagnosticLevel Level, int Count);

/// <summary>A named subject in a grammar-health diagnostic.</summary>
/// <param name="Text">The human-readable title and subtitle shown for the subject.</param>
/// <param name="Role">Whether the part is prose, a value, or a named FieldWorks subject.</param>
/// <param name="ObjectId">The subject GUID or internal identity.</param>
/// <param name="FieldWorksKind">The FieldWorks class name reported by PanGloss.</param>
/// <param name="FieldWorksLink">The report's FieldWorks URL when one is available.</param>
public sealed record GrammarWarningPart(
    string Text,
    GrammarWarningPartRole Role,
    string? ObjectId = null,
    [property: JsonPropertyName("kind")] string? FieldWorksKind = null,
    string? FieldWorksLink = null)
{
    /// <summary>The subject's title exactly as PanGloss reported it.</summary>
    public string? Title { get; init; }

    /// <summary>The subject's subtitle exactly as PanGloss reported it.</summary>
    public string? Subtitle { get; init; }

    /// <summary>The subject's FieldWorks GUID, if PanGloss recorded one.</summary>
    public string? SubjectGuid { get; init; }

    /// <summary>The report's internal identity for the subject, when available.</summary>
    public string? InternalId { get; init; }

    /// <summary>The GUID targeted by the FieldWorks link.</summary>
    public string? FieldWorksGuid { get; init; }

    /// <summary>The FieldWorks link state reported by PanGloss.</summary>
    public FieldWorksLinkStatus? LinkStatus { get; init; }

    /// <summary>The reason PanGloss gave when the link is unavailable.</summary>
    public FieldWorksLinkReason? LinkReason { get; init; }

    /// <summary>The FieldWorks tool identifier reported for an available link.</summary>
    public string? FieldWorksTool { get; init; }

}
