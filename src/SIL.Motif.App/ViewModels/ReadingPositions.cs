using System.Collections.Generic;
using System.Linq;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Pairs a word's display readings, which come in the project's reading order, with the parser's emitted
/// analyses that grades, markings and changes are indexed by. A reading with no recorded source index sits
/// at its own position, as every reading stored before the reading order existed does.
/// </summary>
internal static class ReadingPositions
{
    /// <summary>The display reading of the analysis emitted at <paramref name="index"/>, or <see langword="null"/>.</summary>
    public static ParserReading? For(IReadOnlyList<ParserReading>? readings, int index)
    {
        if (readings is null) return null;
        for (var position = 0; position < readings.Count; position++)
            if ((readings[position].SourceIndex ?? position) == index) return readings[position];
        return null;
    }

    /// <summary>The emitted-analysis indexes in the order the readings are shown.</summary>
    public static IEnumerable<int> ShownOrder(IReadOnlyList<ParserReading>? readings, int count) =>
        Enumerable.Range(0, count).OrderBy(index => ShownPosition(readings, index, count));

    /// <summary>The number a person sees for the analysis emitted at <paramref name="index"/>, starting at 1.</summary>
    public static int NumberOf(IReadOnlyList<ParserReading>? readings, int index, int count) =>
        ShownPosition(readings, index, count) + 1;

    private static int ShownPosition(IReadOnlyList<ParserReading>? readings, int index, int count)
    {
        if (readings is null) return index;
        for (var position = 0; position < readings.Count; position++)
            if ((readings[position].SourceIndex ?? position) == index) return position;
        return count + index;
    }
}
