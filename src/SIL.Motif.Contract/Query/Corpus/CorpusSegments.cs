using System.Text;

namespace SIL.Motif.Contract.Query.Corpus;

/// <summary>
/// The multi-character graphemes of a corpus's orthography, read from an optional segment file.
/// </summary>
/// <remarks>
/// The file is plain UTF-8 text. Each line lists graphemes separated by spaces, tabs or commas, for example
/// <c>ng ny ch</c>. Blank lines are skipped and <c>#</c> starts a comment that runs to the end of the line.
/// Entries are normalised to NFC. The longest entry that matches at a position wins. A word is split into
/// these graphemes and, wherever none matches, into single code points. With no file, every segment is a code
/// point and the corpus labels its segmentation <see cref="CorpusWordList.ApproximateSegmentation"/>.
/// </remarks>
public sealed class CorpusSegments
{
    /// <summary>No graphemes: segments are code points.</summary>
    public static CorpusSegments None { get; } = new([]);

    private CorpusSegments(IReadOnlyList<string> graphemes)
    {
        Graphemes = graphemes;
        var index = new Dictionary<char, List<string>>();
        foreach (var grapheme in graphemes.Where(grapheme => grapheme.Length > 1))
        {
            if (!index.TryGetValue(grapheme[0], out var list)) index[grapheme[0]] = list = [];
            list.Add(grapheme);
        }
        Index = index.ToDictionary(pair => pair.Key,
            pair => pair.Value.OrderByDescending(grapheme => grapheme.Length).ThenBy(grapheme => grapheme, StringComparer.Ordinal).ToArray());
    }

    /// <summary>The graphemes the file listed, NFC-normalised, distinct, in ordinal order.</summary>
    public IReadOnlyList<string> Graphemes { get; }

    /// <summary>True when no multi-character grapheme is listed.</summary>
    public bool IsEmpty => Index.Count == 0;

    internal IReadOnlyDictionary<char, string[]> Index { get; }

    /// <summary>Parses the segment file text.</summary>
    public static CorpusSegments Parse(string text)
    {
        var graphemes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine;
            if (line.IndexOf('#') is var hash and >= 0) line = line[..hash];
            foreach (var entry in line.Split([' ', '\t', ',', '\r'], StringSplitOptions.RemoveEmptyEntries))
                graphemes.Add(entry.Normalize(NormalizationForm.FormC));
        }
        return graphemes.Count == 0 ? None : new CorpusSegments([.. graphemes]);
    }

    /// <summary>Reads and parses a segment file.</summary>
    public static CorpusSegments Load(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));

    /// <summary>Splits a form into graphemes, longest listed match first, otherwise code points.</summary>
    public List<string> Segment(string form) => TextColumns.Tokenize(form, Index);
}
