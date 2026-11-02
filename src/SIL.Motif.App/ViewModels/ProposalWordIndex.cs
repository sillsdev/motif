using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>What the Proposal's side keeps of one Trial word: its matrix cell, its work and whether it reads steps.</summary>
/// <param name="Cell">The packed matrix cell, see <see cref="ProposalWordIndex.Pack"/>.</param>
/// <param name="WorkSteps">The parser work steps the Trial recorded, or null when it recorded none.</param>
/// <param name="ElapsedMs">The time the Trial recorded, or null; only a tie-break for the slowest sort.</param>
/// <param name="HidesSteps">Whether the word was refused or not parsed, so there are no steps to read.</param>
internal readonly record struct ProposalWordFacts(byte Cell, long? WorkSteps, int? ElapsedMs, bool HidesSteps);

/// <summary>
/// The Trial's words in spelling order with, for each, the values the word list reads from the Proposal's side; no
/// <see cref="AssessmentWordResult"/> is kept, so the Trial's rows can be released once the index is made.
/// </summary>
internal sealed class ProposalWordIndex
{
    internal const byte NoCell = byte.MaxValue;
    private const byte HidesStepsFlag = 1;
    private const int Stride = 8;

    // A cell is packed as row * Stride + column in one byte, so a new column or row must still fit.
    static ProposalWordIndex()
    {
        if (Enum.GetValues<CompareColumnKind>().Length > Stride || Enum.GetValues<WordProjectStatus>().Length * Stride > NoCell)
            throw new InvalidOperationException("A matrix cell no longer packs into one byte; widen ProposalWordIndex.Pack.");
    }

    // The spellings run together in one buffer, word n at [_starts[n], _starts[n + 1]).
    private readonly char[] _chars;
    private readonly int[] _starts;
    private readonly byte[] _cells;
    private readonly long[] _steps;
    private readonly int[] _elapsed;
    private readonly byte[] _flags;

    private ProposalWordIndex(char[] chars, int[] starts, byte[] cells, long[] steps, int[] elapsed, byte[] flags)
    {
        _chars = chars;
        _starts = starts;
        _cells = cells;
        _steps = steps;
        _elapsed = elapsed;
        _flags = flags;
    }

    /// <summary>An index over no words, for a Proposal side that was not read.</summary>
    public static ProposalWordIndex Empty { get; } = new([], [0], [], [], [], []);

    /// <summary>How many words it holds.</summary>
    public int Count => _cells.Length;

    /// <summary>Packs a cell into the byte the index keeps.</summary>
    public static byte Pack(WordProjectStatus row, CompareColumnKind column) => (byte)((int)row * Stride + (int)column);

    /// <summary>Unpacks a byte made by <see cref="Pack"/>.</summary>
    public static (WordProjectStatus Row, CompareColumnKind Column) Unpack(byte packed) =>
        ((WordProjectStatus)(packed / Stride), (CompareColumnKind)(packed % Stride));

    /// <summary>
    /// Indexes <paramref name="words"/>, whose cells (and, when given, whether each hides its steps) are in the same order.
    /// </summary>
    public static ProposalWordIndex Of(IReadOnlyList<AssessmentWordResult> words, byte[] cells, bool[]? hidesSteps = null)
    {
        var order = new int[words.Count];
        for (var index = 0; index < order.Length; index++) order[index] = index;
        Array.Sort(order, (left, right) => string.CompareOrdinal(words[left].Word, words[right].Word));
        var length = 0;
        foreach (var word in words) length += word.Word.Length;
        var chars = new char[length];
        var starts = new int[order.Length + 1];
        var sortedCells = new byte[order.Length];
        var steps = new long[order.Length];
        var elapsed = new int[order.Length];
        var flags = new byte[order.Length];
        var at = 0;
        for (var index = 0; index < order.Length; index++)
        {
            var word = words[order[index]];
            starts[index] = at;
            word.Word.CopyTo(0, chars, at, word.Word.Length);
            at += word.Word.Length;
            sortedCells[index] = cells[order[index]];
            steps[index] = word.WorkSteps ?? -1;
            elapsed[index] = word.ElapsedMs ?? -1;
            flags[index] = hidesSteps is not null && hidesSteps[order[index]] ? HidesStepsFlag : (byte)0;
        }
        starts[order.Length] = at;
        return new ProposalWordIndex(chars, starts, sortedCells, steps, elapsed, flags);
    }

    /// <summary>Finds what the Trial recorded for a spelling, or false when it did not measure the word.</summary>
    public bool TryFind(string word, out ProposalWordFacts facts)
    {
        var low = 0;
        var high = _cells.Length - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var order = new ReadOnlySpan<char>(_chars, _starts[middle], _starts[middle + 1] - _starts[middle])
                .CompareTo(word, StringComparison.Ordinal);
            if (order == 0)
            {
                facts = new ProposalWordFacts(_cells[middle], _steps[middle] < 0 ? null : _steps[middle],
                    _elapsed[middle] < 0 ? null : _elapsed[middle], (_flags[middle] & HidesStepsFlag) != 0);
                return true;
            }
            if (order < 0) low = middle + 1;
            else high = middle - 1;
        }
        facts = default;
        return false;
    }
}
