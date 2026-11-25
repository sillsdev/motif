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
/// <param name="Rules">The rules that make this form, by name, for a <c>possible</c> or <c>generated</c> form.</param>
/// <param name="TestRoot">The real neighbour the form was generated on, as <c>form (entry:msa)</c>, for a <c>generated</c> form.</param>
/// <param name="Surface">The whole word PanGloss generated, which the form is the morpheme's piece of.</param>
/// <param name="PieceSource"><c>morphSpans</c> when the engine placed the morpheme in the word, or <c>whole-word</c> when it could not and the word stands in.</param>
/// <param name="Trace">The rule steps of the derivation, each <c>rule: input -> output</c>.</param>
/// <param name="Cell">The environment cell the test root was chosen for, <c>rule/edge</c>, or <c>control</c>.</param>
public sealed record RealizesForm(string Form, string Tier, string Source, int Words, IReadOnlyList<string> Examples,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Rules = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TestRoot = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Surface = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PieceSource = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Trace = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Cell = null);

/// <summary>A phonological rule that may change the morpheme, with the environment PanGloss reports for it.</summary>
/// <param name="Id">The rule's id in the grammar.</param>
/// <param name="Name">The rule's name.</param>
/// <param name="Subrule">The subrule's index, or null for a metathesis rule.</param>
/// <param name="Edges">The edges of the morpheme the rule was scanned at, <c>left</c> and <c>right</c>.</param>
/// <param name="Environment">The rule's environment, <c>left _ right</c>; null when PanGloss could not print it.</param>
/// <param name="MatchPattern">The rule as a <c>match</c> pattern, <c>target / environment</c>; null when it could not be printed.</param>
/// <param name="Rhs">The replacement alternative; a class or bundle is a feature change, not any member.</param>
/// <param name="Verdict"><c>unknown/</c> and the reason PanGloss gave, such as <c>unknown/environment_unchecked</c>.</param>
/// <param name="NonLocal">Whether the rule may be non-local, so a search of its match pattern may miss a rewritten position.</param>
public sealed record RealizesRule(string Id, string Name, int? Subrule, IReadOnlyList<string> Edges, string? Environment, string? MatchPattern,
    string? Rhs, string Verdict, bool NonLocal);

/// <summary>A corpus search over a rule's match pattern, narrowed to the morpheme's own context.</summary>
/// <param name="Pattern">The rule as a <c>match</c> pattern, run through the same engine as <c>phon</c>.</param>
/// <param name="Context">A regular expression over the surface form that a word must also match, the morpheme with the rule's target widened; null for none.</param>
/// <param name="Rule">The rule's name.</param>
/// <param name="Side"><c>output</c>: the rule's environment with its target replaced by what it writes, which finds words the rule has changed; <c>input</c>: where the rule could apply, which finds words it has not changed yet.</param>
public sealed record RealizesPhonClause(string Pattern, string? Context, string Rule, string Side = "output");

/// <summary>What the effects scan said, so a reader sees how many rules the set rests on.</summary>
/// <param name="Rules">Rules that may change the morpheme: every row PanGloss could not rule out, counted once per rule and subrule.</param>
/// <param name="NotOnForm">Rules whose target is not on the morpheme, so they are out.</param>
/// <param name="Unmodelled">Rules that PanGloss could not model, such as an underspecified segment.</param>
/// <param name="Seconds">Wall-clock seconds the effects call took; zero when not measured.</param>
/// <param name="UnmodelledBy">Rules that could not be modelled, by PanGloss's reason, including <c>lhs_may_straddle_edge</c> and <c>may_be_fed</c>; null when none.</param>
public sealed record RealizesEffectsSummary(int Rules, int NotOnForm, int Unmodelled, double Seconds = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, int>? UnmodelledBy = null);

/// <summary>The search a variant set compiles to.</summary>
/// <param name="Kind"><c>regex</c>: a .NET non-backtracking expression over the surface form.</param>
/// <param name="Text">The expression, unanchored in the engine's sense; <paramref name="Anchor"/> is already written into it.</param>
/// <param name="Anchor"><c>start</c> for a prefix, <c>end</c> for a suffix, <c>none</c> for a stem or an infix, <c>context</c> when each term carries its own anchor and attested context.</param>
/// <param name="Segmentation"><c>approximate</c>: the expression reads characters, not the grammar's segments, so a form may also match across a digraph.</param>
/// <param name="OverBroad">Whether one term matches more than a fifth of the corpus's word types, so the search finds more than the morpheme.</param>
/// <param name="Broadest">The broadest such term, shown with <c>^</c> for the word start and <c>$</c> for its end; null when none is over-broad.</param>
public sealed record RealizesPattern(string Kind, string Text, string Anchor, string Segmentation, bool OverBroad = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Broadest = null);

/// <summary>What parsing the corpus's words to find a morpheme came to, so a reader sees how much of the corpus the set rests on.</summary>
/// <param name="Types">Word types in the corpus.</param>
/// <param name="Parsed">Word types the batch parse answered for.</param>
/// <param name="WithMorpheme">Parsed words with an analysis that contains the morpheme.</param>
/// <param name="NoAnalysis">Parsed words the grammar gave no analysis.</param>
/// <param name="Unsegmentable">Words PanGloss could not segment with the grammar's characters.</param>
/// <param name="Capped">Words whose parse stopped at the attempt or work limit.</param>
/// <param name="TimedOut">Words whose parse stopped at the word timeout.</param>
/// <param name="Failed">Words the resident parser did not answer.</param>
/// <param name="Seconds">Wall-clock seconds the index took to build or load, and the search over it.</param>
/// <param name="Placed">Matching words whose morpheme piece PanGloss's spans gave.</param>
/// <param name="WholeWord">Matching words that stand in as their whole word, for a typed null span or spans that do not add up.</param>
/// <param name="IndexBuilt">Whether this query built the parse index; false when a cached one was reused.</param>
/// <param name="IndexSeconds">Seconds to build or load the parse index.</param>
public sealed record RealizesParseReport(int Types, int Parsed, int WithMorpheme, int NoAnalysis, int Unsegmentable, int Capped, int TimedOut,
    int Failed, double Seconds, int Placed = 0, int WholeWord = 0, bool IndexBuilt = false, double IndexSeconds = 0);

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
/// <param name="Rules">The rules that may change the morpheme, for a <c>possible</c> or <c>generated</c> set; null for <c>attested</c>.</param>
/// <param name="Phon">Corpus searches over the rules' match patterns that join the form search, for a <c>possible</c> set.</param>
/// <param name="Effects">The effects scan's counts, for a <c>possible</c> or <c>generated</c> set.</param>
public sealed record RealizesResult(string Schema, string Morpheme, string Tier, bool Complete, IReadOnlyList<RealizesReason> Reasons,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? StopReason,
    IReadOnlyList<RealizesForm> Forms, RealizesPattern Pattern,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RealizesParseReport? Parse = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RealizesRule>? Rules = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RealizesPhonClause>? Phon = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RealizesEffectsSummary? Effects = null)
{
    /// <summary>The schema name of the JSON form.</summary>
    public const string SchemaName = "motif.realizes.v1";
}

/// <summary>The forms of one variant set that a hit contains, so a reader sees why the word matched.</summary>
/// <param name="Morpheme">The morpheme as resolved.</param>
/// <param name="Tier">The tier of the set.</param>
/// <param name="Forms">The set's forms the word carries at the anchor.</param>
public sealed record RealizesHit(string Morpheme, string Tier, IReadOnlyList<string> Forms);
