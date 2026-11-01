namespace SIL.Motif.Contract.Responses;

/// <summary>One grammar object PanGloss named in a finding that says it left the object out of the grammar.</summary>
/// <param name="MotifId">The object's id in Motif's portable form, the base64url form of its FieldWorks GUID.</param>
/// <param name="Kind">The FieldWorks class PanGloss reported for the object, such as <c>PhEnvironment</c>.</param>
/// <param name="Name">The object's name or form as PanGloss reported it, when it reported one.</param>
/// <param name="Field">The field of the object the finding is about, when PanGloss named one.</param>
public sealed record DroppedGrammarObject(string MotifId, string? Kind, string? Name, string? Field);

/// <summary>A grammar-health finding saying PanGloss continued after dropping part of the grammar it was given.</summary>
/// <param name="Code">PanGloss's stable finding code, such as <c>grammar.environment.invalid</c>.</param>
/// <param name="Objects">The objects the finding names; empty when PanGloss named none with an identity.</param>
/// <param name="Message">PanGloss's description of what it dropped and why.</param>
/// <param name="Listed">
/// True for a code on Motif's closed list of dropped-grammar codes, whose wording is specific; false for any other
/// warning PanGloss raised about named grammar objects, which Motif reports generically rather than swallow.
/// </param>
public sealed record DroppedGrammarFinding(string Code, IReadOnlyList<DroppedGrammarObject> Objects, string Message,
    bool Listed = true);

/// <summary>
/// What a Trial's candidate grammar lost without refusing: pieces PanGloss dropped with only a warning, so the
/// parse looks fine while the grammar is weaker than the Proposal says. A Trial is clean only when the check ran and
/// the Proposal introduced none of them.
/// </summary>
/// <param name="Introduced">
/// The findings the Proposal introduced: absent from the Baseline's own check, or naming an object the Proposal
/// creates or edits.
/// </param>
/// <param name="PreExisting">How many findings the Baseline's grammar already had, which do not make a Trial unclean.</param>
public sealed record DroppedGrammar(
    IReadOnlyList<DroppedGrammarFinding> Introduced,
    int PreExisting)
{
    /// <summary>
    /// How many introduced findings were left out of <see cref="Introduced"/> to keep the stored result small; the
    /// first <see cref="MaxFindings"/> are kept, in the order PanGloss reported them.
    /// </summary>
    public int OmittedFindings { get; init; }

    /// <summary>The most introduced findings a result keeps, so a Trial's stored result stays a few kilobytes.</summary>
    public const int MaxFindings = 25;

    /// <summary>False when the check did not run, so nothing is known about dropped grammar; never clean.</summary>
    public bool Checked { get; init; } = true;

    /// <summary>Why the check did not run, when <see cref="Checked"/> is false.</summary>
    public string? NotCheckedReason { get; init; }

    /// <summary>A result for a check that did not run, carrying why.</summary>
    public static DroppedGrammar NotChecked(string reason) =>
        new([], 0) { Checked = false, NotCheckedReason = reason };

    /// <summary>Whether the check ran and the Proposal introduced nothing PanGloss dropped.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsClean => Checked && Introduced.Count == 0;
}
