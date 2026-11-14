namespace SIL.Motif.Contract.Parsimony;

/// <summary>
/// One orthographic fold a project declares for near-duplicate lexemes, written <c>from&gt;to</c> in
/// <c>[parsimony] fold</c>: after lower-casing, every <paramref name="From"/> becomes <paramref name="To"/>.
/// Cebuano's setting is <c>iy&gt;y</c>. There is no built-in rule.
/// </summary>
/// <param name="From">The non-empty text to replace.</param>
/// <param name="To">The replacement, possibly empty.</param>
public sealed record FoldRule(string From, string To)
{
    /// <summary>Reads <c>from&gt;to</c>; false when there is no <c>&gt;</c> or the left side is empty.</summary>
    /// <param name="text">The rule text.</param>
    /// <param name="rule">The parsed rule.</param>
    public static bool TryParse(string text, out FoldRule rule)
    {
        var at = text.IndexOf('>', StringComparison.Ordinal);
        rule = at > 0 ? new FoldRule(text[..at], text[(at + 1)..]) : new FoldRule("", "");
        return at > 0;
    }

    /// <summary>The <c>from&gt;to</c> spelling.</summary>
    public override string ToString() => From + ">" + To;
}
