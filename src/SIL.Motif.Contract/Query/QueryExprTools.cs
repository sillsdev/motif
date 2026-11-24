using SIL.Motif.Contract.Query.Phon;

namespace SIL.Motif.Contract.Query;

/// <summary>Reads a <see cref="QueryExpr"/> without evaluating it.</summary>
public static class QueryExprTools
{
    /// <summary>Every field predicate anywhere in the expression.</summary>
    public static IEnumerable<QueryField> Leaves(QueryExpr? expr) => expr switch
    {
        QueryField field => [field],
        QueryAnd and => and.Items.SelectMany(Leaves),
        QueryOr or => or.Items.SelectMany(Leaves),
        QueryNot not => Leaves(not.Item),
        _ => [],
    };

    /// <summary>Every <c>phon</c> and <c>env</c> predicate in the expression, with whether it counts toward a row's reported matches (not under a <c>not</c>).</summary>
    internal static List<(PhonLeaf Leaf, bool Positive)> Patterns(QueryExpr? expr)
    {
        var found = new List<(PhonLeaf, bool)>();
        Walk(expr, true);
        return found;

        void Walk(QueryExpr? node, bool positive)
        {
            switch (node)
            {
                case QueryPhon or QueryEnv: found.Add((LeafOf(node), positive)); break;
                case QueryAnd and: foreach (var item in and.Items) Walk(item, positive); break;
                case QueryOr or: foreach (var item in or.Items) Walk(item, positive); break;
                case QueryNot not: Walk(not.Item, !positive); break;
            }
        }
    }

    /// <summary>Whether the expression holds a <c>phon</c> or <c>env</c> predicate.</summary>
    public static bool HasPatterns(QueryExpr? expr) => expr switch
    {
        QueryPhon or QueryEnv => true,
        QueryAnd and => and.Items.Any(HasPatterns),
        QueryOr or => or.Items.Any(HasPatterns),
        QueryNot not => HasPatterns(not.Item),
        _ => false,
    };

    internal static PhonLeaf LeafOf(QueryExpr node) => node switch
    {
        QueryPhon phon when phon.Where == QueryOps.PhonWordScope => new PhonLeaf("phon", phon.Pattern, PhonPattern.CompilePhon(phon.Pattern)),
        QueryPhon phon => throw QueryRefusalException.NotYetAvailable($"phon (where: {phon.Where})"),
        QueryEnv env => new PhonLeaf("env", env.Environment, PhonPattern.CompileEnv(env.Environment)),
        _ => throw new ArgumentException("Not a pattern predicate."),
    };

    /// <summary>The field predicates every row must satisfy: the expression itself when it is one, or the children of a top-level and.</summary>
    public static IEnumerable<QueryField> Required(QueryExpr? expr) => expr switch
    {
        QueryField field => [field],
        QueryAnd and => and.Items.OfType<QueryField>(),
        _ => [],
    };

    /// <summary>Joins expressions with and, collapsing one into itself and none into null.</summary>
    public static QueryExpr? AllOf(IReadOnlyList<QueryExpr> items) => items.Count switch
    {
        0 => null,
        1 => items[0],
        _ => new QueryAnd(items),
    };

    /// <summary>The v1 selector of a predicate the v1 engine can run with the same meaning, or null.</summary>
    public static QueryCondition? ToCondition(QueryField field)
    {
        var text = field.Value as string;
        var list = field.Value as IReadOnlyList<string>;
        return field.Op switch
        {
            QueryOps.Eq when text is not null => new QueryCondition(field.Field, "is", text),
            QueryOps.Ne when text is not null => new QueryCondition(field.Field, "not", text),
            QueryOps.In when list is { Count: > 0 } && list.All(v => !v.Contains(',') && v.Trim() == v) =>
                new QueryCondition(field.Field, "in", string.Join(",", list)),
            QueryOps.Has when list is { Count: > 0 } && list.All(v => !v.Contains(',') && v.Trim() == v) =>
                new QueryCondition(field.Field, "has", string.Join(",", list)),
            QueryOps.Lt when field.Value is long n => new QueryCondition(field.Field, "<", n.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            QueryOps.Gt when field.Value is long g => new QueryCondition(field.Field, ">", g.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            QueryOps.Contains when text is not null => new QueryCondition(field.Field, "~", text),
            _ => null,
        };
    }

    /// <summary>The v1 selectors for an expression that is a conjunction of predicates v1 can run; false otherwise.</summary>
    public static bool TryLower(QueryExpr? expr, out List<QueryCondition> conditions)
    {
        conditions = [];
        if (expr is null) return true;
        var parts = expr is QueryAnd and ? and.Items : [expr];
        foreach (var part in parts)
        {
            if (part is not QueryField field || ToCondition(field) is not { } condition) return false;
            conditions.Add(condition);
        }
        return true;
    }
}
