using System.Text;

namespace SIL.Motif.Contract.Query.Phon;

/// <summary>A pattern ready to send to PanGloss: its text and, for an <c>env</c>, where the target sits in the span it matches.</summary>
/// <param name="Text">The pattern text PanGloss's <c>match</c> op reads, with natural classes in brackets.</param>
/// <param name="TargetOffset">For an <c>env</c>, the segments before the target in a match; null for <c>phon</c>, or when the left context has no fixed width.</param>
/// <param name="TargetWidth">For an <c>env</c>, the segments of the target; null for <c>phon</c>, or when the target has no fixed width.</param>
public sealed record PhonPlan(string Text, int? TargetOffset = null, int? TargetWidth = null)
{
    /// <summary>The key two predicates share when PanGloss would answer them identically.</summary>
    public string Key => Text;
}

/// <summary>
/// The pattern language of <c>phon</c> and <c>env</c>: natural-class names (<c>C</c>, <c>Nasal</c>, or <c>[Name with spaces]</c>),
/// feature bundles <c>[+voice -son]</c>, literal segments <c>/ŋ/</c>, <c>( )</c> optional, <c>( )*</c> repeated, <c>#</c> a word edge,
/// <c>+</c> a morpheme boundary and alpha variables <c>[αvoice]</c>. An <c>env</c> reads <c>target / left _ right</c>, with spaces around
/// the <c>/</c> and the <c>_</c>. Motif checks the shape; PanGloss, which owns the grammar, checks the names.
/// </summary>
public static class PhonPattern
{
    private enum Kind { Open, Close, CloseStar, Edge, Plus, Class, Bundle, Literal, Slash, Underscore }

    private readonly record struct Token(Kind Kind, string Text, int At);

    /// <summary>Compiles a <c>phon</c> pattern; a malformed one is a <see cref="SIL.Motif.Contract.Commands.RefusalCodes.QueryDocumentInvalid"/> refusal that names the position.</summary>
    public static PhonPlan CompilePhon(string pattern)
    {
        var tokens = Tokenize(pattern, "phon");
        foreach (var token in tokens)
            if (token.Kind is Kind.Slash or Kind.Underscore)
                throw Error("phon", pattern, token.At, "the / and _ of an environment belong to env, not phon.");
        CheckShape("phon", pattern, tokens);
        if (tokens.Count == 0) throw Error("phon", pattern, 0, "a pattern needs at least one item.");
        return new PhonPlan(Render(tokens));
    }

    /// <summary>Compiles an <c>env</c> of the form <c>target / left _ right</c> to the pattern <c>left target right</c>.</summary>
    public static PhonPlan CompileEnv(string env)
    {
        var tokens = Tokenize(env, "env");
        var slash = tokens.FindIndex(token => token.Kind == Kind.Slash);
        if (slash < 0 || tokens.Skip(slash + 1).Any(token => token.Kind == Kind.Slash))
            throw Error("env", env, 0, "an environment is <target> / <left> _ <right>, with one / that has spaces around it.");
        var score = tokens.FindIndex(slash + 1, token => token.Kind == Kind.Underscore);
        if (score < 0 || tokens.Skip(score + 1).Any(token => token.Kind == Kind.Underscore) ||
            tokens.Take(slash).Any(token => token.Kind == Kind.Underscore))
            throw Error("env", env, tokens[slash].At, "after the / comes <left> _ <right>, with one _ that has spaces around it.");
        var target = tokens.GetRange(0, slash);
        var left = tokens.GetRange(slash + 1, score - slash - 1);
        var right = tokens.GetRange(score + 1, tokens.Count - score - 1);
        if (target.Count == 0) throw Error("env", env, 0, "the target before the / is empty.");
        CheckShape("env", env, target);
        CheckShape("env", env, left);
        CheckShape("env", env, right);
        var all = new List<Token>(left.Count + target.Count + right.Count);
        all.AddRange(left);
        all.AddRange(target);
        all.AddRange(right);
        return new PhonPlan(Render(all), Width(left), Width(target));
    }

    /// <summary>The index just past the balanced <c>)</c> that closes a pattern whose text starts at <paramref name="start"/> (after the opening parenthesis), or -1.</summary>
    internal static int FindClose(string text, int start)
    {
        var depth = 1;
        for (var i = start; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '[':
                    var bracket = text.IndexOf(']', i + 1);
                    if (bracket < 0) return -1;
                    i = bracket;
                    break;
                case '/' when i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]):
                    var slash = text.IndexOf('/', i + 1);
                    if (slash < 0) return -1;
                    i = slash;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    if (--depth == 0) return i;
                    break;
            }
        }
        return -1;
    }

    private static int? Width(List<Token> tokens)
    {
        var width = 0;
        foreach (var token in tokens)
            switch (token.Kind)
            {
                case Kind.Class or Kind.Bundle: width++; break;
                case Kind.Edge or Kind.Plus: break;
                default: return null;
            }
        return width;
    }

    private static string Render(List<Token> tokens)
    {
        var text = new StringBuilder();
        foreach (var token in tokens)
        {
            if (text.Length > 0 && token.Kind != Kind.CloseStar && token.Kind != Kind.Close) text.Append(' ');
            switch (token.Kind)
            {
                case Kind.Open: text.Append('('); break;
                case Kind.Close: text.Append(')'); break;
                case Kind.CloseStar: text.Append(")*"); break;
                case Kind.Edge: text.Append('#'); break;
                case Kind.Plus: text.Append('+'); break;
                case Kind.Class or Kind.Bundle: text.Append('[').Append(token.Text).Append(']'); break;
                case Kind.Literal: text.Append('/').Append(token.Text).Append('/'); break;
            }
        }
        return text.ToString();
    }

    private static void CheckShape(string kind, string source, List<Token> tokens)
    {
        var depth = 0;
        var last = default(Token?);
        foreach (var token in tokens)
        {
            if (token.Kind == Kind.Open) depth++;
            else if (token.Kind is Kind.Close or Kind.CloseStar)
            {
                if (--depth < 0) throw Error(kind, source, token.At, "a ) has no matching (.");
                if (last is { Kind: Kind.Open }) throw Error(kind, source, token.At, "an empty ( ) group.");
            }
            last = token;
        }
        if (depth > 0) throw Error(kind, source, source.Length, "a ( is never closed.");
    }

    private static List<Token> Tokenize(string text, string kind)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            switch (c)
            {
                case '(':
                    tokens.Add(new Token(Kind.Open, "(", i++));
                    continue;
                case ')':
                    if (i + 1 < text.Length && text[i + 1] == '*')
                    {
                        tokens.Add(new Token(Kind.CloseStar, ")*", i));
                        i += 2;
                    }
                    else tokens.Add(new Token(Kind.Close, ")", i++));
                    continue;
                case '#':
                    tokens.Add(new Token(Kind.Edge, "#", i++));
                    continue;
                case '+':
                    tokens.Add(new Token(Kind.Plus, "+", i++));
                    continue;
                case '*':
                    throw Error(kind, text, i, "a * follows a ) only, as in ( ... )*.");
                case ']':
                    throw Error(kind, text, i, "a ] has no matching [.");
                case '[':
                    var close = text.IndexOf(']', i + 1);
                    if (close < 0) throw Error(kind, text, i, "a [ is never closed.");
                    var inner = text[(i + 1)..close].Trim();
                    if (inner.Length == 0) throw Error(kind, text, i, "an empty [ ].");
                    tokens.Add(new Token(IsBundle(inner) ? Kind.Bundle : Kind.Class, inner, i));
                    i = close + 1;
                    continue;
                case '/':
                    if (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1]))
                    {
                        tokens.Add(new Token(Kind.Slash, "/", i++));
                        continue;
                    }
                    var end = text.IndexOf('/', i + 1);
                    if (end < 0) throw Error(kind, text, i, "a literal /.../ is never closed; put spaces around the / of an environment.");
                    tokens.Add(new Token(Kind.Literal, text[(i + 1)..end], i));
                    i = end + 1;
                    continue;
            }
            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('(' or ')' or '[' or ']' or '#' or '+' or '/' or '*')) i++;
            var name = text[start..i];
            tokens.Add(name == "_" ? new Token(Kind.Underscore, "_", start) : new Token(Kind.Class, name, start));
        }
        return tokens;
    }

    // A bundle is signed features or alpha variables; any other bracketed text names a natural class.
    private static bool IsBundle(string inner) => inner[0] is '+' or '-' || (inner[0] >= 'α' && inner[0] <= 'ω');

    private static QueryRefusalException Error(string kind, string source, int at, string message) =>
        QueryRefusalException.DocumentInvalid($"The {kind} pattern '{source}' is malformed at character {at + 1}: {message}", "where");
}
