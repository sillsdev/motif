using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Runtime.Review;

internal static class TrialReviewRuleWork
{
    internal static IReadOnlyList<ReviewSectionItem>? Read(MotifDatabase database, TrialJobInput input,
        string? resultJson, FrozenReviewInputs inputs, ReviewWorkInputs? work)
    {
        if (work is null || ReviewWorkComparison.Compare(work).Comparison != WorkComparison.Comparable ||
            input.ReviewBaselines is null || resultJson is null) return null;
        try
        {
            using var document = JsonDocument.Parse(resultJson);
            if (!document.RootElement.TryGetProperty("reviewAfter", out var afterJson)) return null;
            var partitions = afterJson.Deserialize<ReviewAfterPartition[]>(MotifJson.CreateOptions());
            if (partitions is null || partitions.Length != input.ReviewBaselines.Count) return null;
            var repository = new AssessmentRepository(database);
            var cases = inputs.Cases.ToDictionary(item => item.CaseKey, StringComparer.Ordinal);
            var beforeRows = new List<AssessmentObjectTiming>();
            var afterRows = new List<AssessmentObjectTiming>();
            foreach (var baseline in input.ReviewBaselines)
            {
                var matches = partitions.Where(part => part.WritingSystemId == baseline.WritingSystemId &&
                    part.CaseKeys.ToHashSet(StringComparer.Ordinal).SetEquals(baseline.CaseKeys)).ToArray();
                if (matches.Length != 1 || baseline.AssessmentIds.Count != 1 || matches[0].AssessmentIds.Count != 1)
                    return null;
                var before = repository.Get(baseline.AssessmentIds[0]);
                var after = repository.Get(matches[0].AssessmentIds[0]);
                if (before.TimingEvidence is not { DetailComplete: true, StatisticsUnavailable: false, HasRetriedWords: false } ||
                    after.TimingEvidence is not { DetailComplete: true, StatisticsUnavailable: false, HasRetriedWords: false })
                    return null;
                foreach (var key in baseline.CaseKeys)
                {
                    if (!cases.TryGetValue(key, out var item)) return null;
                    beforeRows.AddRange(before.ObjectTimings.Where(row => row.Word == item.Word).Select(row => row with { Word = key }));
                    afterRows.AddRange(after.ObjectTimings.Where(row => row.Word == item.Word).Select(row => row with { Word = key }));
                }
            }
            var keys = inputs.Cases.Select(item => item.CaseKey).ToHashSet(StringComparer.Ordinal);
            var unattributed = TimingObjectComparison.Unattributed(beforeRows, afterRows, keys,
                work.Before.Sum(item => (decimal)item.Work!.Value), work.After.Sum(item => (decimal)item.Work!.Value), true);
            return TimingObjectComparison.Compare(beforeRows, afterRows, keys, true).Append(unattributed)
                .OrderBy(row => row.Kind, StringComparer.Ordinal)
                .ThenBy(row => row.Key, StringComparer.Ordinal).ThenBy(row => row.IdentityQuality, StringComparer.Ordinal)
                .ThenBy(row => row.Scope, StringComparer.Ordinal).Select(row => new ReviewSectionItem("parser-work",
                    new RuleWorkRow(row.Kind, row.Key, row.IdentityQuality, row.Scope, row.Name, row.Status,
                        row.Change.PercentChange, row.Change.UnavailableReason))).ToArray();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        { return null; }
    }
}
