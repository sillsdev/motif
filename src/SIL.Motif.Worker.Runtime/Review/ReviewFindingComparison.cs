using SIL.Motif.Contract.Review;

namespace SIL.Motif.Worker.Runtime.Review;

/// <summary>Compares frozen diagnostic and per-measure Parsimony identities without netting gains against losses.</summary>
public static class ReviewFindingComparison
{
    /// <summary>Returns independent added and removed warning and finding events.</summary>
    public static ReviewFindingDelta Compare(ReviewFindingInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var limitations = new List<string>();
        var added = new List<string>();
        var removed = new List<string>();
        if (inputs.BeforeWarnings is null || inputs.AfterWarnings is null) limitations.Add("warnings-unavailable");
        else
        {
            var before = Diagnostics(inputs.BeforeWarnings, limitations);
            var after = Diagnostics(inputs.AfterWarnings, limitations);
            added.AddRange(after.Known.Where(pair => !before.Known.ContainsKey(pair.Key) &&
                !before.Unresolved.Contains(pair.Value) && !before.Unresolved.Contains(("", ""))).Select(pair => pair.Key).Order(StringComparer.Ordinal));
            removed.AddRange(before.Known.Where(pair => !after.Known.ContainsKey(pair.Key) &&
                !after.Unresolved.Contains(pair.Value) && !after.Unresolved.Contains(("", ""))).Select(pair => pair.Key).Order(StringComparer.Ordinal));
        }

        var measures = new List<ReviewMeasureDelta>();
        if (inputs.BeforeParsimony is null || inputs.AfterParsimony is null) limitations.Add("parsimony-unavailable");
        else
        {
            var old = inputs.BeforeParsimony.GroupBy(item => (item.Group, item.MeasureId)).ToDictionary(group => group.Key, group => group.ToArray());
            var current = inputs.AfterParsimony.GroupBy(item => (item.Group, item.MeasureId)).ToDictionary(group => group.Key, group => group.ToArray());
            foreach (var key in old.Keys.Union(current.Keys).OrderBy(key => key.Group, StringComparer.Ordinal).ThenBy(key => key.MeasureId, StringComparer.Ordinal))
            {
                var before = old.GetValueOrDefault(key);
                var after = current.GetValueOrDefault(key);
                var left = before?.FirstOrDefault();
                var right = after?.FirstOrDefault();
                var exemplar = right ?? left!;
                string? reason = before is null || after is null ? "parsimony-pair-missing" :
                    before.Length != 1 || after.Length != 1 ? "parsimony-duplicate-measure" :
                    !left!.Completed || !right!.Completed ? "parsimony-detector-incomplete" :
                    left.Version != right.Version || left.ThresholdVersion != right.ThresholdVersion ||
                    left.UniverseDigest != right.UniverseDigest || left.Unit != right.Unit ? "parsimony-universe-changed" : null;
                if (reason is not null)
                {
                    measures.Add(new ReviewMeasureDelta(key.Group, key.MeasureId, exemplar.Unit, [], [], [], reason));
                    continue;
                }
                var oldFindings = left!.Findings.GroupBy(item => item.FindingId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
                var newFindings = right!.Findings.GroupBy(item => item.FindingId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
                if (oldFindings.Values.Any(g => g.Length != 1) || newFindings.Values.Any(g => g.Length != 1))
                {
                    measures.Add(new ReviewMeasureDelta(key.Group, key.MeasureId, exemplar.Unit, [], [], [], "duplicate-finding"));
                    continue;
                }
                var gained = newFindings.Keys.Except(oldFindings.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                var lost = oldFindings.Keys.Except(newFindings.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                var changed = oldFindings.Keys.Intersect(newFindings.Keys, StringComparer.Ordinal)
                    .Where(id => oldFindings[id][0].EvidenceDigest != newFindings[id][0].EvidenceDigest)
                    .Order(StringComparer.Ordinal).ToArray();
                measures.Add(new ReviewMeasureDelta(key.Group, key.MeasureId, exemplar.Unit, gained, lost, changed, null));
            }
        }
        return new ReviewFindingDelta(added, removed, measures, limitations.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static (Dictionary<string, (string Origin, string Code)> Known,
        HashSet<(string Origin, string Code)> Unresolved) Diagnostics(
        IReadOnlyList<ReviewDiagnostic> diagnostics, List<string> limitations)
    {
        var known = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        var unresolved = new HashSet<(string, string)>();
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity is not ("warning" or "error" or "info"))
            {
                limitations.Add("diagnostic-severity-unknown");
                unresolved.Add(Family(diagnostic));
                continue;
            }
            if (!ReviewDiagnosticIdentity.TryCreate(diagnostic, out var identity))
            {
                limitations.Add("diagnostic-identity-unavailable");
                if (diagnostic.Severity is "warning" or "error")
                    unresolved.Add(Family(diagnostic));
                continue;
            }
            if (diagnostic.Severity is "warning" or "error") known[identity] = (diagnostic.Origin, diagnostic.Code);
        }
        return (known, unresolved);
    }

    private static (string Origin, string Code) Family(ReviewDiagnostic diagnostic) =>
        string.IsNullOrWhiteSpace(diagnostic.Origin) || string.IsNullOrWhiteSpace(diagnostic.Code)
            ? ("", "") : (diagnostic.Origin, diagnostic.Code);
}
