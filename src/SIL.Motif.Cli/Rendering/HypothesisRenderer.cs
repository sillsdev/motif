using System.Text;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli.Rendering;

internal static class HypothesisRenderer
{
    public static string RenderChanged(HypothesisResponse response)
    {
        var h = response.Hypothesis;
        var text = new StringBuilder();
        text.AppendLine($"{h.HypothesisId} {h.Status}: {h.Change}{Condition(h.Environment, h.HostClass, h.Slot)}");
        text.AppendLine($"  {h.Statement}");
        if (h.Tests.Count > 0)
            text.AppendLine($"  Latest test {h.Tests[^1].TestNumber}: {h.Tests[^1].Verdict}, margin {h.Tests[^1].Margin}");
        if (response.Neighbours.Count > 0)
        {
            text.AppendLine("  Neighbours on this shape:");
            foreach (var n in response.Neighbours)
                text.AppendLine($"    {n.HypothesisId} {n.Status} ({n.Mechanism}){(n.LatestVerdict is null ? "" : ", " + n.LatestVerdict)}");
        }
        return text.ToString();
    }

    public static string RenderNeighbours(HypothesisNeighboursResponse response)
    {
        var text = new StringBuilder();
        if (response.Neighbours.Count == 0)
            return $"No hypothesis on record for {response.Query}.{Environment.NewLine}";
        text.AppendLine($"{response.Neighbours.Count} on record for {response.Query}" +
            $"{(response.HasPassed ? "; a passed rule stands: widen or merge it" : "")}" +
            $"{(response.HasFailedAttempt ? "; a rule attempt failed, so a list is allowed" : "")}:");
        foreach (var n in response.Neighbours)
            text.AppendLine($"  {n.HypothesisId} {n.Status} ({n.Mechanism}){(n.LatestVerdict is null ? "" : ", " + n.LatestVerdict)}: {n.Statement}");
        return text.ToString();
    }

    public static string RenderList(HypothesisListResponse response)
    {
        if (response.Hypotheses.Count == 0) return $"No hypotheses.{Environment.NewLine}";
        var text = new StringBuilder();
        text.AppendLine($"Hypotheses {response.Offset + 1}-{response.Offset + response.Hypotheses.Count} of {response.Total}:");
        foreach (var h in response.Hypotheses)
            text.AppendLine($"  {h.HypothesisId}  {h.Status}  {h.Change}{Condition(h.Environment, h.HostClass, h.Slot)}  {h.Statement}");
        return text.ToString();
    }

    private static string Condition(string environment, string hostClass, string slot)
    {
        var parts = new[] { environment, hostClass, slot }.Where(p => p.Length > 0).ToList();
        return parts.Count == 0 ? "" : " (" + string.Join("; ", parts) + ")";
    }
}
