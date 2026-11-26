using System.Globalization;
using System.Text.Json;
using SIL.Motif.Contract.Review;

namespace SIL.Motif.Commands.Review;

/// <summary>What a Trial's Reading delta is held to: expected-change manifests and caps on doubled words.</summary>
/// <param name="RetiredRows">The lines of an expect-retired manifest, or null to hold nothing against the retired side.</param>
/// <param name="AddedRows">The lines of an expect-added manifest, or null to hold nothing against the added side.</param>
/// <param name="MaxDoubled">The most doubled words allowed, or null for no cap.</param>
/// <param name="MaxDoubledFrac">The most doubled words as a fraction of the measured words that parsed before, 0 to 1, or null for no cap.</param>
public sealed record TrialExpectations(IReadOnlyList<string>? RetiredRows = null, IReadOnlyList<string>? AddedRows = null,
    long? MaxDoubled = null, double? MaxDoubledFrac = null)
{
    /// <summary>Whether any manifest or cap was given.</summary>
    public bool Any => RetiredRows is not null || AddedRows is not null || MaxDoubled is not null || MaxDoubledFrac is not null;
}

/// <summary>
/// Holds a Reading delta to a <see cref="TrialExpectations"/> with the lane gate's rules, so a Trial that already ran
/// answers the gate without a second measurement.
/// </summary>
internal static class TrialExpectationCheck
{
    private const int MaxMismatchRows = 50;
    private const int DoubledSampleSize = 20;

    /// <summary>The message of a request that cannot be evaluated, or null when it can.</summary>
    internal static string? Invalid(TrialExpectations expectations)
    {
        if (expectations.MaxDoubled is < 0) return "--max-doubled must be zero or more.";
        if (expectations.MaxDoubledFrac is { } fraction && !(fraction is >= 0 and <= 1))
            return "--max-doubled-frac must be between 0 and 1.";
        return null;
    }

    /// <summary>Reads the manifest lines into the Readings each word expects, refusing a line that is not a row.</summary>
    internal static (Dictionary<string, Dictionary<string, long>>? Rows, string? Error) Parse(string kind, IReadOnlyList<string> lines,
        bool keepCase)
    {
        var rows = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        for (var index = 0; index < lines.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index])) continue;
            if (!ReadingManifestFormat.TryParseRow(lines[index], out var word, out var key, out var count))
                return (null, $"The {kind} manifest line {index + 1} is not a row of word, signature and count.");
            if (!keepCase) word = ManifestWriter.ManifestWord(word);
            if (!rows.TryGetValue(word, out var readings)) rows[word] = readings = new Dictionary<string, long>(StringComparer.Ordinal);
            if (!readings.TryAdd(key, count))
                return (null, $"The {kind} manifest repeats a Reading of '{word}' on line {index + 1}.");
        }
        return (rows, null);
    }

    /// <summary>Evaluates the delta; the manifests are already parsed with <see cref="Parse"/>.</summary>
    internal static ReadingExpectationResult Evaluate(IReadOnlyList<ReadingDeltaForm> forms, ReadingDeltaCounts counts,
        TrialExpectations expectations, Dictionary<string, Dictionary<string, long>>? retiredRows,
        Dictionary<string, Dictionary<string, long>>? addedRows, IReadOnlyDictionary<string, string>? aliases, bool keepCase,
        string? notCurrent = null)
    {
        // A known non-word that stopped parsing needs no manifest row.
        var judged = forms.Where(form => !form.Classes.Contains(ReadingDeltaClasses.Rejected))
            .Select(form => (Word: keepCase ? form.Word : ManifestWriter.ManifestWord(form.Word), Form: form))
            .GroupBy(item => item.Word, StringComparer.Ordinal).Select(group => group.First()).ToArray();
        var measured = forms.Select(form => keepCase ? form.Word : ManifestWriter.ManifestWord(form.Word))
            .ToHashSet(StringComparer.Ordinal);
        var mismatches = new List<ReadingExpectationMismatch>();
        long mismatched = 0;
        long notMeasured = 0;
        foreach (var (kind, rows, pick) in new (string, Dictionary<string, Dictionary<string, long>>?,
                     Func<ReadingDeltaForm, IReadOnlyList<(SIL.Motif.Contract.HandAnalysis.ReadingSignature Signature, long Count)>>)[]
                 {
                     (ReadingExpectationKinds.Retired, retiredRows, form => form.Retired),
                     (ReadingExpectationKinds.Added, addedRows, form => form.Added),
                 })
        {
            if (rows is null) continue;
            notMeasured += rows.Keys.Count(word => !measured.Contains(word));
            foreach (var (word, form) in judged)
            {
                var actual = new Dictionary<string, (long Count, SIL.Motif.Contract.HandAnalysis.ReadingSignature Signature)>(StringComparer.Ordinal);
                foreach (var (signature, count) in pick(form))
                    actual[ReadingManifestFormat.Key(signature, aliases)] = (count, signature);
                rows.TryGetValue(word, out var expected);
                expected ??= [];
                var unexpected = actual.Where(pair => !expected.TryGetValue(pair.Key, out var count) || count != pair.Value.Count)
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
                var missing = expected.Where(pair => !actual.TryGetValue(pair.Key, out var found) || found.Count != pair.Value)
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
                if (unexpected.Length == 0 && missing.Length == 0) continue;
                mismatched++;
                mismatches.Add(new ReadingExpectationMismatch(kind, word,
                    [.. unexpected.Select(pair => new ReadingDeltaReading(pair.Value.Signature.Id, pair.Value.Count,
                        ReadingManifestFormat.ToElement(pair.Value.Signature)))],
                    [.. missing.Select(pair => new ReadingDeltaReading(pair.Key, pair.Value, KeyElement(pair.Key)))]));
            }
        }

        var doubled = forms.Where(form => form.Classes.Contains(ReadingDeltaClasses.Doubled)).Select(form => form.Word)
            .Order(StringComparer.Ordinal).ToArray();
        var parsedBefore = counts.ParsedBefore;
        var parts = new List<string>();
        if (expectations.MaxDoubled is { } cap && doubled.Length > cap)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"--max-doubled {cap}"));
        if (expectations.MaxDoubledFrac is { } fraction && doubled.Length > fraction * parsedBefore)
            parts.Add(string.Create(CultureInfo.InvariantCulture,
                $"--max-doubled-frac {fraction} x {parsedBefore} = {fraction * parsedBefore:g}"));
        var overCap = parts.Count > 0;
        var capReason = overCap ? string.Create(CultureInfo.InvariantCulture, $"doubled {doubled.Length} > cap ") + string.Join(" and ", parts) : null;

        string state;
        string? reason;
        if (notCurrent is not null)
            (state, reason) = (ReadingExpectationStates.Unmeasured, notCurrent);
        else if (counts.Unmeasured > 0)
        {
            state = ReadingExpectationStates.Unmeasured;
            reason = string.Create(CultureInfo.InvariantCulture,
                $"{counts.Unmeasured} forms were not measured on both sides or have no complete Reading identity, so the delta cannot be held to the expectations.");
        }
        else if (mismatched > 0)
        {
            state = ReadingExpectationStates.Mismatch;
            reason = string.Create(CultureInfo.InvariantCulture,
                $"{mismatched} words differ from the manifests") + (capReason is null ? "." : "; " + capReason + ".");
        }
        else if (overCap)
        {
            state = ReadingExpectationStates.DoubledOverCap;
            reason = capReason;
        }
        else (state, reason) = (ReadingExpectationStates.Green, null);

        return new ReadingExpectationResult(state, reason, mismatched,
            [.. mismatches.OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Word, StringComparer.Ordinal).Take(MaxMismatchRows)],
            notMeasured, doubled.Length, [.. doubled.Take(DoubledSampleSize)], parsedBefore, expectations.MaxDoubled,
            expectations.MaxDoubledFrac, overCap);
    }

    private static JsonElement KeyElement(string key)
    {
        using var document = JsonDocument.Parse(key);
        return document.RootElement.Clone();
    }
}
