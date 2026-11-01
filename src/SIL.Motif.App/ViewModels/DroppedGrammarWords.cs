using System.Globalization;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The window's sentence for grammar the parser left out: each object by its kind and name in words, with none of
/// the parser's finding codes, FieldWorks class names or ids that the command line and the AI tools show.
/// </summary>
public static class DroppedGrammarWords
{
    /// <summary>The most objects named in one sentence; the rest are counted.</summary>
    private const int MaxNamed = 3;

    /// <summary>The Review blocker for <paramref name="dropped"/>.</summary>
    public static string Blocker(DroppedGrammar dropped)
    {
        ArgumentNullException.ThrowIfNull(dropped);
        var objects = dropped.Introduced.SelectMany(finding => finding.Objects.Count == 0
                ? [(DroppedGrammarObject?)null]
                : finding.Objects.Select(item => (DroppedGrammarObject?)item))
            .DistinctBy(item => item?.MotifId).ToArray();
        var named = objects.Take(MaxNamed).Select(Describe).Distinct(StringComparer.Ordinal).ToList();
        var more = Math.Max(0, objects.Length - MaxNamed) + dropped.OmittedFindings;
        var list = string.Join(", ", named);
        if (more > 0) list += " " + string.Format(CultureInfo.CurrentCulture, MotifAppStrings.DroppedGrammarMoreFormat, more);
        return string.Format(CultureInfo.CurrentCulture, MotifAppStrings.DroppedGrammarBlockerFormat, list);
    }

    private static string Describe(DroppedGrammarObject? item)
    {
        if (item is null) return MotifAppStrings.DroppedGrammarUnknownObject;
        var kind = KindWord(item.Kind);
        return string.IsNullOrWhiteSpace(item.Name)
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.DroppedGrammarUntitledObjectFormat, kind)
            : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.DroppedGrammarObjectFormat, kind, item.Name);
    }

    // FieldWorks class names are matched by their stable stems; any other class reads as part of the grammar.
    private static string KindWord(string? fieldWorksClass)
    {
        var name = fieldWorksClass ?? string.Empty;
        bool Has(string stem) => name.Contains(stem, StringComparison.OrdinalIgnoreCase);
        if (Has("Environment")) return MotifAppStrings.DroppedKindEnvironment;
        if (Has("Allomorph") || name is "MoForm" or "MoStemAllomorph" or "MoAffixAllomorph")
            return MotifAppStrings.DroppedKindAllomorph;
        if (Has("LexEntry")) return MotifAppStrings.DroppedKindEntry;
        if (Has("NaturalClass") || Has("PhNC")) return MotifAppStrings.DroppedKindSoundClass;
        if (Has("Slot")) return MotifAppStrings.DroppedKindSlot;
        if (Has("StemName")) return MotifAppStrings.DroppedKindStemName;
        if (Has("Prohib")) return MotifAppStrings.DroppedKindProhibition;
        if (Has("Stratum")) return MotifAppStrings.DroppedKindStratum;
        if (Has("Rule")) return MotifAppStrings.DroppedKindRule;
        return MotifAppStrings.DroppedKindOther;
    }
}
