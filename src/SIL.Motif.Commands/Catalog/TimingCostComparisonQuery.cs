using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using SIL.Motif.Worker.Runtime.Review;
using SIL.Motif.Contract;
using System.Text.Json;

namespace SIL.Motif.Commands.Catalog;

/// <summary>Compares only the Baseline Assessments named by the Trial that produced the selected run.</summary>
public static class TimingCostComparisonQuery
{
    /// <summary>Reads an exact retained pair; absent or ambiguous evidence remains unavailable.</summary>
    public static TimingCostComparison Read(MotifDatabase database, string? jobId, AssessmentRecord after,
        IReadOnlyList<string> words, bool hasReplacements, string by = "rule")
    {
        if (jobId is null || hasReplacements)
            return TimingCostComparison.Unavailable("No single paired Trial Assessment was selected.");
        var job = new JobRepository(database).Get(jobId);
        if (job is null) return TimingCostComparison.Unavailable("The Trial job is no longer retained.");
        TrialJobInput input;
        try { input = TrialJobInput.Parse(job.InputJson); }
        catch (Exception exception) when (exception is InvalidOperationException or System.Text.Json.JsonException)
        { return TimingCostComparison.Unavailable("The Trial's paired Assessment references are unavailable."); }
        var ids = BaselineIds(input, job.ResultJson, after.AssessmentId).Distinct(StringComparer.Ordinal);
        var repository = new AssessmentRepository(database);
        var candidates = new List<AssessmentRecord>();
        foreach (var id in ids)
        {
            AssessmentRecord before;
            try { before = repository.Get(id); }
            catch (KeyNotFoundException) { continue; }
            if (before.Kind != "ParseTime" || before.ProposalId is not null || !TrialWorkComparison.AreCompatible(before, after)) continue;
            var available = (before.Words ?? []).Select(row => row.Word).ToHashSet(StringComparer.Ordinal);
            if (words.All(available.Contains)) candidates.Add(before);
        }
        return candidates.Count == 1 ? Compare(candidates[0], after, words, by)
            : TimingCostComparison.Unavailable(candidates.Count == 0
                ? "No compatible retained Baseline Assessment covers these words."
                : "More than one retained Baseline Assessment covers these words.");
    }

    internal static IReadOnlyList<string> BaselineIds(TrialJobInput input, string? resultJson, string afterId)
    {
        if (input.ReviewInputs is null) return input.BaselineAssessmentIds ?? [];
        if (resultJson is null) return [];
        try
        {
            using var result = JsonDocument.Parse(resultJson);
            if (result.RootElement.ValueKind != JsonValueKind.Object ||
                !result.RootElement.TryGetProperty("reviewAfter", out var node)) return [];
            var after = node.Deserialize<ReviewAfterPartition[]>(MotifJson.CreateOptions()) ?? [];
            var matching = after.Where(partition => partition is not null && partition.AssessmentIds is not null &&
                partition.AssessmentIds.Contains(afterId, StringComparer.Ordinal)).ToArray();
            if (matching.Length != 1) return [];
            var candidate = matching[0];
            if (string.IsNullOrWhiteSpace(candidate.WritingSystemId) || candidate.CaseKeys is null) return [];
            var before = (input.ReviewBaselines ?? []).Where(partition =>
                partition is not null && partition.CaseKeys is not null && partition.AssessmentIds is not null &&
                partition.WritingSystemId == candidate.WritingSystemId &&
                partition.CaseKeys.ToHashSet(StringComparer.Ordinal).SetEquals(candidate.CaseKeys)).ToArray();
            return before.Length == 1 ? before[0].AssessmentIds : [];
        }
        catch (JsonException) { return []; }
    }

    /// <summary>Compares one exact pair over the requested word identities.</summary>
    public static TimingCostComparison Compare(AssessmentRecord before, AssessmentRecord after, IReadOnlyList<string> words,
        string by = "rule")
    {
        if (!TrialWorkComparison.AreCompatible(before, after))
            return TimingCostComparison.Unavailable("The paired Assessments have incompatible parser or scope evidence.");
        var requested = words.ToHashSet(StringComparer.Ordinal);
        var left = (before.Words ?? []).Where(row => requested.Contains(row.Word)).ToArray();
        var right = (after.Words ?? []).Where(row => requested.Contains(row.Word)).ToArray();
        if (requested.Count != words.Count || left.Length != requested.Count || right.Length != requested.Count ||
            !requested.SetEquals(left.Select(row => row.Word)) || !requested.SetEquals(right.Select(row => row.Word)))
            return TimingCostComparison.Unavailable("Both Assessments must contain every selected word exactly once.");
        TimingCostChange Change(IReadOnlyList<SIL.Motif.Host.Parser.AssessedWord> oldWords,
            IReadOnlyList<SIL.Motif.Host.Parser.AssessedWord> newWords)
        {
            var result = TrialWorkComparison.Compare(oldWords, newWords);
            return result.PercentChange is { } value ? new TimingCostChange(value, null)
                : TimingCostChange.Unavailable("Complete comparable work is unavailable or the Baseline work is zero.");
        }
        var leftIndex = left.ToDictionary(row => row.Word, StringComparer.Ordinal);
        var rightIndex = right.ToDictionary(row => row.Word, StringComparer.Ordinal);
        var byWord = words.Select(word => new TimingWordCostChange(word,
            Change([leftIndex[word]], [rightIndex[word]]))).ToArray();
        var total = Change(left, right);
        IReadOnlyList<TimingObjectCostChange> objects = total.PercentChange is null ? [] : TimingObjectComparison.Compare(before.ObjectTimings, after.ObjectTimings, requested,
            before.TimingEvidence is { DetailComplete: true, StatisticsUnavailable: false, HasRetriedWords: false } &&
            after.TimingEvidence is { DetailComplete: true, StatisticsUnavailable: false, HasRetriedWords: false }, by);
        if (by == "rule" && total.PercentChange is not null)
            objects = objects.Append(TimingObjectComparison.Unattributed(before.ObjectTimings, after.ObjectTimings, requested,
                left.Sum(row => (decimal)row.ParserWork!.Value), right.Sum(row => (decimal)row.ParserWork!.Value),
                before.TimingEvidence is { DetailComplete: true, StatisticsUnavailable: false, HasRetriedWords: false } &&
                after.TimingEvidence is { DetailComplete: true, StatisticsUnavailable: false, HasRetriedWords: false })).ToArray();
        return new TimingCostComparison(total, byWord)
        {
            Objects = objects,
            ObjectsUnavailableReason = objects.Count == 0 ? "No complete paired object detail is available." : null,
        };
    }


}
