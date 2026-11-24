using System.Collections.Frozen;

namespace SIL.Motif.Contract.Query;

/// <summary>The ops of a field predicate and the kinds of a v2 query expression.</summary>
public static class QueryOps
{
    /// <summary>The row has the value among its items.</summary>
    public const string Eq = "eq";
    /// <summary>The row lacks the value among its items.</summary>
    public const string Ne = "ne";
    /// <summary>Some item is one of the listed values.</summary>
    public const string In = "in";
    /// <summary>No item is one of the listed values.</summary>
    public const string Nin = "nin";
    /// <summary>The numeric column is below the value.</summary>
    public const string Lt = "lt";
    /// <summary>The numeric column is above the value.</summary>
    public const string Gt = "gt";
    /// <summary>The set column holds one of the listed values.</summary>
    public const string Has = "has";
    /// <summary>Some item matches the regular expression from its first to its last character.</summary>
    public const string Regex = "regex";
    /// <summary>Some item contains a match of the regular expression anywhere.</summary>
    public const string Contains = "contains";
    /// <summary>The column holds a value (true) or none (false).</summary>
    public const string Exists = "exists";

    /// <summary>Every op, in the order the schema lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [Eq, Ne, In, Nin, Lt, Gt, Has, Regex, Contains, Exists];

    /// <summary>The ops whose value is a list of strings.</summary>
    public static bool TakesList(string op) => op is In or Nin or Has;

    /// <summary>The kinds reserved for later phases: the schema names them so it stays stable, and a query that uses one is refused.</summary>
    public static IReadOnlyList<string> ReservedKinds { get; } = ["morph", "seq", "realizes"];

    /// <summary>The pattern kinds, which PanGloss's <c>match</c> op runs over surface forms.</summary>
    public static IReadOnlyList<string> PatternKinds { get; } = ["phon", "env"];

    internal static readonly FrozenSet<string> Reserved = ReservedKinds.ToFrozenSet(StringComparer.Ordinal);
}

/// <summary>One node of a <c>motif.query.v2</c> boolean expression.</summary>
public abstract record QueryExpr;

/// <summary>Every inner expression holds; a canonical node has at least two.</summary>
public sealed record QueryAnd(IReadOnlyList<QueryExpr> Items) : QueryExpr;

/// <summary>Some inner expression holds; a canonical node has at least two.</summary>
public sealed record QueryOr(IReadOnlyList<QueryExpr> Items) : QueryExpr;

/// <summary>The inner expression does not hold.</summary>
public sealed record QueryNot(QueryExpr Item) : QueryExpr;

/// <summary>A predicate on one column.</summary>
/// <param name="Field">The column name.</param>
/// <param name="Op">One of <see cref="QueryOps.All"/>.</param>
/// <param name="Value">A string, a long (<c>lt</c>, <c>gt</c>), a bool (<c>exists</c>) or a <c>string[]</c> (<c>in</c>, <c>nin</c>, <c>has</c>).</param>
public sealed record QueryField(string Field, string Op, object Value) : QueryExpr;

/// <summary>A reserved kind (<c>morph</c>, <c>seq</c>, <c>realizes</c>): it parses, and running it is refused.</summary>
/// <param name="Kind">The reserved kind.</param>
/// <param name="Body">The kind's JSON body, kept so the document survives a round trip through the AST.</param>
public sealed record QueryReserved(string Kind, string Body) : QueryExpr;

/// <summary>A phonological pattern over the surface form: <c>{phon: "C V C V"}</c>, run by PanGloss's <c>match</c> op.</summary>
/// <param name="Pattern">The pattern text, as the person or agent wrote it.</param>
public sealed record QueryPhon(string Pattern) : QueryExpr;

/// <summary>An environment <c>target / left _ right</c> over the surface form: <c>{env: "[+voice] / # _ V"}</c>, run by PanGloss's <c>match</c> op.</summary>
/// <param name="Environment">The environment text, as the person or agent wrote it.</param>
public sealed record QueryEnv(string Environment) : QueryExpr;
