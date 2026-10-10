using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Help;

namespace SIL.Motif.Host.Parsimony;

/// <summary>Turns a check that could not look into the information line a Parsimony Report shows.</summary>
public static class ParsimonyNotes
{
    /// <summary>The note reason when a Report was built without an Assessment for the parser-tier checks.</summary>
    public const string NoAssessmentReason = "no Assessment was supplied for this Report.";
    /// <summary>
    /// The measure ID of no-effect notes. They describe the grammar facts rather than one measure, so they share
    /// this pseudo-measure ID; it has no recipe and no finding.
    /// </summary>
    public const string NoEffectMeasureId = "grammar-facts/no-effect";
    private static readonly Lazy<ParsimonyRecipeCatalog> Recipes = new(ParsimonyRecipeCatalog.Load);

    private static readonly IReadOnlyDictionary<string, string> SectionPhrases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["adhoc_prohibitions"] = "ad hoc prohibitions",
            ["affix_processes"] = "affix processes",
            ["allomorphs"] = "allomorphs",
            ["categories"] = "part-of-speech categories",
            ["compiled_mappings"] = "compiled morph rules",
            ["compound_rules"] = "compound rules",
            ["entries"] = "lexical entries",
            ["environments"] = "environments",
            ["features"] = "phonological features",
            ["load_accounting"] = "load accounting",
            ["msas"] = "morphosyntactic analyses",
            ["patterns"] = "phonological patterns",
            ["phonology"] = "phonological rules",
            ["templates"] = "affix templates",
        };

    /// <summary>
    /// The not-checked note for a result that carries a note reason, or for an Inconclusive result without one. The
    /// note is one short clause; the run keeps its full Detail for Report readers. Returns null when nothing is missing.
    /// </summary>
    public static ParsimonyNote? NotChecked(ParsimonyMeasureResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var reason = result.NoteReason;
        if (reason is null) return null;
        var title = Recipes.Value.Find(result.Run.MeasureId)?.Title ?? result.Run.MeasureId;
        return new ParsimonyNote("not-checked", result.Run.MeasureId, $"Not checked: {title} — {reason}", null);
    }

    /// <summary>The note reason for grammar-facts sections a query needs but the artifact does not publish.</summary>
    public static string MissingFactsReason(IEnumerable<string> sections)
    {
        var items = sections.Select(section => SectionPhrases.GetValueOrDefault(section) ?? "other grammar facts")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var list = items.Count == 1 ? items[0]
            : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];
        return $"the grammar facts do not include {list}.";
    }
}
