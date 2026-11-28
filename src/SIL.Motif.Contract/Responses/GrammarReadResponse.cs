using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Requests;

namespace SIL.Motif.Contract.Responses;

/// <summary>A page of project objects read from a saved Baseline.</summary>
/// <param name="Kind">The kind of objects in the page.</param>
/// <param name="Total">The number of objects matching the filters.</param>
/// <param name="Limit">The maximum number of objects requested for this page.</param>
/// <param name="Items">The typed objects in this page.</param>
public sealed record GrammarReadResponse(
    GrammarReadKind Kind,
    int Total,
    int Limit,
    IReadOnlyList<GrammarReadItem> Items)
{
    /// <summary>The Baseline whose saved model supplied these objects.</summary>
    public BaselineToken? Baseline { get; init; }

    /// <summary>When FieldWorks last saved the project state captured by this Baseline.</summary>
    public DateTimeOffset? SourceLastWriteUtc { get; init; }

    /// <summary>When the live project file was last written while this response was made.</summary>
    public DateTimeOffset? ProjectLastWriteUtc { get; init; }

    /// <summary>Whether the live project file has a later save time than this Baseline.</summary>
    public bool IsStale { get; init; }

    /// <summary>What was read: <c>baseline</c>, or <c>proposal &lt;id&gt;</c> for a Proposal's Trial copy.</summary>
    public string? At { get; init; }

    /// <summary>For a read at a Proposal: what the copy is, so it is not mistaken for the current Baseline.</summary>
    public string? AtLabel { get; init; }

    /// <summary>
    /// For a bare exact form that also matched marked affix headwords: which ones, and how to match only one kind.
    /// </summary>
    public string? ExactNote { get; init; }

    /// <summary>The number of objects returned on this page.</summary>
    public int Returned => Items.Count;

    /// <summary>The cursor that starts the next page, or <see langword="null"/> when this page ends the matches.</summary>
    public string? NextCursor { get; init; }
}

/// <summary>One typed object returned by a grammar read.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(GrammarPhonemeRead), "phoneme")]
[JsonDerivedType(typeof(GrammarNaturalClassRead), "natural_class")]
[JsonDerivedType(typeof(GrammarEnvironmentRead), "environment")]
[JsonDerivedType(typeof(GrammarPartOfSpeechRead), "part_of_speech")]
[JsonDerivedType(typeof(GrammarInflectionFeatureRead), "inflection_feature")]
[JsonDerivedType(typeof(GrammarLexicalEntryRead), "lexical_entry")]
[JsonDerivedType(typeof(GrammarPhonologicalRuleRead), "phonological_rule")]
[JsonDerivedType(typeof(GrammarAffixTemplateRead), "affix_template")]
public abstract record GrammarReadItem(string Id);

/// <summary>A phoneme and the writing-system representations authored for it.</summary>
/// <param name="Id">The phoneme's canonical id.</param>
/// <param name="Name">The phoneme's name in the best available analysis writing system.</param>
/// <param name="Representations">The stored codes with their writing-system tags.</param>
public sealed record GrammarPhonemeRead(
    string Id,
    string? Name,
    IReadOnlyList<GrammarRepresentationRead> Representations) : GrammarReadItem(Id);

/// <summary>A phoneme's written representation in one writing system.</summary>
/// <param name="Text">The exact stored representation.</param>
/// <param name="WritingSystem">The writing-system tag for the representation.</param>
public sealed record GrammarRepresentationRead(string? Text, string? WritingSystem);

/// <summary>A natural class and its segment members or feature specifications.</summary>
/// <param name="Id">The natural class's canonical id.</param>
/// <param name="Name">The class's name.</param>
/// <param name="Abbreviation">The class's abbreviation, when present.</param>
/// <param name="ClassKind">The LibLCM natural-class kind.</param>
/// <param name="Members">The phonemes in a segment natural class.</param>
/// <param name="Features">The feature specifications in a feature natural class.</param>
public sealed record GrammarNaturalClassRead(
    string Id,
    string? Name,
    string? Abbreviation,
    string ClassKind,
    IReadOnlyList<GrammarNamedObjectRead> Members,
    IReadOnlyList<GrammarFeatureSpecificationRead> Features) : GrammarReadItem(Id);

/// <summary>A linked project object identified by its canonical id and display name.</summary>
/// <param name="Id">The linked object's canonical id.</param>
/// <param name="Name">The linked object's name.</param>
public sealed record GrammarNamedObjectRead(string Id, string? Name);

/// <summary>A feature specification and its optional symbolic value.</summary>
/// <param name="Feature">The feature definition.</param>
/// <param name="Value">The symbolic value, when the specification selects one.</param>
public sealed record GrammarFeatureSpecificationRead(
    GrammarNamedObjectRead Feature,
    GrammarNamedObjectRead? Value);

/// <summary>A phonological environment and the notation FieldWorks stores for it.</summary>
/// <param name="Id">The environment's canonical id.</param>
/// <param name="Name">The environment's name.</param>
/// <param name="Notation">The stored environment notation.</param>
public sealed record GrammarEnvironmentRead(string Id, string? Name, string? Notation) : GrammarReadItem(Id);

/// <summary>A part of speech and the category that contains it.</summary>
/// <param name="Id">The part of speech's canonical id.</param>
/// <param name="Name">The category's name.</param>
/// <param name="Abbreviation">The category's abbreviation, when present.</param>
/// <param name="Parent">The parent category, or <see langword="null"/> for a top-level category.</param>
public sealed record GrammarPartOfSpeechRead(
    string Id,
    string? Name,
    string? Abbreviation,
    GrammarNamedObjectRead? Parent) : GrammarReadItem(Id);

/// <summary>An inflection feature and its closed values when it declares them.</summary>
/// <param name="Id">The feature's canonical id.</param>
/// <param name="Name">The feature's name.</param>
/// <param name="Abbreviation">The feature's abbreviation, when present.</param>
/// <param name="FeatureKind">The LibLCM feature kind.</param>
/// <param name="Values">The feature's declared symbolic values.</param>
public sealed record GrammarInflectionFeatureRead(
    string Id,
    string? Name,
    string? Abbreviation,
    string FeatureKind,
    IReadOnlyList<GrammarNamedObjectRead> Values) : GrammarReadItem(Id);

/// <summary>A lexical entry with its lexeme form, senses and alternate forms.</summary>
/// <param name="Id">The entry's canonical id.</param>
/// <param name="Headword">The stored headword.</param>
/// <param name="MorphType">The lexeme form's morph type, when present.</param>
/// <param name="MorphTypeId">The morph type's canonical id, when present.</param>
/// <param name="LexemeForm">The entry's lexeme form, when present.</param>
/// <param name="Senses">The entry's senses.</param>
/// <param name="AlternateForms">The entry's alternate forms.</param>
public sealed record GrammarLexicalEntryRead(
    string Id,
    string? Headword,
    string? MorphType,
    string? MorphTypeId,
    GrammarFormRead? LexemeForm,
    IReadOnlyList<GrammarSenseRead> Senses,
    IReadOnlyList<GrammarFormRead> AlternateForms) : GrammarReadItem(Id)
{
    /// <summary>
    /// Every morpho-syntactic analysis of the entry, whether or not a sense uses it; present only when one entry is
    /// read by id.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<GrammarGrammaticalInfoRead>? Msas { get; init; }
}

/// <summary>An inflectional affix template: its category, finality and slots in order from the stem outward.</summary>
/// <param name="Id">The template's canonical id.</param>
/// <param name="Name">The template's name.</param>
/// <param name="PartOfSpeech">The category that owns the template.</param>
/// <param name="Final">Whether a word may end after this template.</param>
/// <param name="PrefixSlots">The prefix slots, in template order.</param>
/// <param name="SuffixSlots">The suffix slots, in template order.</param>
public sealed record GrammarAffixTemplateRead(
    string Id,
    string? Name,
    GrammarNamedObjectRead? PartOfSpeech,
    bool Final,
    IReadOnlyList<GrammarAffixSlotRead> PrefixSlots,
    IReadOnlyList<GrammarAffixSlotRead> SuffixSlots) : GrammarReadItem(Id);

/// <summary>One slot of a template and the affix analyses that name it.</summary>
/// <param name="Id">The slot's canonical id.</param>
/// <param name="Name">The slot's name.</param>
/// <param name="Optional">Whether a word may leave the slot empty.</param>
/// <param name="Affixes">The inflectional affix analyses that fill the slot.</param>
public sealed record GrammarAffixSlotRead(
    string Id,
    string? Name,
    bool Optional,
    IReadOnlyList<GrammarSlotAffixRead> Affixes);

/// <summary>An inflectional affix analysis that fills a slot, with its entry.</summary>
/// <param name="EntryId">The entry's canonical id.</param>
/// <param name="Headword">The entry's headword.</param>
/// <param name="MorphType">The lexeme form's morph type.</param>
/// <param name="MsaId">The analysis's canonical id.</param>
/// <param name="PartOfSpeech">The analysis's part of speech, when present.</param>
public sealed record GrammarSlotAffixRead(
    string EntryId,
    string? Headword,
    string? MorphType,
    string MsaId,
    GrammarNamedObjectRead? PartOfSpeech);

/// <summary>A form and the authored environments that constrain it.</summary>
/// <param name="Id">The form's canonical id.</param>
/// <param name="Form">The stored form text.</param>
/// <param name="WritingSystem">The form's writing-system tag.</param>
/// <param name="FormKind">The LibLCM form kind.</param>
/// <param name="MorphType">The form's morph type, when present.</param>
/// <param name="MorphTypeId">The morph type's canonical id, when present.</param>
/// <param name="Environments">The environments the form references.</param>
public sealed record GrammarFormRead(
    string Id,
    string? Form,
    string? WritingSystem,
    string FormKind,
    string? MorphType,
    string? MorphTypeId,
    IReadOnlyList<GrammarEnvironmentReferenceRead> Environments);

/// <summary>An environment referenced by a form.</summary>
/// <param name="Id">The environment's canonical id.</param>
/// <param name="Name">The environment's name.</param>
/// <param name="Notation">The stored environment notation.</param>
public sealed record GrammarEnvironmentReferenceRead(string Id, string? Name, string? Notation);

/// <summary>A sense with its gloss, definition and grammatical-information link.</summary>
/// <param name="Id">The sense's canonical id.</param>
/// <param name="Gloss">The gloss in the best analysis writing system.</param>
/// <param name="GlossWritingSystem">The gloss's writing-system tag.</param>
/// <param name="Definition">The definition in the best analysis writing system.</param>
/// <param name="DefinitionWritingSystem">The definition's writing-system tag.</param>
/// <param name="GrammaticalInfo">The linked grammatical information, when present.</param>
public sealed record GrammarSenseRead(
    string Id,
    string? Gloss,
    string? GlossWritingSystem,
    string? Definition,
    string? DefinitionWritingSystem,
    GrammarGrammaticalInfoRead? GrammaticalInfo);

/// <summary>Grammatical information linked to a lexical sense.</summary>
/// <param name="Id">The grammatical-information object's canonical id.</param>
/// <param name="Kind">The LibLCM grammatical-information kind.</param>
/// <param name="PartOfSpeech">The linked source or main part of speech, when present.</param>
/// <param name="ResultPartOfSpeech">The linked result part of speech, when present.</param>
public sealed record GrammarGrammaticalInfoRead(
    string Id,
    string Kind,
    GrammarNamedObjectRead? PartOfSpeech,
    GrammarNamedObjectRead? ResultPartOfSpeech)
{
    /// <summary>The inflection class a stem analysis belongs to, when it names one.</summary>
    public GrammarNamedObjectRead? InflectionClass { get; init; }

    /// <summary>The affix slots an inflectional affix analysis fills; absent when it fills none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<GrammarNamedObjectRead>? Slots { get; init; }
}

/// <summary>A phonological rule and the authoring intent its edit guard copies.</summary>
/// <param name="Id">The rule's canonical id.</param>
/// <param name="Name">The rule's name.</param>
/// <param name="Position">The rule's 1-based place in the project's application order.</param>
/// <param name="RuleKind">The LibLCM rule class.</param>
/// <param name="Disabled">Whether the rule is switched off.</param>
/// <param name="RightHandSideCount">The number of outcomes of a regular rule.</param>
/// <param name="Intent">
/// The rule's intent as the edit verbs read it, or <see langword="null"/> when a context or applicability
/// reference has a shape the composer cannot express.
/// </param>
public sealed record GrammarPhonologicalRuleRead(
    string Id,
    string? Name,
    int Position,
    string RuleKind,
    bool Disabled,
    int? RightHandSideCount,
    JsonNode? Intent) : GrammarReadItem(Id);
