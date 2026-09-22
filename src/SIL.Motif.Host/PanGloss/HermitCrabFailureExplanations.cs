namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Turns one of PanGloss's 23 trace <c>failureReason</c> codes into the plain sentence FieldWorks' own Try a
/// Word dialog would show for the reason it corresponds to (<c>FormatHCTrace.xsl</c>'s
/// <c>ShowAnyFailure</c> template). Several of FieldWorks' sentences interpolate a named allomorph, feature
/// value, or competing parse that PanGloss's trace tree does not carry at all, so those reasons get a plain
/// sentence describing the same mechanism instead of a misquote built from data this module does not have.
/// </summary>
public static class HermitCrabFailureExplanations
{
    private static readonly IReadOnlyDictionary<string, string> Exact = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["SurfaceFormMismatch"] = "The synthesized surface form does not match the input word.",
        ["BoundRoot"] = "A bound stem or root was found completely by itself. These must have at least one other morpheme present.",
        ["PartialParse"] = "This parse does not include all analyzed morphemes. Perhaps the missing morphemes are in an inflectional template that is not available at this point in the synthesis.",
        ["NonPartialRuleProhibitedAfterFinalTemplate"] = "Further derivation is prohibited after a final template.",
        ["NonPartialRuleRequiredAfterNonFinalTemplate"] = "Further derivation is required after a non-final template, but this affix is not derivational.",
        ["RequiredStemName"] = "This allomorph's stem-name label requires an inflectional affix with a matching label, but there are no such affixes.",
        ["ExcludedStemName"] = "The parse's inflectional features match a stem-name label that another allomorph of this root excludes.",
        ["MaxApplicationCount"] = "This rule hit its configured limit on how many times it may apply within one derivation.",
    };

    // A reason FieldWorks phrases with a named allomorph, feature or competing word this trace does not carry.
    private static readonly IReadOnlyDictionary<string, string> Paraphrased = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ObligatorySyntacticFeatures"] = "A syntactic feature the rule requires was missing or did not match on the word.",
        ["RequiredSyntacticFeatureStruct"] = "A required bundle of agreement features was not satisfied.",
        ["HeadRequiredSyntacticFeatureStruct"] = "A required bundle of agreement features was not satisfied on the compound's head member.",
        ["NonHeadRequiredSyntacticFeatureStruct"] = "A required bundle of agreement features was not satisfied on the compound's non-head member.",
        ["AllomorphCoOccurrenceRules"] = "An ad hoc prohibition between specific allomorphs ruled this allomorph out here.",
        ["MorphemeCoOccurrenceRules"] = "An ad hoc prohibition between specific morphemes ruled this morpheme out here.",
        ["Environments"] = "The phonological environment this allomorph or rule is restricted to did not match at this point in the word.",
        ["DisjunctiveAllomorph"] = "A different, mutually exclusive allomorph of the same morpheme was chosen instead.",
        ["Pattern"] = "The rule's input or output pattern did not match this candidate's shape.",
        ["HeadPattern"] = "The rule's pattern did not match the compound's head member.",
        ["NonHeadPattern"] = "The rule's pattern did not match the compound's non-head member.",
        ["HeadProdRestrictMprFeatures"] = "A production restriction stated in terms of word-class features failed for the compound's head member.",
        ["NonHeadProdRestrictMprFeatures"] = "A production restriction stated in terms of word-class features failed for the compound's non-head member.",
        ["RequiredMprFeatures"] = "A word-class feature the rule requires was not present on the word.",
        ["ExcludedMprFeatures"] = "A word-class feature the rule specifically excludes was present on the word.",
    };

    /// <summary>
    /// The plain-English explanation for <paramref name="failureReason"/>, or a generic sentence built from
    /// the reason's own name when it names none of the 23 catalogued reasons (a future PanGloss addition).
    /// </summary>
    public static string Explain(string failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        if (Exact.TryGetValue(failureReason, out var exact)) return exact;
        if (Paraphrased.TryGetValue(failureReason, out var paraphrased)) return paraphrased;
        return "The parser refused this step: " + Humanize(failureReason) + ".";
    }

    // "SurfaceFormMismatch" -> "surface form mismatch", for a reason this module does not yet catalogue.
    private static string Humanize(string pascalCase)
    {
        var words = new List<string>();
        var start = 0;
        for (var index = 1; index <= pascalCase.Length; index++)
        {
            if (index == pascalCase.Length || char.IsUpper(pascalCase[index]))
            {
                words.Add(pascalCase[start..index]);
                start = index;
            }
        }
        return string.Join(' ', words).ToLowerInvariant();
    }
}
