using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Texts;

namespace SIL.Motif.Commands.Catalog;

/// <summary>
/// A Selection word with the Compare cell the Overview placed it in, built once so each measure reads the same
/// placement; <see cref="Measured"/> is null when the run did not measure the word.
/// </summary>
internal readonly record struct PlacedWord(string Form, AssessedWord? Measured, CompareColumnKind Column, string MeaningCode);

/// <summary>Builds the Overview's unparsed-words row and the counts Text Coverage leaves out, from stored evidence only.</summary>
internal static class OverviewUnparsedBuilder
{
    public static OverviewUnparsedWords Build(IReadOnlyList<PlacedWord> words,
        IReadOnlyDictionary<string, int>? occurrences)
    {
        var hasCounts = occurrences is { Count: > 0 } &&
            words.Any(placed => occurrences.GetValueOrDefault(placed.Form) > 0);
        var forms = new List<string>();
        var counts = new List<long>();
        var unknown = new List<bool>();
        var reasons = new List<(string Kind, string? Code)>();
        var incorrectSpelling = 0;
        foreach (var placed in words)
        {
            if (placed.Measured is not { } word ||
                placed.Column is not (CompareColumnKind.NoParse or CompareColumnKind.Timeout)) continue;
            if (word.ProjectStanding == ProjectStanding.IncorrectSpelling)
            {
                incorrectSpelling++;
                continue;
            }
            forms.Add(placed.Form);
            counts.Add(hasCounts ? occurrences!.GetValueOrDefault(placed.Form) : 0);
            unknown.Add(!hasCounts || !occurrences!.ContainsKey(placed.Form));
            reasons.Add(ReasonOf(placed, word));
        }

        var order = RankedWords.Order(forms, counts, hasCounts ? unknown : null);
        var top = order.Take(OverviewMeasureLimits.TopWords).Select(index => new OverviewUnparsedWord(
            forms[index], unknown[index] ? null : (int)counts[index], reasons[index].Kind, reasons[index].Code)).ToArray();
        var groups = Enumerable.Range(0, forms.Count)
            .GroupBy(index => reasons[index])
            .Select(group => new OverviewUnparsedGroup(group.Key.Kind, group.Key.Code, group.Count(),
                Saturate(group.Sum(index => counts[index]))))
            .OrderByDescending(group => group.Words)
            .ThenBy(group => group.Kind, StringComparer.Ordinal)
            .ThenBy(group => group.Code, StringComparer.Ordinal)
            .ToArray();
        return new OverviewUnparsedWords(forms.Count, Saturate(counts.Sum()), hasCounts, top, top.Sum(word => word.Occurrences ?? 0),
            groups.Take(OverviewMeasureLimits.TopWords).ToArray(), groups.Length, incorrectSpelling)
        { NoCountWords = hasCounts ? unknown.Count(isUnknown => isUnknown) : 0 };
    }

    private static int Saturate(long value) => (int)Math.Min(value, int.MaxValue);

    /// <summary>
    /// The digit-only and other-writing-system places in the chosen Texts, from the Text summaries captured with the
    /// Baseline; null when no Texts are chosen or the summary is not the one the evidence was measured against.
    /// </summary>
    public static (int DigitOnly, int OtherWritingSystem)? LeftOut(ProjectSummarySnapshot? summary,
        IReadOnlyList<Guid>? chosenTextIds)
    {
        if (summary is null || chosenTextIds is not { Count: > 0 }) return null;
        var chosen = chosenTextIds.ToHashSet();
        var texts = summary.Texts.Where(text => chosen.Contains(text.TextId)).ToArray();
        return (texts.Sum(text => text.DigitOccurrenceCount), texts.Sum(text => text.OtherWritingSystemOccurrenceCount));
    }

    private static (string Kind, string? Code) ReasonOf(PlacedWord placed, AssessedWord word)
    {
        if (placed.MeaningCode == "refused")
            return (UnparsedKind.Refused, ParserRefusals.Of(word.Morphology, word.Outcome)?.Code);
        if (placed.Column == CompareColumnKind.Timeout)
            return (StopLimit.From(word.Outcome, word.Morphology) ?? UnparsedKind.Stopped, null);
        return (UnparsedKind.NoAnalysis, null);
    }
}

/// <summary>Orders words by a count, largest first, and by spelling within a count, without comparing strings per pair.</summary>
internal static class RankedWords
{
    /// <summary>
    /// The indices of <paramref name="forms"/> ordered by count descending, then by ordinal spelling; words flagged in
    /// <paramref name="unknown"/> come after every counted word, a known zero included. Spellings are ranked once,
    /// so the sort itself compares only integers and a tie at any rank is settled the same way every run.
    /// </summary>
    public static int[] Order(IReadOnlyList<string> forms, IReadOnlyList<long> counts, IReadOnlyList<bool>? unknown = null)
    {
        var bySpelling = Enumerable.Range(0, forms.Count).ToArray();
        if (!IsOrdinalSorted(forms)) Array.Sort(bySpelling, (a, b) => string.CompareOrdinal(forms[a], forms[b]));
        var spellingRank = new int[forms.Count];
        for (var place = 0; place < bySpelling.Length; place++) spellingRank[bySpelling[place]] = place;
        var keys = new ulong[forms.Count];
        var order = new int[forms.Count];
        for (var index = 0; index < keys.Length; index++)
        {
            var rank = unknown is not null && unknown[index] ? 0u : (uint)Math.Clamp(counts[index], 0, uint.MaxValue - 1) + 1;
            keys[index] = ((ulong)(uint.MaxValue - rank) << 32) | (uint)spellingRank[index];
            order[index] = index;
        }
        Array.Sort(keys, order);
        return order;
    }

    private static bool IsOrdinalSorted(IReadOnlyList<string> forms)
    {
        for (var index = 1; index < forms.Count; index++)
            if (string.CompareOrdinal(forms[index - 1], forms[index]) > 0) return false;
        return true;
    }
}
