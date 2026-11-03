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

    /// <summary>An affix template with no slot, which the parser cannot use.</summary>
    public const string TemplateHasNoSlots = "grammar.template-has-no-slots";

    /// <summary>A phoneme spelling that other phonemes' spellings add up to, which the parser reads as the longest one.</summary>
    public const string PhonemeSpellingAmbiguous = "grammar.phoneme-spelling-ambiguous";

    /// <summary>A phoneme that no form, wordform, rule, natural class or environment uses.</summary>
    public const string PhonemeNeverUsed = "grammar.phoneme-never-used";

    /// <summary>A form's environment the parser cannot read, which it drops, leaving the form unrestricted by it.</summary>
    public const string AllomorphEnvironmentIgnoredByParser = "grammar.allomorph-environment-ignored-by-parser";

    /// <summary>A form that already spells the material its environment asks of its neighbour, which may be a mistake.</summary>
    public const string AllomorphRepeatsEnvironmentMaterial = "grammar.allomorph-repeats-environment-material";

    /// <summary>An entry marked do-not-parse, which PanGloss leaves out and FieldWorks' parsers still use.</summary>
    public const string DoNotParseIgnoredByFieldWorks = "fieldworks.do-not-parse-ignored";
}
