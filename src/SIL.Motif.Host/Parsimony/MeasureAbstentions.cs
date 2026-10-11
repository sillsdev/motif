namespace SIL.Motif.Host.Parsimony;

/// <summary>
/// Collects why a measure declined to judge some of its inputs: a count and one plain sentence per reason. An
/// Inconclusive run shows the counts first, in the same words the alternation measure uses, then the sentences.
/// </summary>
internal sealed class MeasureAbstentions
{
    private readonly SortedDictionary<string, long> _counts = new(StringComparer.Ordinal);
    private readonly List<string> _sentences = [];

    /// <summary>Whether any reason has a positive count.</summary>
    public bool Any => _counts.Count > 0;

    /// <summary>Records <paramref name="count"/> inputs set aside for <paramref name="code"/>; a zero adds nothing.</summary>
    /// <param name="code">A stable kebab-case name for the reason.</param>
    /// <param name="count">How many inputs the reason set aside.</param>
    /// <param name="sentence">One plain sentence that says what was set aside and why.</param>
    public void Add(string code, long count, string sentence)
    {
        if (count <= 0) return;
        _counts[code] = _counts.GetValueOrDefault(code) + count;
        _sentences.Add(sentence);
    }

    /// <summary>The count of one reason, or zero when it never arose.</summary>
    public long Count(string code) => _counts.GetValueOrDefault(code);

    /// <summary>The counts line followed by one sentence per reason, in the order the reasons were added.</summary>
    public string Detail() => "Abstention counts by reason: " +
        string.Join(", ", _counts.Select(item => $"{item.Key}={item.Value}")) + ". " + string.Join(" ", _sentences);

    /// <summary>Capitalises a lower-case note clause so it reads as a sentence.</summary>
    public static string Sentence(string? clause) => string.IsNullOrEmpty(clause) ? string.Empty
        : char.ToUpperInvariant(clause[0]) + clause[1..];

    /// <summary>Joins clauses into one phrase: "a", "a and b", or "a, b, and c".</summary>
    public static string JoinClauses(IReadOnlyList<string> clauses) => clauses.Count switch
    {
        0 => string.Empty,
        1 => clauses[0],
        2 => clauses[0] + " and " + clauses[1],
        _ => string.Join(", ", clauses.Take(clauses.Count - 1)) + ", and " + clauses[^1],
    };
}
