namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A named left, right or result category is not a part of speech in the project.</summary>
    public const string CompoundRuleInvalidCategory = "compound-rule.invalid-category";
    /// <summary>The same compound rule already exists in the project or earlier in the Draft.</summary>
    public const string CompoundRuleDuplicate = "compound-rule.duplicate";
    /// <summary>The project has no morphology data to own the compound rule.</summary>
    public const string CompoundRuleNoMorphologyData = "compound-rule.no-morphology-data";
}
