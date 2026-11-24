using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Query;

/// <summary>A typed reason a variant set is not exhaustive or not exact, so an agent can show why before trusting a negation.</summary>
/// <param name="Code">The reason, such as <c>unwritten_variant</c>.</param>
/// <param name="Detail">One sentence saying what it means here; null when the code says it all.</param>
public sealed record RealizesReason(string Code, string? Detail = null);

/// <summary>One surface form of the morpheme and the evidence for it.</summary>
/// <param name="Form">The surface string, NFC.</param>
/// <param name="Tier">The weakest tier that supports the form: <c>attested</c> here.</param>
/// <param name="Source">Where the form came from, comma-joined when several agree: <c>lexeme-form</c> or <c>alternate-form</c> from stored analyses, <c>corpus-parse</c> from parsing the corpus, <c>corpus-parse-whole-word</c> when the parse gave no surface piece and the whole word stands in.</param>
/// <param name="Words">How many word types, summed over the sources, have an analysis that cuts this form for the morpheme.</param>
/// <param name="Examples">A few of those words, by spelling.</param>
public sealed record RealizesForm(string Form, string Tier, string Source, int Words, IReadOnlyList<string> Examples);

/// <summary>The search a variant set compiles to.</summary>
/// <param name="Kind"><c>regex</c>: a .NET non-backtracking expression over the surface form.</param>
/// <param name="Text">The expression, unanchored in the engine's sense; <paramref name="Anchor"/> is already written into it.</param>
/// <param name="Anchor"><c>start</c> for a prefix, <c>end</c> for a suffix, <c>none</c> for a stem or an infix.</param>
/// <param name="Segmentation"><c>approximate</c>: the expression reads characters, not the grammar's segments, so a form may also match across a digraph.</param>
public sealed record RealizesPattern(string Kind, string Text, string Anchor, string Segmentation);

/// <summary>What parsing the corpus's words to find a morpheme came to, so a reader sees how much of the corpus the set rests on.</summary>
/// <param name="Types">Word types in the corpus.</param>
/// <param name="Parsed">Word types the batch parse answered for.</param>
/// <param name="WithMorpheme">Parsed words with an analysis that contains the morpheme.</param>
/// <param name="NoAnalysis">Parsed words the grammar gave no analysis.</param>
/// <param name="Unsegmentable">Words PanGloss could not segment with the grammar's characters.</param>
/// <param name="Capped">Words whose parse stopped at the attempt or work limit.</param>
/// <param name="TimedOut">Words whose parse stopped at the word timeout.</param>
/// <param name="Failed">Words the resident parser did not answer.</param>
/// <param name="Sample">The cap on traced matches asked for; null when every match was traced.</param>
/// <param name="Seconds">Wall-clock seconds the index and the traces took.</param>
/// <param name="Traced">Matching words traced for the morpheme's surface piece.</param>
/// <param name="IndexBuilt">Whether this query built the parse index; false when a cached one was reused.</param>
/// <param name="IndexSeconds">Seconds to build or load the parse index.</param>
public sealed record RealizesParseReport(int Types, int Parsed, int WithMorpheme, int NoAnalysis, int Unsegmentable, int Capped, int TimedOut,
    int Failed, int? Sample, double Seconds, int Traced = 0, bool IndexBuilt = false, double IndexSeconds = 0);

/// <summary>A morpheme's variant set at one tier, versioned <see cref="SchemaName"/>.</summary>
/// <param name="Schema">The schema name.</param>
/// <param name="Morpheme">The morpheme as resolved: its form and its entry:msa id.</param>
/// <param name="Tier">The tier asked for.</param>
/// <param name="Complete">Whether the set is exhaustive for its tier; false means an absence is not proven, and negation is refused.</param>
/// <param name="Reasons">Why the set is incomplete or approximate; empty when complete.</param>
/// <param name="StopReason">Why a tier that enumerates stopped early; null for <c>attested</c>.</param>
/// <param name="Forms">The surface forms searched for.</param>
/// <param name="Pattern">The compiled search.</param>
/// <param name="Parse">The corpus parse the set rests on; null when only stored analyses were read.</param>
public sealed record RealizesResult(string Schema, string Morpheme, string Tier, bool Complete, IReadOnlyList<RealizesReason> Reasons,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? StopReason,
    IReadOnlyList<RealizesForm> Forms, RealizesPattern Pattern,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RealizesParseReport? Parse = null)
{
    /// <summary>The schema name of the JSON form.</summary>
    public const string SchemaName = "motif.realizes.v1";
}

/// <summary>The forms of one variant set that a hit contains, so a reader sees why the word matched.</summary>
/// <param name="Morpheme">The morpheme as resolved.</param>
/// <param name="Tier">The tier of the set.</param>
/// <param name="Forms">The set's forms the word carries at the anchor.</param>
public sealed record RealizesHit(string Morpheme, string Tier, IReadOnlyList<string> Forms);
