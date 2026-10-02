using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SIL.Motif.Commands.Queries;

/// <summary>Window sentences for recorded PanGloss refusal codes, without assigning an unrecorded owner.</summary>
public static class TraceFailureSentences
{
    public static IReadOnlyDictionary<string, string> Catalog { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ObligatorySyntacticFeatures"] = "{0} lacks an obligatory inflection feature.",
        ["AllomorphCoOccurrenceRules"] = "{0} does not satisfy an allomorph combination restriction.",
        ["Environments"] = "{0} does not fit the required sound environment.",
        ["MorphemeCoOccurrenceRules"] = "{0} does not satisfy a morpheme combination restriction.",
        ["DisjunctiveAllomorph"] = "{0} uses an allomorph that was passed over for another one.",
        ["SurfaceFormMismatch"] = "{0} builds a form that differs from the word being parsed.",
        ["Pattern"] = "{0} does not satisfy the rule's pattern check.",
        ["HeadPattern"] = "{0} does not satisfy the pattern check for the compound's head.",
        ["NonHeadPattern"] = "{0} does not satisfy the pattern check for the compound's non-head part.",
        ["RequiredSyntacticFeatureStruct"] = "{0} needs an inflection feature the word doesn't have.",
        ["HeadRequiredSyntacticFeatureStruct"] = "{0} needs an inflection feature the compound's head doesn't have.",
        ["NonHeadRequiredSyntacticFeatureStruct"] = "{0} needs an inflection feature the compound's non-head part doesn't have.",
        ["HeadProdRestrictMprFeatures"] = "{0} does not satisfy a lexical class restriction on the compound's head.",
        ["NonHeadProdRestrictMprFeatures"] = "{0} does not satisfy a lexical class restriction on the compound's non-head part.",
        ["RequiredMprFeatures"] = "{0} needs a lexical class feature the word doesn't have.",
        ["ExcludedMprFeatures"] = "{0} excludes a lexical class feature the word has.",
        ["RequiredStemName"] = "{0} needs a stem name the word doesn't have.",
        ["ExcludedStemName"] = "{0} excludes a stem name the word has.",
        ["PartialParse"] = "{0} did not complete the required rules and templates.",
        ["BoundRoot"] = "{0} leaves a bound root standing alone.",
        ["NonPartialRuleProhibitedAfterFinalTemplate"] = "{0} can't apply after the last template.",
        ["NonPartialRuleRequiredAfterNonFinalTemplate"] = "{0} needs a rule after a non-final template.",
        ["MaxApplicationCount"] = "{0} reached the rule's application limit.",
    };

    /// <summary>Uses only a recorded code, explicitly attributed rule, and named required operand.</summary>
    public static string Explain(string? code, string? rule = null, string? required = null)
    {
        if (string.IsNullOrWhiteSpace(code)) return "PanGloss didn't record why.";
        if (!Catalog.TryGetValue(code, out var sentence)) return $"PanGloss recorded an unfamiliar reason: {code}.";
        var text = string.Format(System.Globalization.CultureInfo.CurrentCulture, sentence,
            string.IsNullOrWhiteSpace(rule) ? "This attempt" : rule);
        if (required is { Length: > 0 } && code.Contains("Feature", StringComparison.Ordinal))
        {
            // Structured operands can contain local ordinals, which do not establish authored feature names.
            try
            {
                using var parsed = JsonDocument.Parse(required);
                if (parsed.RootElement.ValueKind == JsonValueKind.String)
                    text += $" Required: {parsed.RootElement.GetString()}.";
            }
            catch (JsonException)
            {
                text += $" Required: {required}.";
            }
        }
        return text;
    }
}
