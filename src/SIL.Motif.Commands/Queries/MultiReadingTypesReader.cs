using System;
using System.Collections.Generic;
using System.Linq;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Counts the word types of an Assessment that have more than one reading and groups them by morpheme pattern.
/// It reports only; it never changes a finding, an exit code or the Baseline's readiness.
/// </summary>
internal static class MultiReadingTypesReader
{
    private const int ExamplesPerGroup = 5;

    /// <summary>The report over <paramref name="words"/>' resolved readings.</summary>
    internal static MultiReadingTypes Read(IReadOnlyList<AssessmentWordResult> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var measured = words.Where(word => !word.IsIncomplete).ToArray();
        var groups = measured
            .Where(word => word.Readings is { Count: > 1 })
            .GroupBy(word => PatternOf(word.Readings!), StringComparer.Ordinal)
            .Select(group => new MultiReadingGroup(group.Key, group.Count(),
                group.Select(word => word.Word).Order(StringComparer.Ordinal).Take(ExamplesPerGroup).ToArray()))
            .OrderByDescending(group => group.Types).ThenBy(group => group.Pattern, StringComparer.Ordinal)
            .ToArray();
        return new MultiReadingTypes(measured.Length, groups.Sum(group => group.Types), groups);
    }

    // Every stem reads "stem", so readings that differ only in their stem share a pattern.
    private static string PatternOf(IReadOnlyList<ParserReading> readings) =>
        string.Join(" | ", readings.Select(ReadingPattern).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    private static string ReadingPattern(ParserReading reading) =>
        string.Join("+", reading.Morphs.Select(MorphName));

    private static string MorphName(ParserReadingMorph morph)
    {
        var form = morph.Form.Trim();
        var affix = form.StartsWith('-') || form.EndsWith('-') || form.StartsWith('=') || form.EndsWith('=');
        if (!affix) return "stem";
        if (!string.IsNullOrWhiteSpace(morph.Gloss)) return morph.Gloss.Trim();
        return !string.IsNullOrWhiteSpace(morph.Category) ? morph.Category.Trim() : form;
    }
}
