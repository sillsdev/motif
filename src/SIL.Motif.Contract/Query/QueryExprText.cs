using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Commands;

namespace SIL.Motif.Contract.Query;

/// <summary>
/// The text form of a query expression: <c>field op value</c> predicates joined by <c>and</c>, <c>or</c>, <c>not</c> and
/// parentheses. <c>not</c> binds tighter than <c>and</c>, which binds tighter than <c>or</c>. A value is a bare word with no
/// spaces, parentheses or quotes, or a double-quoted string; the list ops take comma-separated values.
/// </summary>
public static class QueryExprText
{
    // The text of each op and the AST op it means; the printer writes the first entry that has the op.
    private static readonly (string Text, string Op)[] OpTexts =
    [
        ("is", QueryOps.Eq), ("not in", QueryOps.Nin), ("not", QueryOps.Ne), ("in", QueryOps.In), ("has", QueryOps.Has),
        ("matches", QueryOps.Regex), ("~", QueryOps.Contains), ("<", QueryOps.Lt), (">", QueryOps.Gt), ("exists", QueryOps.Exists),
        ("missing", QueryOps.Exists), ("=", QueryOps.Eq), ("contains", QueryOps.Contains),
    ];

    /// <summary>Whether a text starts a boolean expression rather than a single v1 selector.</summary>
    public static bool LooksBoolean(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('(') || trimmed.StartsWith("not ", StringComparison.Ordinal)) return true;
        return Words(text).Any(word => word is "or" or "and");
    }

    private static IEnumerable<string> Words(string text) => text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Parses one expression; a malformed one is a <see cref="RefusalCodes.QueryDocumentInvalid"/> refusal that names the position.</summary>
    public static bool TryParse(string text, [NotNullWhen(true)] out QueryExpr? expr, [NotNullWhen(false)] out Refusal? refusal)
    {
        try
        {
            var parser = new Parser(text);
            expr = parser.ParseAll();
            refusal = null;
            return true;
        }
        catch (QueryRefusalException error)
        {
            expr = null;
            refusal = error.Refusal;
            return false;
        }
    }

    /// <summary>Parses one expression, throwing the refusal when it is malformed.</summary>
    public static QueryExpr Parse(string text) =>
        TryParse(text, out var expr, out var refusal) ? expr : throw new QueryRefusalException(refusal);

    /// <summary>Writes the canonical text of an expression; parsing it again gives the same expression.</summary>
    public static string Print(QueryExpr expr) => Print(expr, 0);

    // parent: 0 none, 2 under or, 3 under and or not; a nested and or or is parenthesised.
    private static string Print(QueryExpr expr, int parent)
    {
        switch (expr)
        {
            case QueryOr or:
                return Wrap(string.Join(" or ", or.Items.Select(item => Print(item, 2))), parent > 1);
            case QueryAnd and:
                return Wrap(string.Join(" and ", and.Items.Select(item => Print(item, 3))), parent > 2);
            case QueryNot not:
                return "not " + Print(not.Item, 3);
            case QueryField field:
                return PrintField(field);
            case QueryReserved reserved:
                throw QueryRefusalException.NotYetAvailable(reserved.Kind);
            default:
                throw new ArgumentException("Unknown query expression.");
        }
    }

    private static string Wrap(string text, bool parenthesise) => parenthesise ? "(" + text + ")" : text;

    private static string PrintField(QueryField field)
    {
        var name = field.Field;
        switch (field.Op)
        {
            case QueryOps.Exists:
                return name + (field.Value is false ? " missing" : " exists");
            case QueryOps.Nin:
                return name + " not in " + List(field);
            case QueryOps.In or QueryOps.Has:
                return name + " " + field.Op + " " + List(field);
        }
        var op = OpTexts.First(entry => entry.Op == field.Op && entry.Text != "missing").Text;
        var value = field.Value switch
        {
            long number => number.ToString(CultureInfo.InvariantCulture),
            string text => Quote(text, forList: false),
            _ => throw new ArgumentException($"The {field.Op} op takes one value."),
        };
        return name + " " + op + " " + value;
    }

    private static string List(QueryField field) => field.Value is IReadOnlyList<string> list
        ? string.Join(",", list.Select(item => Quote(item, forList: true)))
        : throw new ArgumentException($"The {field.Op} op takes a list of values.");

    private static string Quote(string value, bool forList)
    {
        var bare = value.Length > 0 && value.All(c => !char.IsWhiteSpace(c) && c is not ('(' or ')' or '"') && !(forList && c == ','));
        if (bare) return value;
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private sealed class Parser(string text)
    {
        private int _at;

        public QueryExpr ParseAll()
        {
            var expr = ParseOr();
            SkipSpace();
            if (_at < text.Length) throw Error("Expected end of the expression, 'and' or 'or'.");
            return expr;
        }

        private QueryExpr ParseOr()
        {
            var items = new List<QueryExpr> { ParseAnd() };
            while (TryWord("or")) items.Add(ParseAnd());
            return items.Count == 1 ? items[0] : new QueryOr(items);
        }

        private QueryExpr ParseAnd()
        {
            var items = new List<QueryExpr> { ParseUnary() };
            while (TryWord("and")) items.Add(ParseUnary());
            return items.Count == 1 ? items[0] : new QueryAnd(items);
        }

        private QueryExpr ParseUnary()
        {
            SkipSpace();
            if (TryWord("not")) return new QueryNot(ParseUnary());
            if (_at < text.Length && text[_at] == '(')
            {
                _at++;
                var inner = ParseOr();
                SkipSpace();
                if (_at >= text.Length || text[_at] != ')') throw Error("Expected ')'.");
                _at++;
                return inner;
            }
            return ParsePredicate();
        }

        private QueryExpr ParsePredicate()
        {
            SkipSpace();
            var start = _at;
            while (_at < text.Length && (char.IsLetterOrDigit(text[_at]) || text[_at] is '.' or '-' or '_' or ':')) _at++;
            var field = text[start.._at];
            if (field.Length == 0) throw Error("Expected a column name.");
            if (field is "and" or "or" or "not") throw Error($"'{field}' is a keyword, not a column name.");
            if (_at < text.Length && text[_at] == '(' && QueryOps.Reserved.Contains(field))
                throw QueryRefusalException.NotYetAvailable(field);
            SkipSpace();
            var op = ReadOp();
            if (op.Op == QueryOps.Exists) return new QueryField(field, QueryOps.Exists, op.Text != "missing");
            return new QueryField(field, op.Op, ReadValue(op.Op));
        }

        private (string Text, string Op) ReadOp()
        {
            foreach (var entry in OpTexts)
            {
                if (!text.AsSpan(_at, Math.Min(entry.Text.Length, text.Length - _at)).Equals(entry.Text, StringComparison.Ordinal))
                    continue;
                var end = _at + entry.Text.Length;
                var isWord = char.IsLetter(entry.Text[0]);
                if (isWord && end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] is not ('(' or ')')) continue;
                _at = end;
                return entry;
            }
            throw Error("Expected an operator: is, not, in, not in, has, matches, ~, <, >, exists or missing.");
        }

        private object ReadValue(string op)
        {
            SkipSpace();
            if (QueryOps.TakesList(op))
            {
                var items = new List<string> { ReadOne(forList: true) };
                while (true)
                {
                    var save = _at;
                    SkipSpace();
                    if (_at < text.Length && text[_at] == ',') { _at++; SkipSpace(); items.Add(ReadOne(forList: true)); }
                    else { _at = save; break; }
                }
                return items.ToArray();
            }
            var value = ReadOne(forList: false);
            if (op is QueryOps.Lt or QueryOps.Gt)
                return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                    ? number : throw Error("Expected a whole number.");
            return value;
        }

        private string ReadOne(bool forList)
        {
            if (_at >= text.Length) throw Error("Expected a value.");
            var sb = new StringBuilder();
            if (text[_at] == '"')
            {
                _at++;
                while (true)
                {
                    if (_at >= text.Length) throw Error("A quote is never closed.");
                    var c = text[_at++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\' && _at < text.Length && text[_at] is '"' or '\\') c = text[_at++];
                    sb.Append(c);
                }
            }
            while (_at < text.Length && !char.IsWhiteSpace(text[_at]) && text[_at] is not ('(' or ')' or '"') &&
                   !(forList && text[_at] == ','))
                sb.Append(text[_at++]);
            return sb.Length == 0 ? throw Error("Expected a value.") : sb.ToString();
        }

        private bool TryWord(string word)
        {
            SkipSpace();
            var end = _at + word.Length;
            if (end > text.Length || !text.AsSpan(_at, word.Length).Equals(word, StringComparison.Ordinal)) return false;
            if (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] is not '(') return false;
            _at = end;
            return true;
        }

        private void SkipSpace()
        {
            while (_at < text.Length && char.IsWhiteSpace(text[_at])) _at++;
        }

        private QueryRefusalException Error(string message) => QueryRefusalException.DocumentInvalid(
            $"{message} (at character {_at + 1} of the expression)", "where");
    }
}
