using System.Text;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli.Rendering;

/// <summary>Renders paired work changes for Timing and Overview.</summary>
internal static class TimingTextRenderer
{
    public static string RenderTiming(TimingResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"Parser work comparison for {response.WordCount:N0} word(s) from {response.WordSet} ({response.AssessmentId})");
        text.AppendLine("  " + (response.EvidenceRelation switch
        {
            TimingEvidenceRelation.Current => "Current Baseline",
            TimingEvidenceRelation.Historical => "Historical Baseline",
            TimingEvidenceRelation.SavedSince => "FieldWorks saved since the measured Baseline",
            _ => "Baseline relationship unknown",
        }));
        if (response.SourceLastWriteUtc is { } saved) text.AppendLine($"  Measured FieldWorks save: {saved:O}");
        if (response.CurrentProjectIsStale) text.AppendLine("  Warning: FieldWorks has changed since the current Baseline.");
        text.AppendLine("  " + Describe(response.CostComparison.Total));
        foreach (var word in response.CostComparison.Words)
            text.AppendLine($"  {word.Word}: {Describe(word.Change)}");
        foreach (var row in response.Aggregates)
        {
            var item = response.CostComparison.Objects.FirstOrDefault(value => value.Kind == row.Kind &&
                value.Key == ObjectIdentity.CanonicalKey(row.Key, row.IdentityQuality));
            text.AppendLine($"  {row.Name} [{row.Kind}:{row.Key}]: " +
                Describe(item?.Change ?? TimingCostChange.Unavailable("No paired object comparison is available.")));
        }
        if (response.CostComparison.ObjectsUnavailableReason is { } reason)
            text.AppendLine("  Object comparison unavailable: " + reason);
        if (response.ObjectTimingDetail is { IsComplete: false, Description: { } detail }) text.AppendLine("  " + detail);
        return text.ToString();
    }

    public static void AppendOverviewSpeed(StringBuilder text, OverviewTiming timing) =>
        text.AppendLine("Work       " + Describe(timing.CostComparison.Total));

    private static string Describe(TimingCostChange change) => change.PercentChange is { } percent
        ? $"{percent:+0.##;-0.##;0}% change from the paired Baseline"
        : "comparison unavailable: " + change.UnavailableReason;
}
