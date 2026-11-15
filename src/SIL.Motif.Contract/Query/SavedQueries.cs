namespace SIL.Motif.Contract.Query;

/// <summary>
/// Queries a build ships under the id of the Parsimony Measure whose detection they express. Each returns the same
/// items as its Measure over the same facts; the Measure adds its findings, evidence and wording.
/// </summary>
public static class SavedQueries
{
    private static readonly IReadOnlyDictionary<string, QueryRecord> ById = new Dictionary<string, QueryRecord>(StringComparer.Ordinal)
    {
        // A stem MSA with no part-of-speech row of its own.
        ["P-msa-no-category"] = new(QuerySources.Stems, [new("category", "is", "none")], []),
        // A fact-table reference count of zero, or references the compiler did not apply.
        ["P-statement-unused"] = new(QuerySources.Objects,
            [new("class", "in", "environment,natural-class"), new("applied", "is", "0")], []),
        // Environments whose two canonical side keys match; only valid environments with both keys have a key.
        ["P-env-duplicate"] = new(QuerySources.Objects, [new("class", "is", "environment")], ["key"], MinCount: 2),
        // Used exception features carried by fewer than four entries on stem MSAs.
        ["P-feature-few-carriers"] = new(QuerySources.Objects,
            [new("class", "is", "exception-feature"), new("users", ">", "0"), new("carriers", "<", "4")], []),
        // Lexeme forms that differ only in Unicode form, case or the project's fold rules.
        ["P-lexeme-near-duplicate"] = new(QuerySources.Forms, [new("kind", "in", "lexeme,variant")], ["fold"],
            MinCount: 2, Fold: "nfc,case,project", Distinct: "form"),
    };

    /// <summary>The measure ids that have a saved query, in a stable order.</summary>
    public static IReadOnlyList<string> Ids { get; } = [.. ById.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Finds the saved query of a measure id.</summary>
    public static bool TryGet(string id, out QueryRecord query)
    {
        var found = ById.TryGetValue(id ?? "", out var value);
        query = value!;
        return found;
    }
}
