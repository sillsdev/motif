using SIL.Motif.Host.Texts;

namespace SIL.Motif.Worker.Baselines;

internal sealed record ProgressIncidence(Guid WordformId, Guid? AnalysisId, string WritingSystem, string Spelling,
    string Bucket, string? Reason, int Count);
internal sealed record ProgressText(Guid Id, string Title, IReadOnlyList<ProgressIncidence> Incidence,
    IReadOnlyDictionary<string, int> Excluded);
internal sealed record ProgressProjection(IReadOnlyList<ProgressText> Texts);
internal sealed record ProgressBuckets(int Total, int Approved, int Misspelled, int Unmeasured,
    IReadOnlyDictionary<string, int> Reasons);
internal sealed record ProgressCounts(ProgressBuckets Tokens, ProgressBuckets Types);

internal static class ProgressProjectionBuilder
{
    public static ProgressProjection Build(TextWordsProjection projection)
    {
        var wordforms = projection.Wordforms.ToDictionary(word => word.WordformId);
        var texts = new List<ProgressText>();
        foreach (var text in projection.Texts)
        {
            var counts = new Dictionary<(Guid Wordform, Guid? Analysis, string Ws, string Form, string Bucket, string? Reason), int>();
            var excluded = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var token in text.Lines.SelectMany(line => line.Tokens))
            {
                if (token.WordformId is not { } id) continue;
                if (!wordforms.TryGetValue(id, out var word))
                    throw new InvalidDataException("A Text occurrence has no projected wordform.");
                if (word.CoverageExclusion is { } exclusion)
                {
                    excluded[exclusion] = excluded.GetValueOrDefault(exclusion) + 1;
                    continue;
                }
                if (word.CoverageSpelling is null || word.CoverageWritingSystem is null)
                    throw new InvalidDataException("A Text occurrence has no coverage identity.");
                var bucket = word.IncorrectSpelling ? "misspelled" : token.Status == InterlinearAnalysisStatus.Approved
                    ? "approved" : "unmeasured";
                var reason = bucket != "unmeasured" ? null : word.CandidateCount > 0
                    ? "fieldworks-candidate-unclassified" : "parser-evidence-unavailable";
                var key = (id, token.AnalysisId, word.CoverageWritingSystem, word.CoverageSpelling, bucket, reason);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
            texts.Add(new ProgressText(text.TextId, text.Title,
                counts.Select(pair => new ProgressIncidence(pair.Key.Wordform, pair.Key.Analysis, pair.Key.Ws, pair.Key.Form, pair.Key.Bucket,
                    pair.Key.Reason, pair.Value)).ToArray(), excluded));
        }
        return new ProgressProjection(texts);
    }

    public static ProgressCounts Aggregate(IEnumerable<ProgressText> texts)
    {
        var tokenTotal = 0;
        var tokenApproved = 0;
        var tokenMisspelled = 0;
        var tokenUnmeasured = 0;
        var tokenReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var byType = new Dictionary<(string WritingSystem, string Spelling), (string Bucket, string? Reason)>();
        foreach (var row in texts.SelectMany(text => text.Incidence))
        {
            tokenTotal += row.Count;
            if (row.Bucket == "approved") tokenApproved += row.Count;
            else if (row.Bucket == "misspelled") tokenMisspelled += row.Count;
            else
            {
                tokenUnmeasured += row.Count;
                if (row.Reason is not null)
                    tokenReasons[row.Reason] = tokenReasons.GetValueOrDefault(row.Reason) + row.Count;
            }
            var key = (row.WritingSystem, row.Spelling);
            if (!byType.TryGetValue(key, out var previous) || Rank(row.Bucket) > Rank(previous.Bucket) ||
                row.Bucket == "unmeasured" && previous.Bucket == row.Bucket &&
                StringComparer.Ordinal.Compare(row.Reason, previous.Reason) < 0)
                byType[key] = (row.Bucket, row.Reason);
        }
        var tokens = new ProgressBuckets(tokenTotal, tokenApproved, tokenMisspelled, tokenUnmeasured, tokenReasons);
        var types = Count(byType.Values.Select(value => (value.Bucket, value.Reason, 1)));
        return new ProgressCounts(tokens, types);
    }

    private static int Rank(string bucket) => bucket == "misspelled" ? 2 : bucket == "unmeasured" ? 1 : 0;

    private static ProgressBuckets Count(IEnumerable<(string Bucket, string? Reason, int Count)> rows)
    {
        var total = 0;
        var approved = 0;
        var misspelled = 0;
        var unmeasured = 0;
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (bucket, reason, count) in rows)
        {
            total += count;
            if (bucket == "approved") approved += count;
            else if (bucket == "misspelled") misspelled += count;
            else
            {
                unmeasured += count;
                if (reason is not null) reasons[reason] = reasons.GetValueOrDefault(reason) + count;
            }
        }
        return new ProgressBuckets(total, approved, misspelled, unmeasured, reasons);
    }
}
