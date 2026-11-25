namespace SIL.Motif.Contract.Query.Readings;

/// <summary>
/// Replaces each <c>only-via</c> by a test of the synthetic column <c>via:kind:id</c>, which a corpus row answers from the word's
/// readings (<c>only</c> when every reading uses the morpheme), so the rest of the engine needs no new node.
/// </summary>
public static class OnlyViaLowering
{
    /// <summary>The column prefix.</summary>
    public const string ColumnPrefix = "via:";

    /// <summary>The column a lowered predicate tests.</summary>
    public static string Column(ReadingTarget target) => ColumnPrefix + target;

    /// <summary>Whether the column is one an <c>only-via</c> lowers to.</summary>
    public static bool IsColumn(string column) => column.StartsWith(ColumnPrefix, StringComparison.Ordinal);

    /// <summary>The target a lowered column names.</summary>
    public static bool TryTarget(string column, out ReadingTarget target)
    {
        target = null!;
        return IsColumn(column) && ReadingTarget.TryParse(column[ColumnPrefix.Length..], out target);
    }

    /// <summary>Every <c>only-via</c> of the expression.</summary>
    public static List<QueryOnlyVia> Find(QueryExpr? expr)
    {
        var found = new List<QueryOnlyVia>();
        Walk(expr);
        return found;

        void Walk(QueryExpr? node)
        {
            switch (node)
            {
                case QueryOnlyVia via: found.Add(via); break;
                case QueryAnd and: foreach (var item in and.Items) Walk(item); break;
                case QueryOr or: foreach (var item in or.Items) Walk(item); break;
                case QueryNot not: Walk(not.Item); break;
            }
        }
    }

    /// <summary>Whether the expression names the live reading columns (<c>readings</c> or <c>only-via</c>).</summary>
    public static bool UsesReadings(QueryExpr? expr) =>
        Find(expr).Count > 0 || QueryExprTools.Leaves(expr).Any(leaf => leaf.Field == "readings");

    /// <summary>Lowers every <c>only-via</c>.</summary>
    public static QueryExpr? Lower(QueryExpr? expr) => expr is null ? null : Visit(expr);

    private static QueryExpr Visit(QueryExpr node) => node switch
    {
        QueryOnlyVia via => new QueryField(Column(new ReadingTarget(via.Kind, via.Id)), QueryOps.Eq, "only"),
        QueryAnd and => new QueryAnd([.. and.Items.Select(Visit)]),
        QueryOr or => new QueryOr([.. or.Items.Select(Visit)]),
        QueryNot not => new QueryNot(Visit(not.Item)),
        _ => node,
    };
}
