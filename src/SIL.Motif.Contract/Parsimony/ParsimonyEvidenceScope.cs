using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Parsimony;

/// <summary>The closed word scope a Parsimony measure may name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParsimonyEvidenceScopeKind>))]
public enum ParsimonyEvidenceScopeKind
{
    /// <summary>The project's saved Default Selection, resolved to exact words and Text identities.</summary>
    [JsonStringEnumMemberName("default-selection")]
    DefaultSelection,

    /// <summary>Every project wordform with an Approved or Disapproved analysis.</summary>
    [JsonStringEnumMemberName("project-approved")]
    ProjectApproved,
}

/// <summary>The exact saved Default Selection frozen into a Parsimony job.</summary>
/// <param name="Name">The saved Selection name.</param>
/// <param name="TextIds">The chosen Text identities.</param>
/// <param name="AddedWords">The saved typed words, without FieldWorks identities.</param>
/// <param name="ResolvedWords">The exact normalized word list measured by the Selection.</param>
/// <param name="SelectionSha256">The Selection digest with its <c>sha256:</c> prefix.</param>
public sealed record ParsimonySelectionSnapshot(
    string Name,
    IReadOnlyList<Guid> TextIds,
    IReadOnlyList<string> AddedWords,
    IReadOnlyList<string> ResolvedWords,
    string SelectionSha256);

/// <summary>The evidence scope and any saved Selection resolved when the job was queued.</summary>
/// <param name="Kind">The requested scope.</param>
/// <param name="Selection">The frozen Default Selection, or null when none was available.</param>
public sealed record ParsimonyScopeBinding(
    ParsimonyEvidenceScopeKind Kind,
    ParsimonySelectionSnapshot? Selection);
