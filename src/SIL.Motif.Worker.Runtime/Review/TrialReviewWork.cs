using System.Security.Cryptography;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Runtime.Review;

internal static class TrialReviewWork
{
    internal static ReviewWorkInputs? Read(MotifDatabase database, TrialJobInput input, string? resultJson,
        FrozenReviewInputs inputs, BaselineToken baseline, TrialReviewLedger ledger)
    {
        if (input.ReviewCounterSemantics is null || input.ReviewBaselines is null || resultJson is null || inputs.ParserDigest is null)
            return null;
        try
        {
            using var document = JsonDocument.Parse(resultJson);
            if (!document.RootElement.TryGetProperty("reviewCounterSemantics", out var semantics) ||
                !semantics.TryGetInt32(out var afterSemantics) || afterSemantics != input.ReviewCounterSemantics ||
                !document.RootElement.TryGetProperty("reviewAfter", out var json) || json.ValueKind != JsonValueKind.Array) return null;
            var afterPartitions = json.Deserialize<ReviewAfterPartition[]>(MotifJson.CreateOptions());
            if (afterPartitions is null) return null;
            var beforeParts = input.ReviewBaselines.Select(part => (part.WritingSystemId, part.CaseKeys, part.AssessmentIds)).ToArray();
            var afterParts = afterPartitions.Select(part => (part.WritingSystemId, part.CaseKeys, part.AssessmentIds)).ToArray();
            var before = Side(database, beforeParts, inputs, ledger.Before, baseline, input.ReviewCounterSemantics.Value);
            var after = Side(database, afterParts, inputs, ledger.After, baseline, input.ReviewCounterSemantics.Value);
            if (before is null || after is null) return null;
            return DeduplicateRequests(inputs, new ReviewWorkInputs(before.Value.Scope, after.Value.Scope,
                inputs.Cases.Select(item => item.CaseKey).ToArray(), before.Value.Cases, after.Value.Cases));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        { return null; }
    }

    internal static ReviewWorkInputs DeduplicateRequests(FrozenReviewInputs inputs, ReviewWorkInputs work)
    {
        var groups = inputs.Cases.GroupBy(item => (item.WritingSystemId, item.Word))
            .Select(group => group.Select(item => item.CaseKey).Order(StringComparer.Ordinal).ToArray()).ToArray();
        IReadOnlyList<ReviewWorkCase> Reduce(IReadOnlyList<ReviewWorkCase> side)
        {
            var index = side.ToDictionary(item => item.CaseKey, StringComparer.Ordinal);
            return groups.Select(keys =>
            {
                var rows = keys.Select(key => index.GetValueOrDefault(key)).ToArray();
                var value = rows[0]?.Work;
                return new ReviewWorkCase(keys[0], rows.All(row => row is { Complete: true } && row.Work == value), value);
            }).ToArray();
        }
        return work with { RequiredCases = groups.Select(keys => keys[0]).ToArray(),
            Before = Reduce(work.Before), After = Reduce(work.After) };
    }

    private static (ReviewWorkScope Scope, IReadOnlyList<ReviewWorkCase> Cases)? Side(MotifDatabase database,
        (string? WritingSystemId, IReadOnlyList<string> CaseKeys, IReadOnlyList<string> AssessmentIds)[] partitions,
        FrozenReviewInputs inputs, IReadOnlyList<TrialReviewObservation> observations, BaselineToken baseline, int semantics)
    {
        var repository = new AssessmentRepository(database);
        var planned = inputs.Cases.ToDictionary(item => item.CaseKey, StringComparer.Ordinal);
        var observed = observations.ToDictionary(item => item.CaseKey, StringComparer.Ordinal);
        var cases = new List<ReviewWorkCase>();
        var settings = new List<object>();
        var tokenisers = new List<object>();
        var assessors = new HashSet<string>(StringComparer.Ordinal);
        var parsers = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var partition in partitions.OrderBy(item => item.WritingSystemId, StringComparer.Ordinal))
        {
            var records = partition.AssessmentIds.Select(repository.Get).Where(record => record.Kind == AssessmentKinds.ParseTime).ToArray();
            if (records.Length != 1 || records[0].Invocation is not { } invocation || records[0].Words is not { } words) return null;
            var record = records[0];
            if (JsonSerializer.Deserialize<BaselineToken>(record.BaselineToken, MotifJson.CreateOptions()) != baseline ||
                invocation.ExecutableBytesSha256 != inputs.ParserDigest || !invocation.CollectStatistics) return null;
            assessors.Add(record.Assessor);
            parsers.Add(invocation.ExecutableBytesSha256);
            var keys = partition.CaseKeys.Order(StringComparer.Ordinal).ToArray();
            settings.Add(new { partition.WritingSystemId, keys, invocation.PerWordTimeoutMs,
                invocation.PerWordParserWorkLimit, invocation.Threads, invocation.CollectStatistics });
            tokenisers.Add(new { partition.WritingSystemId, record.TokeniserName, record.TokeniserVersion });
            var byWord = words.GroupBy(item => item.Word, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            foreach (var key in keys)
            {
                if (!seen.Add(key) || !planned.TryGetValue(key, out var item) || item.WritingSystemId != partition.WritingSystemId ||
                    !record.Selection.Words.Contains(item.Word, StringComparer.Ordinal)) return null;
                if (!byWord.TryGetValue(item.Word, out var rows) || rows.Length != 1) return null;
                var complete = observed.TryGetValue(key, out var observation) && observation.Completion == Completion.Complete && !rows[0].IsIncomplete;
                cases.Add(new ReviewWorkCase(key, complete, rows[0].ParserWork));
            }
        }
        if (seen.Count != planned.Count || assessors.Count != 1 || parsers.Count != 1) return null;
        var baselineDigest = Digest(baseline);
        return (new ReviewWorkScope(inputs.ScopeDigest, baselineDigest, assessors.Single(), parsers.Single(), semantics.ToString(),
            Digest(settings), Digest(tokenisers)), cases);
    }

    private static string Digest<T>(T value) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(value, MotifJson.CreateOptions())));
}
