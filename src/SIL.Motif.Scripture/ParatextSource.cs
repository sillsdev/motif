using SIL.Scripture;

namespace SIL.Motif.Scripture;

/// <summary>One chapter of a Source: the standard three-letter book ID and the chapter number.</summary>
public readonly record struct ChapterRef(string Book, int Chapter)
{
    /// <summary>The chapter written as <c>MAT 5</c>.</summary>
    public override string ToString() => $"{Book} {Chapter}";

    /// <summary>Reads a chapter written as a book ID, a space and a chapter number, such as <c>MAT 5</c>.</summary>
    public static bool TryParse(string? text, out ChapterRef chapter)
    {
        chapter = default;
        var parts = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var number) || number < 1) return false;
        var book = parts[0].ToUpperInvariant();
        if (Canon.BookIdToNumber(book) <= 0) return false;
        chapter = new ChapterRef(book, number);
        return true;
    }
}

/// <summary>What Motif knows about a Paratext project folder: its identity, versification and the chapters it holds.</summary>
/// <param name="Name">The short project name from Settings.xml.</param>
/// <param name="Guid">The project GUID from Settings.xml, empty when the file has none.</param>
/// <param name="Versification">The versification the project's verse references follow.</param>
/// <param name="ChaptersByBook">The chapters with verse text per book, books in canonical order, chapters ascending.</param>
public sealed record ParatextSource(
    string Name,
    string Guid,
    ScrVers Versification,
    IReadOnlyDictionary<string, IReadOnlyList<int>> ChaptersByBook)
{
    /// <summary>Every chapter of the project in canonical order.</summary>
    public IEnumerable<ChapterRef> Chapters =>
        ChaptersByBook.SelectMany(book => book.Value.Select(chapter => new ChapterRef(book.Key, chapter)));

    /// <summary>Whether the project holds verse text for this chapter.</summary>
    public bool HasChapter(ChapterRef chapter) =>
        ChaptersByBook.TryGetValue(chapter.Book, out var chapters) && chapters.Contains(chapter.Chapter);
}
