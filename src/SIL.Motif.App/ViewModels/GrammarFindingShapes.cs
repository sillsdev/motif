using System.Text.RegularExpressions;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Groups the parser's load warnings, which carry no code or name of their own, by the shape of the sentence
/// the parser wrote: the same words once the quoted identifiers, environments and positions are taken out.
/// The label is the parser's own wording, so Motif invents no names; once the parser names its warnings,
/// <see cref="SIL.Motif.Contract.Responses.GrammarWarning.Group"/> carries that name and this is not used.
/// </summary>
public static partial class GrammarFindingShapes
{
    /// <summary>The shared label for every warning written the same way as <paramref name="text"/>.</summary>
    public static string LabelOf(string text)
    {
        var shape = Prefix().Replace(text, string.Empty);
        shape = Code().Replace(shape, string.Empty);
        shape = Elide(Quoted(), shape);
        shape = Elide(Guid(), shape);
        // Environments nest brackets, so the innermost pair goes first until none is left.
        while (Parenthesised().IsMatch(shape)) shape = Parenthesised().Replace(shape, string.Empty);
        shape = Elide(Number(), shape);
        shape = LeadingSubject().Replace(shape, string.Empty);
        shape = RepeatedSegmenting().Replace(shape, "cannot segment: ");
        shape = Spaces().Replace(shape, " ");
        shape = SpaceBeforePunctuation().Replace(shape, "$1");
        // The first clause names the problem; what follows a semicolon or full stop says what to do about it.
        var head = SentenceEnd().Split(shape.Split(';', 2)[0], 2)[0].Trim().TrimEnd(':', '.').Trim();
        return head.Length == 0 ? "Other notes from loading the grammar" : char.ToUpperInvariant(head[0]) + head[1..];
    }

    // A value the sentence runs on past keeps its place as "…"; one that ends a clause is simply dropped.
    private static string Elide(Regex value, string shape) =>
        value.Replace(shape, match => ContinuesAfter().IsMatch(shape.AsSpan(match.Index + match.Length)) ? "…" : string.Empty);

    /// <summary>
    /// Whether the parser says it left something out of the grammar here — skipped it, refused it, or
    /// treated it as absent — which is what puts a finding among the ones to fix first.
    /// </summary>
    public static bool IsLeftOut(string text) => LeftOut().IsMatch(text);

    [GeneratedRegex(@"^\s*(warning|error|capability)\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex Prefix();

    // A kebab-case code such as hc-partial-morpheme, or a dotted grammar path such as morphology.adhocProhibitions.
    [GeneratedRegex(@"^\s*([a-z]+(-[a-z0-9]+)+|[a-z]+(\.[A-Za-z]+)+)\s*:\s*")]
    private static partial Regex Code();

    [GeneratedRegex(@"\.\s+")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"^(,|\s+\p{Ll})")]
    private static partial Regex ContinuesAfter();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex Guid();

    [GeneratedRegex("\"[^\"]*\"|'[^']*'")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"\([^()]*\)")]
    private static partial Regex Parenthesised();

    [GeneratedRegex(@"\b\d+\b")]
    private static partial Regex Number();

    [GeneratedRegex(@"^\s*(lex entry|allomorph|phoneme|natural class|boundary|msa|sense|circumfix entry)(\s*…)?(\s+(lex entry|allomorph|sense|msa)(\s*…)?)*\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingSubject();

    [GeneratedRegex(@"(cannot segment\s*:\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatedSegmenting();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s+([:;,.])")]
    private static partial Regex SpaceBeforePunctuation();

    [GeneratedRegex(@"skipped|treated as absent|refusing|zero loadable|failed (validation|to parse)|does not resolve|no non-empty form|has no slots", RegexOptions.IgnoreCase)]
    private static partial Regex LeftOut();
}
