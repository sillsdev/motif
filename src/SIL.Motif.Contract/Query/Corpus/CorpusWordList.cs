using System.Globalization;
using System.Text;

namespace SIL.Motif.Contract.Query.Corpus;

/// <summary>One word type of a corpus with its token count and, when the list carried one, its parse state.</summary>
public sealed record CorpusEntry(string Form, long Tokens, string? ParseState = null);

/// <summary>How a corpus is read.</summary>
/// <param name="FoldCase">Lower-case every form (invariant culture) so spellings differing only in case are one type.</param>
/// <param name="Segments">The orthography's multi-character graphemes, or <see cref="CorpusSegments.None"/>.</param>
/// <param name="SplitHyphens">Split running text at a hyphen-minus between letters; by default it stays inside the word.</param>
public sealed record CorpusOptions(bool FoldCase = false, CorpusSegments? Segments = null, bool SplitHyphens = false);

/// <summary>A corrupt corpus file: the message names the file and line.</summary>
public sealed class CorpusFormatException(string message) : Exception(message);

/// <summary>
/// A plain word list read from a TSV or a folder of text files: the types and token counts of surface forms, with no
/// project and no parse needed. Forms are NFC. Types are in ordinal order, so every analysis over a
/// corpus is deterministic.
/// </summary>
public sealed class CorpusWordList
{
    /// <summary>The segmentation label when no segment file names the orthography's graphemes.</summary>
    public const string ApproximateSegmentation = "approximate";

    /// <summary>The segmentation label when a segment file supplies multi-character graphemes.</summary>
    public const string SegmentFileSegmentation = "segment-file";

    private readonly Dictionary<string, CorpusEntry> _byForm;

    private CorpusWordList(List<CorpusEntry> words, CorpusOptions options)
    {
        Words = words;
        Options = options;
        Segments = options.Segments ?? CorpusSegments.None;
        _byForm = words.ToDictionary(word => word.Form, StringComparer.Ordinal);
        TokenCount = words.Sum(word => word.Tokens);
    }

    /// <summary>The word types in ordinal form order.</summary>
    public IReadOnlyList<CorpusEntry> Words { get; }

    /// <summary>The options the corpus was read with.</summary>
    public CorpusOptions Options { get; }

    /// <summary>The graphemes used to segment forms.</summary>
    public CorpusSegments Segments { get; }

    /// <summary>The number of word types.</summary>
    public int TypeCount => Words.Count;

    /// <summary>The sum of the token counts.</summary>
    public long TokenCount { get; }

    /// <summary><see cref="ApproximateSegmentation"/> unless a segment file lists multi-character graphemes.</summary>
    public string SegmentationLabel => Segments.IsEmpty ? ApproximateSegmentation : SegmentFileSegmentation;

    /// <summary>Whether a form is a type of the corpus (after the corpus's own normalisation).</summary>
    public bool TryGet(string form, out CorpusEntry word) => _byForm.TryGetValue(form, out word!);

    /// <summary>Builds a corpus from counted forms: NFC, optional case folding, equal forms merged.</summary>
    public static CorpusWordList FromCounts(IEnumerable<CorpusEntry> words, CorpusOptions? options = null)
    {
        options ??= new CorpusOptions();
        var merged = new Dictionary<string, CorpusEntry>(StringComparer.Ordinal);
        foreach (var word in words)
        {
            var form = Normalize(word.Form, options.FoldCase);
            if (form.Length == 0 || word.Tokens <= 0) continue;
            merged[form] = merged.TryGetValue(form, out var existing)
                ? existing with { Tokens = existing.Tokens + word.Tokens, ParseState = existing.ParseState ?? word.ParseState }
                : new CorpusEntry(form, word.Tokens, word.ParseState);
        }
        var list = merged.Values.ToList();
        list.Sort((left, right) => string.CompareOrdinal(left.Form, right.Form));
        return new CorpusWordList(list, options);
    }

    /// <summary>Reads a TSV file or a folder of <c>.txt</c> files.</summary>
    public static CorpusWordList Load(string path, CorpusOptions? options = null)
    {
        if (Directory.Exists(path)) return LoadFolder(path, options);
        if (File.Exists(path)) return LoadTsv(path, options);
        throw new FileNotFoundException("The corpus path is neither a file nor a folder.", path);
    }

    /// <summary>
    /// Reads <c>form&lt;TAB&gt;count&lt;TAB&gt;state</c> lines. A first line whose first cell is <c>form</c> or <c>word</c> is a header and names
    /// the columns (<c>count</c>, <c>tokens</c> or <c>frequency</c> for the count; <c>state</c> or <c>parse.state</c> for the parse state).
    /// Without a header the columns are form, count, state. A missing count is 1. Blank lines and lines starting <c>#</c> are skipped.
    /// </summary>
    public static CorpusWordList LoadTsv(string path, CorpusOptions? options = null) =>
        ParseTsv(File.ReadAllText(path, Encoding.UTF8), options, path);

    /// <summary>Parses TSV text; <paramref name="source"/> names the file in errors.</summary>
    public static CorpusWordList ParseTsv(string text, CorpusOptions? options = null, string source = "corpus")
    {
        var words = new List<CorpusEntry>();
        int formColumn = 0, countColumn = 1, stateColumn = 2;
        var lineNumber = 0;
        var first = true;
        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.TrimEnd('\r');
            if (line.Trim().Length == 0 || line.StartsWith('#')) continue;
            var cells = line.Split('\t');
            if (first)
            {
                first = false;
                if (cells[0].Trim().ToLowerInvariant() is "form" or "word" or "wordform")
                {
                    formColumn = 0;
                    countColumn = stateColumn = -1;
                    for (var i = 0; i < cells.Length; i++)
                        switch (cells[i].Trim().ToLowerInvariant())
                        {
                            case "count" or "tokens" or "frequency" or "freq": countColumn = i; break;
                            case "state" or "parse.state" or "parse-state": stateColumn = i; break;
                        }
                    continue;
                }
            }
            var form = cells[formColumn].Trim();
            long tokens = 1;
            if (countColumn >= 0 && countColumn < cells.Length && cells[countColumn].Trim() is { Length: > 0 } count &&
                !long.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out tokens))
                throw new CorpusFormatException($"{source} line {lineNumber}: the count '{count}' is not a whole number.");
            var state = stateColumn >= 0 && stateColumn < cells.Length && cells[stateColumn].Trim() is { Length: > 0 } s ? s : null;
            words.Add(new CorpusEntry(form, tokens, state));
        }
        return FromCounts(words, options);
    }

    /// <summary>Tokenises every <c>.txt</c> file of a folder (recursively, in ordinal path order) and counts types and tokens.</summary>
    public static CorpusWordList LoadFolder(string folder, CorpusOptions? options = null)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(folder, "*.txt", SearchOption.AllDirectories).OrderBy(file => file, StringComparer.Ordinal))
            foreach (var token in Tokenize(File.ReadAllText(file, Encoding.UTF8), options?.FoldCase ?? false, options?.SplitHyphens ?? false))
                counts[token] = counts.GetValueOrDefault(token) + 1;
        return FromCounts(counts.Select(pair => new CorpusEntry(pair.Key, pair.Value)), options);
    }

    /// <summary>
    /// Splits running text into words: maximal runs of Unicode letters and combining marks, NFC. An apostrophe (U+0027,
    /// U+2019, U+02BC) or, unless <paramref name="splitHyphens"/>, a hyphen-minus with a letter on both sides stays inside the word.
    /// </summary>
    public static IEnumerable<string> Tokenize(string text, bool foldCase = false, bool splitHyphens = false)
    {
        var runes = text.Normalize(NormalizationForm.FormC).EnumerateRunes().ToArray();
        var builder = new StringBuilder();
        for (var i = 0; i < runes.Length; i++)
        {
            var rune = runes[i];
            var joiner = rune.Value is 0x27 or 0x2019 or 0x2BC || (rune.Value == '-' && !splitHyphens);
            if (IsWordRune(rune) || (joiner && builder.Length > 0 && i + 1 < runes.Length && IsWordRune(runes[i + 1])))
            { builder.Append(rune.ToString()); continue; }
            if (builder.Length > 0) { yield return Normalize(builder.ToString(), foldCase); builder.Clear(); }
        }
        if (builder.Length > 0) yield return Normalize(builder.ToString(), foldCase);
    }

    private static bool IsWordRune(Rune rune) => Rune.IsLetter(rune) || Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private static string Normalize(string form, bool foldCase)
    {
        var nfc = form.Normalize(NormalizationForm.FormC);
        return foldCase ? nfc.ToLowerInvariant().Normalize(NormalizationForm.FormC) : nfc;
    }

    /// <summary>
    /// A word as a row of the <c>forms</c> source: <c>form</c>, <c>tokens</c> (the frequency), <c>shape</c>, <c>syllables</c>,
    /// <c>fold</c>, <c>initial:n</c> and <c>final:n</c> from the same helper the project source uses, plus
    /// <c>parse.state</c> when the list carried one.
    /// </summary>
    public CorpusRow Row(CorpusEntry word) => new(word, Segments);
}

/// <summary>A corpus word read through the columns of the project <c>forms</c> source.</summary>
public sealed class CorpusRow
{
    private readonly TextColumns _text;

    internal CorpusRow(CorpusEntry word, CorpusSegments segments)
    {
        Word = word;
        _text = new TextColumns(word.Form, segments.Index, QueryFold.Compile(null));
    }

    /// <summary>The word.</summary>
    public CorpusEntry Word { get; }

    /// <summary>The form's segments: graphemes from the segment file, else code points.</summary>
    public IReadOnlyList<string> Segments => _text.Segments;

    /// <summary>The values of a column: <c>form</c>, <c>tokens</c>, <c>parse.state</c>, or a text column of the forms source. No value is an empty list.</summary>
    public IReadOnlyList<string> Get(string column) => column switch
    {
        "form" => [Word.Form],
        "tokens" => [Word.Tokens.ToString(CultureInfo.InvariantCulture)],
        "parse.state" => Word.ParseState is { } state ? [state] : [],
        _ => _text.Get(column),
    };
}
