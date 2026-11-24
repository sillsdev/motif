using System.Text;
using System.Text.RegularExpressions;

namespace SIL.Motif.Contract.Query;

/// <summary>
/// Compiles the surface forms of a variant set to one search over a corpus's surface forms. The set is the seam between the
/// tiers and the corpus: <c>attested</c> feeds it now, and <c>possible</c> will feed it the same way.
/// </summary>
public static class RealizesPatternCompiler
{
    /// <summary>A word carries a form at the start, at the end or anywhere.</summary>
    public static IReadOnlyList<string> Anchors { get; } = ["start", "end", "none"];

    /// <summary>A search term that must sit at the start of the word: the marker leads the text.</summary>
    public const char StartMarker = '\uE000';

    /// <summary>A search term that must sit at the end of the word: the marker follows the text.</summary>
    public const char EndMarker = '\uE001';

    /// <summary>The marker (or none) a context-anchored term carries for <paramref name="anchor"/>.</summary>
    public static string Mark(string text, string anchor) => anchor switch
    {
        "start" => StartMarker + text,
        "end" => text + EndMarker,
        _ => text,
    };

    /// <summary>A term as a reader sees it: <c>^</c> for the start of the word and <c>$</c> for its end.</summary>
    public static string Show(string term) => term.Replace(StartMarker, '^').Replace(EndMarker, '$');

    /// <summary>The forms, as the set holds them, ready to search: NFC, no empty form, distinct, longest first so an alternation prefers the longer.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string> forms, bool foldCase = false) =>
    [
        .. forms.Select(form => foldCase ? form.Normalize(NormalizationForm.FormC).ToLowerInvariant() : form.Normalize(NormalizationForm.FormC))
            .Where(form => form.Length > 0).Distinct(StringComparer.Ordinal)
            .OrderByDescending(form => form.Length).ThenBy(form => form, StringComparer.Ordinal),
    ];

    /// <summary>The whole expression for the forms: one alternation with the anchor written in.</summary>
    public static string Compile(IReadOnlyList<string> forms, string anchor) => Wrap(forms, anchor);

    /// <summary>
    /// The expression cut into alternations of at most <paramref name="limit"/> characters each, so each fits a field predicate;
    /// a word matches the set when it matches one of them. A single form longer than the limit is refused.
    /// </summary>
    public static IReadOnlyList<string> Chunks(IReadOnlyList<string> forms, string anchor, int limit)
    {
        var chunks = new List<string>();
        var current = new List<string>();
        foreach (var form in forms)
        {
            current.Add(form);
            if (Wrap(current, anchor).Length <= limit) continue;
            current.RemoveAt(current.Count - 1);
            if (current.Count == 0) throw new ArgumentException("A form is too long to search for.");
            chunks.Add(Wrap(current, anchor));
            current = [form];
            if (Wrap(current, anchor).Length > limit) throw new ArgumentException("A form is too long to search for.");
        }
        if (current.Count > 0) chunks.Add(Wrap(current, anchor));
        return chunks;
    }

    /// <summary>Whether the word carries the form at the anchor, the same test the expression makes.</summary>
    public static bool Carries(string word, string form, string anchor)
    {
        var atStart = form.StartsWith(StartMarker);
        var atEnd = form.EndsWith(EndMarker);
        if (atStart || atEnd)
        {
            var text = form.Trim(StartMarker, EndMarker);
            if (atStart && atEnd) return string.Equals(word, text, StringComparison.Ordinal);
            return atStart ? word.StartsWith(text, StringComparison.Ordinal) : word.EndsWith(text, StringComparison.Ordinal);
        }
        return anchor switch
        {
            "start" => word.StartsWith(form, StringComparison.Ordinal),
            "end" => word.EndsWith(form, StringComparison.Ordinal),
            _ => word.Contains(form, StringComparison.Ordinal),
        };
    }

    /// <summary>The compiled expression as it ships in a result.</summary>
    public static RealizesPattern Pattern(IReadOnlyList<string> forms, string anchor) =>
        new("regex", Compile(forms, anchor), anchor, "approximate");

    private static string Wrap(IReadOnlyList<string> forms, string anchor)
    {
        var body = "(?:" + string.Join("|", forms.Select(Term)) + ")";
        return anchor switch { "start" => @"\A" + body, "end" => body + @"\z", _ => body };
    }

    // A term carries its own anchors as markers; a plain form is escaped and left to the set's anchor.
    private static string Term(string form)
    {
        var text = form.Trim(StartMarker, EndMarker);
        return (form.StartsWith(StartMarker) ? @"\A" : "") + Regex.Escape(text) + (form.EndsWith(EndMarker) ? @"\z" : "");
    }
}
