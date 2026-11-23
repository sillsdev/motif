using System.Globalization;
using System.Text;

namespace SIL.Motif.Contract.Query;

/// <summary>The sources a query can read and the columns each stores.</summary>
public static class QuerySources
{
    /// <summary>The source of lexeme, alternate and variant forms.</summary>
    public const string Forms = "forms";
    /// <summary>The source of stem MSAs.</summary>
    public const string Stems = "stems";
    /// <summary>The source of references from a user to a statement.</summary>
    public const string Uses = "uses";
    /// <summary>The source of grammar objects with their use counts.</summary>
    public const string Objects = "objects";

    /// <summary>Every source name.</summary>
    public static IReadOnlyList<string> All { get; } = [Forms, Stems, Uses, Objects];

    /// <summary>The stored columns of a stems, uses or objects row, in storage order.</summary>
    public static IReadOnlyList<string> StoredColumns(string source) => source switch
    {
        Stems => ["form", "entry", "category", "features", "gloss", "msa-count", "alternates"],
        Uses => ["target-kind", "target", "user-kind", "user", "role", "effect"],
        Objects => ["class", "label", "uses", "applied", "carriers", "users", "key"],
        _ => throw new ArgumentException("Unknown query source."),
    };

    /// <summary>The stored columns that hold several values per row.</summary>
    public static bool IsStoredSet(string source, string column) =>
        source == Stems && column is "category" or "features";

    internal static bool IsTextColumn(string column) =>
        column is "shape" or "syllables" or "fold" || IsEdge(column, out _, out _);

    internal static bool IsEdge(string column, out bool initial, out int count)
    {
        initial = column.StartsWith("initial:", StringComparison.Ordinal);
        count = 0;
        return (initial || column.StartsWith("final:", StringComparison.Ordinal)) &&
               int.TryParse(column.AsSpan(column.IndexOf(':') + 1), NumberStyles.None, CultureInfo.InvariantCulture, out count) &&
               count is >= 1 and <= 8;
    }

    internal static bool IsColumn(string source, string column) => column == "id" || IsTextSource(source) && IsTextColumn(column) ||
        source != Forms && StoredColumns(source).Contains(column);

    private static bool IsTextSource(string source) => source is Forms or Stems;

    internal static bool IsNumeric(string source, string column) => column switch
    {
        "tokens" => source == Forms,
        "syllables" => IsTextSource(source),
        "msa-count" or "alternates" => source == Stems,
        "uses" or "applied" or "carriers" or "users" => source == Objects,
        _ => false,
    };

    /// <summary>Every column a source accepts in a predicate, with its type: <c>text</c>, <c>set</c> or <c>numeric</c>; ordered by name.</summary>
    public static IReadOnlyList<(string Name, string Type)> Columns(string source)
    {
        var names = new List<string> { "id" };
        if (source == Forms) names.AddRange(FormQuery.FormColumnNames);
        else names.AddRange(StoredColumns(source));
        if (IsTextSource(source))
        {
            names.AddRange(["shape", "syllables", "fold"]);
            for (var count = 1; count <= 8; count++) names.AddRange([$"initial:{count}", $"final:{count}"]);
        }
        return [.. names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(name => (name,
            IsNumeric(source, name) ? "numeric" : (source == Forms ? FormQuery.IsFormSet(name) : IsStoredSet(source, name)) ? "set" : "text"))];
    }

    internal static string SortColumn(string source) => source switch { Stems => "form", Uses => "target", _ => "label" };
}

/// <summary>The segments of one text, tokenised with the project's grapheme table, and the columns derived from them.</summary>
internal sealed class TextColumns(string text, IReadOnlyDictionary<char, string[]> graphemes, Func<string, string> fold)
{
    private List<string>? _segments;

    public List<string> Segments => _segments ??= Tokenize(text, graphemes);

    public IReadOnlyList<string> Get(string column)
    {
        switch (column)
        {
            case "fold": return [fold(text)];
            case "shape": return [string.Concat(Segments.Select(Skeleton))];
            case "syllables": return [Segments.Count(IsVowel).ToString(CultureInfo.InvariantCulture)];
        }
        if (!QuerySources.IsEdge(column, out var initial, out var count)) throw new ArgumentException("Unknown query column.");
        if (Segments.Count < count) return [];
        return [string.Concat(initial ? Segments.Take(count) : Segments.Skip(Segments.Count - count))];
    }

    // The vowel convention of StringCorrespondence: a, e, i, o, u on the segment's NFD base letter.
    private static bool IsVowel(string segment) => segment.Length > 0 &&
        segment.Normalize(NormalizationForm.FormD)[0] is var c && char.IsLetter(c) && "aeiou".Contains(char.ToLowerInvariant(c));

    private static string Skeleton(string segment) => IsVowel(segment) ? "V" : char.IsLetter(segment[0]) ? "C" : segment;

    internal static List<string> Tokenize(string text, IReadOnlyDictionary<char, string[]> graphemes)
    {
        var result = new List<string>();
        for (var i = 0; i < text.Length;)
        {
            string? match = null;
            if (graphemes.TryGetValue(text[i], out var candidates))
                foreach (var candidate in candidates)
                    if (text.AsSpan(i).StartsWith(candidate, StringComparison.Ordinal)) { match = candidate; break; }
            match ??= Rune.GetRuneAt(text, i).ToString();
            result.Add(match);
            i += match.Length;
        }
        return result;
    }
}
