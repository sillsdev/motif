namespace SIL.Motif.Contract.Responses;

/// <summary>Stable codes for Motif findings projected from recorded parser evidence.</summary>
public static class GrammarFindingCodes
{
    /// <summary>An allomorph the parser could not segment while importing the grammar for Parse all words.</summary>
    public const string ParseAllomorphUnsegmentable = "parse.allomorph.unsegmentable";

    /// <summary>A required inflectional slot has no assigned inflectional affixes.</summary>
    public const string RequiredSlotEmpty = "grammar.required-slot-empty";

    /// <summary>A stored negated feature value, which FieldWorks' own parser loader cannot read.</summary>
    public const string NegatedFeatureValue = "grammar.negated-feature-value";

    /// <summary>A derivational affix with a From Inflection Class and no To Inflection Class, so derived stems keep the From class.</summary>
    public const string DerivedStemKeepsFromClass = "grammar.derived-stem-keeps-from-class";
}
