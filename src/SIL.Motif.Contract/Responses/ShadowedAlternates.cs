using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// What PanGloss's shadowed-alternates report says about a grammar: alternates of an entry or affix rule that no word
/// can select because an earlier alternate is eligible wherever they are (HermitCrab takes the first eligible one), and
/// alternates that are selected but that no following affix accepts. Advisory only.
/// </summary>
/// <param name="Complete">Whether PanGloss analysed every morpheme with two or more alternates.</param>
/// <param name="Alternates">Shadowing rows, definite and maybe; unshadowed alternates are not listed.</param>
/// <param name="DeadContinuations">Rows for alternates that are selected, then rejected at every following affix.</param>
public sealed record ShadowedAlternatesReport(
    bool Complete, IReadOnlyList<ShadowedAlternate> Alternates,
    IReadOnlyList<DeadContinuationAlternate> DeadContinuations)
{
    /// <summary>The proven rows, shown as a warning.</summary>
    [JsonIgnore]
    public IEnumerable<ShadowedAlternate> Definite => Alternates.Where(row => row.Tier == ShadowedAlternateTiers.Definite);

    /// <summary>The rows PanGloss could not prove, shown as a softer note.</summary>
    [JsonIgnore]
    public IEnumerable<ShadowedAlternate> Maybe => Alternates.Where(row => row.Tier == ShadowedAlternateTiers.Maybe);
}

/// <summary>The two certainties of a shadowed-alternate or dead-continuation row.</summary>
public static class ShadowedAlternateTiers
{
    /// <summary>PanGloss proved it (<c>shadowed</c> or <c>proven</c>): a warning.</summary>
    public const string Definite = "definite";

    /// <summary>PanGloss could not decide (<c>maybe_shadowed</c> or <c>maybe</c>): a softer note.</summary>
    public const string Maybe = "maybe";
}

/// <summary>One alternate PanGloss reports as shadowed, or possibly shadowed.</summary>
/// <param name="Tier">A <see cref="ShadowedAlternateTiers"/> value.</param>
/// <param name="Kind"><c>entry</c> for a lexical-entry alternate or <c>rule</c> for an affix-rule alternate.</param>
/// <param name="Owner">The entry or affix rule that has the alternates.</param>
/// <param name="Form">The alternate's form, or null when it has none (an affix process).</param>
/// <param name="ShadowedBy">The earlier alternates responsible, by form or, without one, by id.</param>
/// <param name="Reasons">PanGloss's reason codes, such as <c>unconstrained_earlier</c> or <c>lhs_subsumption_undecided</c>.</param>
/// <param name="SourceAllomorphGuids">The FieldWorks allomorph guids this row stands for.</param>
/// <param name="RemovableGuids">The guids PanGloss proved safe to remove; empty for a maybe row.</param>
public sealed record ShadowedAlternate(
    string Tier, string Kind, string Owner, string? Form, IReadOnlyList<string> ShadowedBy,
    IReadOnlyList<string> Reasons, IReadOnlyList<string> SourceAllomorphGuids, IReadOnlyList<string> RemovableGuids)
{
    /// <summary>The owning entry's guid when PanGloss records one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EntryGuid { get; init; }

    /// <summary>The owning morphosyntactic analysis's guid when PanGloss records one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MsaGuid { get; init; }

    /// <summary>The alternate as a person reads it: <c>entry eA 'ba'</c>.</summary>
    [JsonIgnore]
    public string Label => $"{(Kind == "rule" ? "affix rule" : "entry")} {Owner}" + (Form is { Length: > 0 } ? $" '{Form}'" : " (an alternate without a form)");
}

/// <summary>One alternate PanGloss reports as selected but rejected by every following affix.</summary>
/// <param name="Tier">A <see cref="ShadowedAlternateTiers"/> value.</param>
/// <param name="Owner">The entry that has the alternate.</param>
/// <param name="Form">The alternate's form, or null.</param>
/// <param name="Reasons">PanGloss's reason codes.</param>
/// <param name="BlockedBy">What the following affixes reject it on, such as <c>required_mpr VC-B</c>.</param>
/// <param name="SourceAllomorphGuids">The FieldWorks allomorph guids this row stands for.</param>
public sealed record DeadContinuationAlternate(
    string Tier, string Owner, string? Form, IReadOnlyList<string> Reasons, IReadOnlyList<string> BlockedBy,
    IReadOnlyList<string> SourceAllomorphGuids)
{
    /// <summary>The alternate as a person reads it: <c>entry eA 'ba'</c>.</summary>
    [JsonIgnore]
    public string Label => $"entry {Owner}" + (Form is { Length: > 0 } ? $" '{Form}'" : "");
}
