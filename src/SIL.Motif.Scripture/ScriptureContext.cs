using System.Text;
using System.Text.RegularExpressions;
using SIL.Machine.Scripture;
using SIL.Scripture;

namespace SIL.Motif.Scripture;

/// <summary>
/// The Context of a Source: the books and chapters a linguist is working through, written as a range
/// string such as <c>GEN-ROM; 1CO 13-15</c>.
/// </summary>
/// <remarks>
/// Parsing is Machine's <see cref="ScriptureRangeParser"/>, which this type guards: only upper-case
/// standard three-letter book IDs are accepted, so <c>gen</c> and <c>1COR</c> are refused with a message
/// that names the standard ID. The parser's own quirks stay visible: <c>;</c> separates parts (a comma only separates chapters within a book
/// and cannot join two books), and a chapter part after a book range replaces that book's whole-book selection,
/// so <c>GEN-ROM; 1CO 13-15</c> keeps only chapters 13 to 15 of 1CO. Whole books
/// are expanded to chapter numbers using the Source's versification, so <see cref="Contains"/> is a plain
/// lookup and the last chapter of a book follows the versification.
/// </remarks>
public sealed class ScriptureContext
{
    private static readonly Regex WordPattern = new(@"[A-Za-z0-9]*[A-Za-z][A-Za-z0-9]*", RegexOptions.Compiled);
    private static readonly string[] Groups = ["OT", "NT", "DC"];

    private readonly SortedDictionary<int, int[]> _chapters;
    private readonly ScrVers _versification;

    private ScriptureContext(string text, ScrVers versification, SortedDictionary<int, int[]> chapters)
    {
        Text = text;
        _versification = versification;
        _chapters = chapters;
    }

    /// <summary>The range string as the linguist wrote it, trimmed.</summary>
    public string Text { get; }

    /// <summary>The standard three-letter IDs of the books that have at least one chapter, in canonical order.</summary>
    public IReadOnlyList<string> Books => _chapters.Keys.Select(Canon.BookNumberToId).ToList();

    /// <summary>Parses a Context, throwing <see cref="FormatException"/> with a readable reason when it is refused.</summary>
    public static ScriptureContext Parse(string text, ScrVers versification) =>
        TryParse(text, versification, out var context, out var error)
            ? context!
            : throw new FormatException(error);

    /// <summary>Parses a Context; on refusal returns false and sets <paramref name="error"/> to a readable reason.</summary>
    public static bool TryParse(string? text, ScrVers versification, out ScriptureContext? context, out string error)
    {
        context = null;
        error = string.Empty;
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            error = "A Context cannot be empty. Write book IDs such as GEN-ROM; 1CO 13-15, or NT.";
            return false;
        }

        var badId = FirstBadBookId(trimmed);
        if (badId is not null)
        {
            error = $"'{badId}' is not a standard book ID. Use the upper-case three-letter ID, such as GEN or 1CO; " +
                    "lower case and aliases such as 1COR are not accepted.";
            return false;
        }

        Dictionary<string, List<int>> parsed;
        try
        {
            if (!ScriptureRangeParser.TryGetChapters(trimmed, versification, out parsed!))
            {
                error = $"'{trimmed}' is not a valid Context. Check that every book is selected before a chapter " +
                        "is removed from it, and that chapter numbers exist in the versification.";
                return false;
            }
        }
        catch (ArgumentException ex)
        {
            error = $"'{trimmed}' is not a valid Context: {ex.Message}";
            return false;
        }

        var expanded = new SortedDictionary<int, int[]>();
        foreach (var (id, list) in parsed)
        {
            var book = Canon.BookIdToNumber(id);
            var last = versification.GetLastChapter(book);
            var chapters = list.Count == 0 ? Enumerable.Range(1, last) : list.Where(c => c >= 1 && c <= last);
            var kept = chapters.Distinct().OrderBy(c => c).ToArray();
            if (kept.Length > 0) expanded[book] = kept;
        }

        if (expanded.Count == 0)
        {
            error = $"'{trimmed}' selects no chapters.";
            return false;
        }

        context = new ScriptureContext(trimmed, versification, expanded);
        return true;
    }

    /// <summary>The chapter numbers of a book in this Context, empty when the book is not in it.</summary>
    public IReadOnlyList<int> ChaptersOf(string bookId)
    {
        var number = Canon.BookIdToNumber(bookId);
        return _chapters.TryGetValue(number, out var chapters) ? chapters : [];
    }

    /// <summary>Whether the chapter of the book is in this Context.</summary>
    public bool Contains(string bookId, int chapter) =>
        Canon.BookIdToNumber(bookId) is var number && number > 0 && _chapters.TryGetValue(number, out var chapters) &&
        Array.BinarySearch(chapters, chapter) >= 0;

    /// <summary>
    /// A canonical text for the same chapters: books in canonical order, a whole book as its ID alone, and
    /// chapter runs joined with a hyphen. Two Contexts that select the same chapters normalize alike.
    /// </summary>
    public string Normalize()
    {
        var parts = new List<string>();
        foreach (var (number, chapters) in _chapters)
        {
            var id = Canon.BookNumberToId(number);
            if (chapters.Length == _versification.GetLastChapter(number) && chapters[0] == 1)
            {
                parts.Add(id);
                continue;
            }

            parts.Add($"{id} {Runs(chapters)}");
        }

        return string.Join("; ", parts);
    }

    private static string Runs(int[] chapters)
    {
        var text = new StringBuilder();
        for (var i = 0; i < chapters.Length; i++)
        {
            var start = i;
            while (i + 1 < chapters.Length && chapters[i + 1] == chapters[i] + 1) i++;
            if (text.Length > 0) text.Append(", ");
            text.Append(i == start ? chapters[i].ToString() : $"{chapters[start]}-{chapters[i]}");
        }

        return text.ToString();
    }

    private static string? FirstBadBookId(string text)
    {
        foreach (Match match in WordPattern.Matches(text))
        {
            var word = match.Value;
            if (Groups.Contains(word, StringComparer.Ordinal)) continue;
            if (word.Length == 3 && word == word.ToUpperInvariant() && Canon.IsCanonical(Canon.BookIdToNumber(word))) continue;
            return word;
        }

        return null;
    }
}
