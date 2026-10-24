using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Host.Assess;

/// <summary>
/// Runs again, serially and under a longer limit, the words a batch stopped at the per-word time limit, then
/// writes the retried rows over the stopped ones.
/// </summary>
/// <remarks>
/// The time limit is wall-clock inside PanGloss, so a loaded machine stops slow words that complete when run
/// unloaded, and each stopped word makes the correctness evidence incomplete. Only time-limit stops are
/// retried: a word that hit the analysis-attempt cap stopped for a reason the machine's load does not change.
/// A word that stops again under the longer limit keeps a stopped row, so a runaway search still blocks Apply.
/// </remarks>
internal static class StoppedWordRetryRunner
{
    internal sealed record Outcome(string Tsv, string Analyses, BatchInvocationEvidence Evidence,
        IReadOnlySet<int> RetriedIndices,
        IReadOnlyDictionary<string, PanGlossWordStatistics>? RetryStatistics);

    /// <summary>Returns the merged evidence, or null when there is nothing to retry or the retry could not be trusted.</summary>
    internal static async Task<Outcome?> RunAsync(
        IPanGlossInvoker invoker, IAssessorCachePathResolver paths, PanGlossRequest.Batch first,
        BatchInvocationEvidence evidence, string tsv, string analyses, Action<bool>? onAdmissionWait,
        CancellationToken cancellationToken)
    {
        if (first.PerWordLimit is not { } limit || evidence.AnalysesPath is null || evidence.AnalysesSha256 is null)
            return null;
        var stopped = BatchTsvParser.Parse(tsv).Where(row => row.Outcome == WordOutcome.TimedOut)
            .OrderBy(row => row.Index).ToArray();
        if (stopped.Length == 0) return null;
        var chosen = stopped.Take(StoppedWordRetry.MaxWords).ToArray();
        var retryLimit = TimeSpan.FromMilliseconds(Math.Min(
            limit.TotalMilliseconds * StoppedWordRetry.Multiplier, int.MaxValue - 1));
        var retryWords = chosen.Select(row => row.Word).ToArray();
        var retryDirectory = paths.DirectoryFor(Guid.NewGuid().ToString("N"));
        var retryStatsPath = first.StatsCachePath is null ? null : Path.Combine(retryDirectory, "retry-stats.sqlite");
        AssessmentArtifactLease? lease = null;
        try
        {
            var request = new PanGlossRequest.Batch(evidence.SourcePath, retryWords, retryLimit, retryStatsPath,
                first.PerWordStepLimit, retryDirectory)
            {
                CollectAnalyses = true,
                AllowMissingStatsCache = true,
                OnAdmissionWait = onAdmissionWait,
                ThreadCount = 1,
            };
            var outcome = await invoker.RunAsync(request, "assess:retry-stopped", cancellationToken)
                .ConfigureAwait(false);
            lease = (outcome as PanGlossOutcome.Completed)?.ArtifactLease;
            if (outcome is PanGlossOutcome.Cancelled) throw new OperationCanceledException(cancellationToken);
            if (outcome is not PanGlossOutcome.Completed { BatchEvidence: { } retry, MorphologyOutput: { } retryAnalyses } done)
                return Failed("the retry run did not complete: " + outcome.Message);
            if (retry.SourceBytesSha256 != evidence.SourceBytesSha256 ||
                retry.ExecutableBytesSha256 != evidence.ExecutableBytesSha256 ||
                retry.AnalysesPath is null || retry.AnalysesSha256 is null ||
                BatchInvocationEvidence.DigestFile(retry.TsvPath) != retry.TsvSha256 ||
                !BatchInvocationEvidence.TextMatchesDigest(done.Output, retry.TsvSha256) ||
                !BatchInvocationEvidence.TextMatchesDigest(retryAnalyses, retry.AnalysesSha256))
                return Failed("the retry run's source, parser or retained files did not match the first pass");
            var retryRows = BatchTsvParser.Parse(done.Output);
            if (retryRows.Count != retryWords.Length || retryRows.Where((row, i) =>
                row.Index != i || row.Word != retryWords[i]).Any())
                return Failed("the retry run did not return one ordered row for every retried word");
            var retryStatistics = ReadRetryStatistics(retryStatsPath, retryWords);

            var merged = Merge(tsv, analyses, chosen.Select(row => row.Index).ToArray(), done.Output, retryAnalyses);
            if (merged is null) return Failed("the retry rows could not be merged into the first-pass files");
            var directory = Path.GetDirectoryName(evidence.TsvPath)!;
            File.Copy(evidence.TsvPath, Path.Combine(directory, "first-pass.tsv"), overwrite: true);
            File.Copy(evidence.AnalysesPath, Path.Combine(directory, "first-pass-analyses.jsonl"), overwrite: true);
            File.Copy(retry.TsvPath, Path.Combine(directory, "retry.tsv"), overwrite: true);
            File.Copy(retry.AnalysesPath, Path.Combine(directory, "retry-analyses.jsonl"), overwrite: true);
            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(evidence.TsvPath, merged.Value.Tsv, utf8);
            File.WriteAllText(evidence.AnalysesPath, merged.Value.Analyses, utf8);

            var recovered = retryRows.Count(row => row.Outcome != WordOutcome.TimedOut);
            var lines = stopped.Select(row =>
            {
                var position = Array.IndexOf(chosen, row);
                if (position < 0)
                    return string.Join('\t', row.Index.ToString(CultureInfo.InvariantCulture), row.Word,
                        row.ElapsedMs.ToString(CultureInfo.InvariantCulture), "-", "not-retried");
                var again = retryRows[position];
                return string.Join('\t', row.Index.ToString(CultureInfo.InvariantCulture), row.Word,
                    row.ElapsedMs.ToString(CultureInfo.InvariantCulture),
                    again.ElapsedMs.ToString(CultureInfo.InvariantCulture),
                    again.Outcome == WordOutcome.TimedOut ? "still-stopped" : "recovered");
            });
            var record = new StoppedWordRetry((int)retryLimit.TotalMilliseconds, stopped.Length, chosen.Length,
                recovered, chosen.Length - recovered, retry.InvocationId, evidence.TsvSha256,
                evidence.AnalysesSha256, retry.TsvSha256, retry.AnalysesSha256, string.Join('\n', lines));
            return new Outcome(merged.Value.Tsv, merged.Value.Analyses, evidence with
            {
                TsvSha256 = BatchInvocationEvidence.DigestFile(evidence.TsvPath),
                AnalysesSha256 = BatchInvocationEvidence.DigestFile(evidence.AnalysesPath),
                StoppedWordRetry = record,
            }, chosen.Select(row => row.Index).ToHashSet(), retryStatistics);
        }
        finally
        {
            lease?.Dispose();
        }

        // The stopped rows stay stopped; the record says a retry was tried and why it gave nothing.
        Outcome Failed(string reason)
        {
            var rows = stopped.Select(row => string.Join('\t', row.Index.ToString(CultureInfo.InvariantCulture),
                row.Word, row.ElapsedMs.ToString(CultureInfo.InvariantCulture), "-", "retry-failed"));
            return new Outcome(tsv, analyses, evidence with
            {
                StoppedWordRetry = new StoppedWordRetry((int)retryLimit.TotalMilliseconds, stopped.Length,
                    chosen.Length, 0, chosen.Length, string.Empty, string.Empty, string.Empty, string.Empty,
                    string.Empty, string.Join('\n', rows)) { Failure = reason },
            }, new HashSet<int>(), null);
        }
    }

    private static IReadOnlyDictionary<string, PanGlossWordStatistics>? ReadRetryStatistics(
        string? cachePath, IReadOnlyCollection<string> words)
    {
        if (cachePath is null || !File.Exists(cachePath)) return null;
        try
        {
            return PanGlossBatchStatisticsReader.Read(cachePath, words, _ => 0, maxObjectTimingRows: 0).Words;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
            UnauthorizedAccessException or SqliteException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Replaces the rows at <paramref name="indices"/> with the retry's rows, in retry order, keeping each original index.</summary>
    internal static (string Tsv, string Analyses)? Merge(
        string tsv, string analyses, IReadOnlyList<int> indices, string retryTsv, string retryAnalyses)
    {
        var position = new Dictionary<int, int>();
        for (var i = 0; i < indices.Count; i++) position[indices[i]] = i;
        var retryTsvRows = new Dictionary<int, string[]>();
        foreach (var raw in retryTsv.Split('\n'))
        {
            var columns = raw.TrimEnd('\r').Split('\t');
            if (columns.Length >= 5 && int.TryParse(columns[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
                int.TryParse(columns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                retryTsvRows[index] = columns;
        }
        var replaced = new HashSet<int>();
        var outTsv = new List<string>();
        foreach (var raw in tsv.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var columns = line.Split('\t');
            if (columns.Length >= 5 && int.TryParse(columns[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
                int.TryParse(columns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
                position.TryGetValue(index, out var at))
            {
                if (!retryTsvRows.TryGetValue(at, out var row)) return null;
                row = (string[])row.Clone();
                row[0] = index.ToString(CultureInfo.InvariantCulture);
                line = string.Join('\t', row);
                replaced.Add(index);
            }
            outTsv.Add(line);
        }
        if (replaced.Count != indices.Count) return null;

        var retryLines = retryAnalyses.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        var lines = analyses.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        foreach (var index in indices)
        {
            if (index < 0 || index >= lines.Length || position[index] >= retryLines.Length) return null;
            if (JsonNode.Parse(retryLines[position[index]]) is not JsonObject node) return null;
            node["index"] = index;
            lines[index] = node.ToJsonString(options);
        }
        return (string.Join('\n', outTsv), string.Join('\n', lines) + "\n");
    }
}
