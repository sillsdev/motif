using System.Text;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Plain names for the kinds of step a parser trace records, such as "Morphological rule" for a step called
/// <c>MorphologicalRuleSynthesis</c>. A rule is one rule to a linguist whichever way the parser ran it, so the
/// analysis and synthesis halves of a step read the same, and a kind this table does not know is spelled out
/// in words rather than shown as the parser's class name.
/// </summary>
public static class TraceStepKinds
{
    private static readonly string[] DirectionSuffixes = ["Input", "Output", "Analysis", "Synthesis"];

    private static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MorphologicalRule"] = "Morphological rule",
        ["PhonologicalRule"] = "Phonological rule",
        ["Template"] = "Affix template",
        ["LexicalLookup"] = "Lexical lookup",
        ["Stratum"] = "Rule level",
        ["CompoundingRule"] = "Compound rule",
        ["Word"] = "Word",
        ["Successful"] = "Built the word",
        ["Failed"] = "Stopped",
        ["Blocked"] = "not repeated (would feed itself)",
    };

    /// <summary>The plain name for a trace step's kind, never the parser's own class name.</summary>
    /// <param name="type">The step's kind as the trace records it.</param>
    public static string Describe(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return "Step";
        if (Kinds.TryGetValue(type, out var exact)) return exact;
        var stem = type;
        // Input and Output come off before Analysis and Synthesis, so "TemplateAnalysisInput" leaves "Template".
        foreach (var suffix in DirectionSuffixes)
            if (stem.Length > suffix.Length && stem.EndsWith(suffix, StringComparison.Ordinal))
                stem = stem[..^suffix.Length];
        return Kinds.TryGetValue(stem, out var kind) ? kind : Humanise(type);
    }

    /// <summary>Why the parser refused a step, in words, for a reason code no sentence was recorded for.</summary>
    /// <param name="reasonCode">The parser's own reason code.</param>
    public static string ExplainReason(string reasonCode) =>
        $"Explanation not recorded (reason code: {reasonCode}).";

    // "SomeFutureStepKind" reads "Some future step kind".
    internal static string Humanise(string pascalCase)
    {
        var text = new StringBuilder();
        for (var index = 0; index < pascalCase.Length; index++)
        {
            var character = pascalCase[index];
            if (character is '_' or '-') character = ' ';
            else if (index > 0 && char.IsUpper(character) && !char.IsUpper(pascalCase[index - 1])) text.Append(' ');
            text.Append(index == 0 ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character));
        }
        return text.ToString();
    }
}
