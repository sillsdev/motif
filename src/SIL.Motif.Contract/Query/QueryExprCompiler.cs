using System.Globalization;
using System.Text.RegularExpressions;
using SIL.Motif.Contract.Query.Phon;

namespace SIL.Motif.Contract.Query;

/// <summary>Turns a <see cref="QueryExpr"/> into a row predicate over one source's columns.</summary>
internal static class QueryExprCompiler
{
    public static Func<IQueryRowView, bool> Compile(QueryExpr expr, QuerySpec spec)
    {
        switch (expr)
        {
            case QueryAnd and:
                var all = and.Items.Select(item => Compile(item, spec)).ToArray();
                return row => all.All(test => test(row));
            case QueryOr or:
                var any = or.Items.Select(item => Compile(item, spec)).ToArray();
                return row => any.Any(test => test(row));
            case QueryNot not:
                var inner = Compile(not.Item, spec);
                return row => !inner(row);
            case QueryReserved reserved:
                throw QueryRefusalException.NotYetAvailable(reserved.Kind);
            case QueryRealizes:
                throw QueryRefusalException.NotYetAvailable("realizes on this source; it runs over a corpus with --grammar");
            case QueryOnlyVia:
                throw QueryRefusalException.NotYetAvailable("only-via on this source; it runs over a corpus with --grammar");
            case QueryMorphIs or QueryMorphSeq:
                throw QueryRefusalException.NotYetAvailable("morph and seq on this source; they run over a corpus with --grammar");
            case QueryPhon or QueryEnv:
                throw QueryRefusalException.NotYetAvailable(expr is QueryPhon ? "phon" : "env");
            case QueryField field:
                return CompileField(field, spec);
            default:
                throw new ArgumentException("Unknown query expression.");
        }
    }

    /// <summary>
    /// The three-valued form of <see cref="Compile"/>: a <c>phon</c> or <c>env</c> predicate answers through <paramref name="phon"/>,
    /// which says unknown for a form PanGloss could not decide, and and, or and not follow Kleene's rules, so an undecided form
    /// is never reported as a match or as the absence of one.
    /// </summary>
    internal static Func<IQueryRowView, Tri> CompileTri(QueryExpr expr, QuerySpec spec, Func<PhonLeaf, string, Tri> phon)
    {
        switch (expr)
        {
            case QueryAnd and:
                var all = and.Items.Select(item => CompileTri(item, spec, phon)).ToArray();
                return row =>
                {
                    var result = Tri.True;
                    foreach (var test in all)
                    {
                        var value = test(row);
                        if (value == Tri.False) return Tri.False;
                        if (value == Tri.Unknown) result = Tri.Unknown;
                    }
                    return result;
                };
            case QueryOr or:
                var any = or.Items.Select(item => CompileTri(item, spec, phon)).ToArray();
                return row =>
                {
                    var result = Tri.False;
                    foreach (var test in any)
                    {
                        var value = test(row);
                        if (value == Tri.True) return Tri.True;
                        if (value == Tri.Unknown) result = Tri.Unknown;
                    }
                    return result;
                };
            case QueryNot not:
                var inner = CompileTri(not.Item, spec, phon);
                return row => inner(row) switch { Tri.True => Tri.False, Tri.False => Tri.True, _ => Tri.Unknown };
            case QueryPhon or QueryEnv:
                var leaf = QueryExprTools.LeafOf(expr);
                return row => row.Get("form") is { Count: > 0 } form ? phon(leaf, form[0]) : Tri.False;
            case QueryReserved reserved:
                throw QueryRefusalException.NotYetAvailable(reserved.Kind);
            case QueryRealizes:
                throw QueryRefusalException.NotYetAvailable("realizes on this source; it runs over a corpus with --grammar");
            case QueryOnlyVia:
                throw QueryRefusalException.NotYetAvailable("only-via on this source; it runs over a corpus with --grammar");
            case QueryMorphIs or QueryMorphSeq:
                throw QueryRefusalException.NotYetAvailable("morph and seq on this source; they run over a corpus with --grammar");
            case QueryField field:
                var test2 = CompileField(field, spec);
                return row => test2(row) ? Tri.True : Tri.False;
            default:
                throw new ArgumentException("Unknown query expression.");
        }
    }

    private static Func<IQueryRowView, bool> CompileField(QueryField field, QuerySpec spec)
    {
        var column = field.Field;
        if (!spec.IsColumn(column)) throw new ArgumentException("Unknown query column.");
        var numeric = spec.IsNumeric(column);
        var set = spec.IsSet(column);
        var type = numeric ? "numeric" : set ? "set" : "text";
        switch (field.Op)
        {
            case QueryOps.Lt or QueryOps.Gt when !numeric:
            case QueryOps.Has when !set:
            case QueryOps.Regex or QueryOps.Contains when numeric:
                throw QueryRefusalException.OpTypeMismatch(column, field.Op, type);
        }
        switch (field.Op)
        {
            case QueryOps.Eq:
                var eq = Scalar(field);
                return row => row.Get(column).Contains(eq, StringComparer.Ordinal);
            case QueryOps.Ne:
                var ne = Scalar(field);
                return row => !row.Get(column).Contains(ne, StringComparer.Ordinal);
            case QueryOps.In or QueryOps.Has:
                var wanted = List(field).ToHashSet(StringComparer.Ordinal);
                return row => row.Get(column).Any(wanted.Contains);
            case QueryOps.Nin:
                var unwanted = List(field).ToHashSet(StringComparer.Ordinal);
                return row => !row.Get(column).Any(unwanted.Contains);
            case QueryOps.Lt or QueryOps.Gt:
                if (!long.TryParse(Scalar(field), NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit))
                    throw new ArgumentException("Expected an integer.");
                var below = field.Op == QueryOps.Lt;
                return row => row.Get(column) is { Count: > 0 } items &&
                    long.TryParse(items[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
                    (below ? value < limit : value > limit);
            case QueryOps.Regex or QueryOps.Contains:
                var pattern = Scalar(field);
                if (pattern.Length > FormQuery.MaximumRegexLength) throw new ArgumentException("Regular expression is too long.");
                var source = field.Op == QueryOps.Regex ? @"\A(?:" + pattern + @")\z" : pattern;
                Regex regex;
                try
                {
                    // The pattern alone must be valid, so an unbalanced ")" cannot break out of the anchor.
                    _ = new Regex(pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);
                    regex = new Regex(source, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);
                }
                catch (Exception e) when (e is ArgumentException or NotSupportedException)
                { throw new ArgumentException("Invalid non-backtracking regular expression.", e); }
                return row => row.Get(column).Any(regex.IsMatch);
            case QueryOps.Exists:
                var wantsValue = field.Value is not false;
                return row => row.Get(column).Any(item => item.Length > 0) == wantsValue;
            default:
                throw new ArgumentException("Unknown query operator.");
        }
    }

    private static string Scalar(QueryField field) => field.Value switch
    {
        string text => text,
        long number => number.ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentException($"The {field.Op} op takes one value."),
    };

    private static string[] List(QueryField field) => field.Value is IReadOnlyList<string> list
        ? [.. list] : throw new ArgumentException($"The {field.Op} op takes a list of values.");
}
