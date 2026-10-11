using System.Text.Json;

namespace SIL.Motif.Tests.Cli;

/// <summary>What one measure reported for one sample grammar in a survey run.</summary>
internal sealed record ObservedMeasure(string MeasureId, string Status, long? EligibleItems, long? FindingItems);

/// <summary>
/// The expected status and finding range of every measure on every sample grammar, read from the checked-in
/// <c>parsimony-survey-bands.json</c>. The ranges are loose enough for parser-speed noise and tight enough that a
/// measure which stops finding, or turns Inconclusive, fails the survey instead of passing as a smaller table.
/// </summary>
internal sealed class ParsimonySurveyBands
{
    internal const string FileName = "parsimony-survey-bands.json";

    private readonly Dictionary<string, Dictionary<string, Band>> _samples;

    private ParsimonySurveyBands(Dictionary<string, Dictionary<string, Band>> samples) => _samples = samples;

    internal IReadOnlyCollection<string> SampleNames => _samples.Keys;

    internal IReadOnlyCollection<string> MeasureIds(string sample) => _samples[sample].Keys;

    internal static ParsimonySurveyBands Load() => Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, FileName)));

    internal static ParsimonySurveyBands Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var samples = new Dictionary<string, Dictionary<string, Band>>(StringComparer.Ordinal);
        foreach (var sample in document.RootElement.GetProperty("samples").EnumerateObject())
        {
            var measures = new Dictionary<string, Band>(StringComparer.Ordinal);
            foreach (var measure in sample.Value.EnumerateObject())
                measures.Add(measure.Name, new Band(
                    measure.Value.GetProperty("status").GetString()!,
                    Optional(measure.Value, "eligibleMin"),
                    Optional(measure.Value, "findingsMin"),
                    Optional(measure.Value, "findingsMax")));
            samples.Add(sample.Name, measures);
        }

        return new ParsimonySurveyBands(samples);
    }

    /// <summary>Returns one sentence per departure from the bands; empty means the survey is within them.</summary>
    internal IReadOnlyList<string> Violations(string sample, IReadOnlyCollection<ObservedMeasure> observed)
    {
        if (!_samples.TryGetValue(sample, out var bands))
            return [$"{sample}: the bands file has no entry for this sample grammar."];
        var violations = new List<string>();
        foreach (var measure in observed.OrderBy(item => item.MeasureId, StringComparer.Ordinal))
        {
            if (!bands.TryGetValue(measure.MeasureId, out var band))
            {
                violations.Add($"{sample} {measure.MeasureId}: the bands file has no entry for this measure.");
                continue;
            }

            if (!string.Equals(measure.Status, band.Status, StringComparison.Ordinal))
                violations.Add($"{sample} {measure.MeasureId}: status {measure.Status}, expected {band.Status}.");
            if (band.EligibleMin is { } eligibleMin && (measure.EligibleItems ?? -1) < eligibleMin)
                violations.Add($"{sample} {measure.MeasureId}: {Show(measure.EligibleItems)} eligible items, " +
                    $"expected at least {eligibleMin}.");
            if (band.FindingsMin is { } min && (measure.FindingItems ?? -1) < min)
                violations.Add($"{sample} {measure.MeasureId}: {Show(measure.FindingItems)} findings, expected at least {min}.");
            if (band.FindingsMax is { } max && (measure.FindingItems ?? long.MaxValue) > max)
                violations.Add($"{sample} {measure.MeasureId}: {Show(measure.FindingItems)} findings, expected at most {max}.");
        }

        foreach (var missing in bands.Keys.Except(observed.Select(item => item.MeasureId), StringComparer.Ordinal))
            violations.Add($"{sample} {missing}: the survey did not run this measure.");
        return violations;
    }

    private static long? Optional(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetInt64() : null;

    private static string Show(long? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "no";

    private sealed record Band(string Status, long? EligibleMin, long? FindingsMin, long? FindingsMax);
}
