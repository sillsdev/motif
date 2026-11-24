using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Query;

/// <summary>A typed reason a variant set is not exhaustive or not exact, so an agent can show why before trusting a negation.</summary>
/// <param name="Code">The reason, such as <c>unwritten_variant</c>.</param>
/// <param name="Detail">One sentence saying what it means here; null when the code says it all.</param>
public sealed record RealizesReason(string Code, string? Detail = null);

/// <summary>One surface form of the morpheme and the evidence for it.</summary>
/// <param name="Form">The surface string, NFC.</param>
/// <param name="Tier">The weakest tier that supports the form: <c>attested</c> here.</param>
/// <param name="Source"><c>lexeme-form</c> when the analysis used the entry's lexeme form, <c>alternate-form</c> for an allomorph of it.</param>
/// <param name="Words">How many word types of the source have an analysis that cuts this form for the morpheme.</param>
/// <param name="Examples">A few of those words, by spelling.</param>
public sealed record RealizesForm(string Form, string Tier, string Source, int Words, IReadOnlyList<string> Examples);

/// <summary>The search a variant set compiles to.</summary>
/// <param name="Kind"><c>regex</c>: a .NET non-backtracking expression over the surface form.</param>
/// <param name="Text">The expression, unanchored in the engine's sense; <paramref name="Anchor"/> is already written into it.</param>
/// <param name="Anchor"><c>start</c> for a prefix, <c>end</c> for a suffix, <c>none</c> for a stem or an infix.</param>
/// <param name="Segmentation"><c>approximate</c>: the expression reads characters, not the grammar's segments, so a form may also match across a digraph.</param>
public sealed record RealizesPattern(string Kind, string Text, string Anchor, string Segmentation);

/// <summary>A morpheme's variant set at one tier, versioned <see cref="SchemaName"/>.</summary>
/// <param name="Schema">The schema name.</param>
/// <param name="Morpheme">The morpheme as resolved: its form and its entry:msa id.</param>
/// <param name="Tier">The tier asked for.</param>
/// <param name="Complete">Whether the set is exhaustive for its tier; false means an absence is not proven, and negation is refused.</param>
/// <param name="Reasons">Why the set is incomplete or approximate; empty when complete.</param>
/// <param name="StopReason">Why a tier that enumerates stopped early; null for <c>attested</c>.</param>
/// <param name="Forms">The surface forms searched for.</param>
/// <param name="Pattern">The compiled search.</param>
public sealed record RealizesResult(string Schema, string Morpheme, string Tier, bool Complete, IReadOnlyList<RealizesReason> Reasons,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? StopReason,
    IReadOnlyList<RealizesForm> Forms, RealizesPattern Pattern)
{
    /// <summary>The schema name of the JSON form.</summary>
    public const string SchemaName = "motif.realizes.v1";
}

/// <summary>The forms of one variant set that a hit contains, so a reader sees why the word matched.</summary>
/// <param name="Morpheme">The morpheme as resolved.</param>
/// <param name="Tier">The tier of the set.</param>
/// <param name="Forms">The set's forms the word carries at the anchor.</param>
public sealed record RealizesHit(string Morpheme, string Tier, IReadOnlyList<string> Forms);
