using System.Globalization;
using SIL.Motif.Contract.Query.Readings;

namespace SIL.Motif.Contract.Query.Corpus;

/// <summary>What changed for one word between the baseline grammar's parse and the target grammar's.</summary>
/// <param name="ParseState">The word's parse state under the baseline grammar.</param>
/// <param name="Readings">How many readings the baseline grammar gave it.</param>
/// <param name="Change">One of <see cref="BaselineSet"/>'s kinds.</param>
public sealed record BaselineCell(string ParseState, int Readings, string Change);

/// <summary>
/// The comparison of <c>--baseline-grammar</c> with <c>--grammar</c>, word by word. The columns <c>base.parse.state</c> and <c>base.readings</c> are the
/// baseline's values (the plain <c>parse.state</c> and <c>readings</c> are the target's), and <c>change</c> is one of <see cref="Kinds"/>.
/// A word either parse could not finish (capped, timed out, no answer) is <c>unknown</c>, never gained or lost.
/// </summary>
public static class BaselineSet
{
    /// <summary>The word has no reading in the baseline and at least one now.</summary>
    public const string Gained = "gained";
    /// <summary>The word had a reading in the baseline and has none now.</summary>
    public const string Lost = "lost";
    /// <summary>Both parse, and the target gives more readings.</summary>
    public const string MoreReadings = "more-readings";
    /// <summary>Both parse, and the target gives fewer readings.</summary>
    public const string FewerReadings = "fewer-readings";
    /// <summary>Both parse with the same number of readings, but the analyses differ.</summary>
    public const string ReadingsChanged = "readings-changed";
    /// <summary>Neither the readings nor their analyses differ.</summary>
    public const string Same = "same";
    /// <summary>A parse stopped at the work limit or the timeout, or got no answer, so the change cannot be known.</summary>
    public const string Unknown = "unknown";

    /// <summary>Every kind of change.</summary>
    public static IReadOnlyList<string> Kinds { get; } = [Gained, Lost, MoreReadings, FewerReadings, ReadingsChanged, Same, Unknown];

    /// <summary>The columns: <c>base.parse.state</c>, <c>base.readings</c> and <c>change</c>.</summary>
    public static IReadOnlyList<string> Columns { get; } = ["base.parse.state", "base.readings", "change"];

    /// <summary>Whether a column name is a comparison column.</summary>
    public static bool IsColumn(string column) => Columns.Contains(column);

    /// <summary>Whether a comparison column holds a number.</summary>
    public static bool IsNumeric(string column) => column == "base.readings";

    /// <summary>Compares one word's parse under the baseline with its parse under the target grammar; a missing parse counts as failed.</summary>
    public static BaselineCell Compare(WordReadings? baseline, WordReadings? target)
    {
        var before = baseline ?? new WordReadings(ReadingStates.Failed, []);
        var now = target ?? new WordReadings(ReadingStates.Failed, []);
        return new BaselineCell(before.State, before.Readings.Count, Classify(before, now));
    }

    private static string Classify(WordReadings before, WordReadings now)
    {
        if (before.MayBeIncomplete || now.MayBeIncomplete) return Unknown;
        var had = before.Readings.Count;
        var has = now.Readings.Count;
        if (had == 0 && has > 0) return Gained;
        if (had > 0 && has == 0) return Lost;
        if (has > had) return MoreReadings;
        if (has < had) return FewerReadings;
        return Signatures(before).SequenceEqual(Signatures(now), StringComparer.Ordinal) ? Same : ReadingsChanged;
    }

    // A reading is its PanGloss signature, else its morphs form and analysis ids; the set is order-free.
    private static IEnumerable<string> Signatures(WordReadings word) => word.Readings
        .Select(reading => reading.Signature ?? string.Join("+", reading.Morphs.Select(morph => morph.Form + "/" + morph.Msa)))
        .Order(StringComparer.Ordinal);

    /// <summary>The value of a comparison column for a word's cell; a word with no cell has no value.</summary>
    public static IReadOnlyList<string> Value(BaselineCell? cell, string column) => cell is null ? [] : column switch
    {
        "base.parse.state" => [cell.ParseState],
        "base.readings" => [cell.Readings.ToString(CultureInfo.InvariantCulture)],
        _ => [cell.Change],
    };
}

/// <summary>The comparison of every corpus word, built once per query.</summary>
public sealed class BaselineCells
{
    private readonly Dictionary<string, BaselineCell> _cells = new(StringComparer.Ordinal);

    /// <summary>Compares each word of the corpus under the two parses.</summary>
    public static BaselineCells Bind(IEnumerable<string> words, IReadingSource baseline, IReadingSource target)
    {
        var cells = new BaselineCells();
        foreach (var word in words) cells._cells[word] = BaselineSet.Compare(baseline.Get(word), target.Get(word));
        return cells;
    }

    /// <summary>The comparison of a word; null for a word the query did not compare.</summary>
    public BaselineCell? Cell(string word) => _cells.GetValueOrDefault(word);

    /// <summary>The value of a comparison column for a word.</summary>
    public IReadOnlyList<string> Value(string word, string column) => BaselineSet.Value(Cell(word), column);
}
