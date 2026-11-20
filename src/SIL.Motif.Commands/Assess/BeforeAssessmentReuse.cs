using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Assess;

internal static class BeforeAssessmentReuse
{
    public static bool MatchesBaseline(MotifDatabase database, AssessmentRecord before, AssessmentRecord after)
    {
        if (before.BaselineToken == after.BaselineToken) return true;
        try
        {
            var first = JsonSerializer.Deserialize<BaselineToken>(before.BaselineToken, MotifJson.CreateOptions());
            var second = JsonSerializer.Deserialize<BaselineToken>(after.BaselineToken, MotifJson.CreateOptions());
            return second is not null && (second.HasSameSemanticIdentity(first) ||
                new BeforeAssessmentBindingRepository(database).Contains(before.AssessmentId, second.SemanticIdentity));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException) { return false; }
    }

    /// <summary>The limits a Trial measures under, and where the time limit came from when an earlier measurement set it.</summary>
    /// <param name="Limits">The limits to measure under.</param>
    /// <param name="Detail">The earlier measurement whose time limit was kept, or null when the policy's own limit stands.</param>
    internal sealed record AdoptedSafetyNet(TrialLimits Limits, string? Detail);

    /// <summary>
    /// The limits a Trial measures under, with the wall-clock safety net an earlier measurement of this Baseline used when
    /// that net is at least as long as the one the project's parse rate sizes now. The net is re-sized from the latest
    /// parse rate on every run, so no earlier measurement would match it again; the parser work limit is what stops a
    /// word, and the net only has to be generous. Keeping the longer of the two lets a repeat Trial match by the limit
    /// exactly, while a net born of a small or fast run can never pin a shorter limit than the policy now asks for.
    /// </summary>
    /// <param name="database">The project's store, for its earlier measurements.</param>
    /// <param name="baseline">The Baseline the Trial measures.</param>
    /// <param name="limits">The limits resolved from the project's policy.</param>
    /// <param name="parserPath">The parser the Trial measures with; only a measurement by the same bytes is followed.</param>
    internal static AdoptedSafetyNet AdoptEarlierSafetyNet(MotifDatabase database, BaselineToken baseline, TrialLimits limits,
        string? parserPath)
    {
        var kept = new AdoptedSafetyNet(limits, null);
        if (limits.PerWordLimit is not { } current || parserPath is null) return kept;
        try
        {
            var parserHash = BatchInvocationEvidence.DigestFile(parserPath);
            foreach (var record in new AssessmentRepository(database)
                .ListRecentBaselineHeaders(AssessmentKind.ParseTime.ToStoredKind(), 50))
            {
                if (record.Assessor != PanGlossAssessor.AssessorName) continue;
                var token = JsonSerializer.Deserialize<BaselineToken>(record.BaselineToken, MotifJson.CreateOptions());
                if (!baseline.HasSameSemanticIdentity(token)) continue;
                var invocation = record.Invocation;
                if (invocation is null || invocation.StoppedWordRetry is not null ||
                    invocation.ExecutableBytesSha256 != parserHash ||
                    invocation.PerWordParserWorkLimit != limits.PerWordParserWorkLimit ||
                    invocation.PerWordTimeoutMs is not { } milliseconds) continue;
                if (milliseconds < current.TotalMilliseconds) return kept;
                return new AdoptedSafetyNet(limits with { PerWordLimit = TimeSpan.FromMilliseconds(milliseconds) },
                    $"time limit {milliseconds} ms kept from assessment {record.AssessmentId} saved {record.SavedUtc}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidDataException or JsonException or ArgumentException or KeyNotFoundException)
        {
            return kept;
        }
        return kept;
    }

    public static string? Find(MotifDatabase database, AssessmentRepository assessments, BaselineToken baseline,
        Selection selection, AssessmentScope.Words scope, LcmCache cache, string parserPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var parserHash = BatchInvocationEvidence.DigestFile(parserPath);
            var version = ParserExecutableIdentity.ReadFreshVersion(parserPath);
            if (version == "unavailable") return null;
            var expected = ApprovedExpectationsDigest.Compute(ApprovedMorphologyReader.Read(cache,
                ApprovedMorphologyReader.FormsOf(scope.ParsedWords)));
            var bound = new BeforeAssessmentBindingRepository(database).Find(baseline.SemanticIdentity,
                AssessmentKind.ParseTime.ToStoredKind());
            var candidates = assessments.ListRecentBaselineHeaders(AssessmentKind.ParseTime.ToStoredKind(), 50).ToList();
            if (bound is not null && assessments.GetHeader(bound) is { } promoted) candidates.Insert(0, promoted);
            foreach (var record in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (record.Assessor != PanGlossAssessor.AssessorName) continue;
                var token = JsonSerializer.Deserialize<BaselineToken>(record.BaselineToken, MotifJson.CreateOptions());
                if (record.AssessmentId != bound && !baseline.HasSameSemanticIdentity(token)) continue;
                var invocation = record.Invocation;
                if (invocation is null || invocation.StoppedWordRetry is not null ||
                    invocation.ExecutableBytesSha256 != parserHash || invocation.ExecutableVersion != version ||
                    invocation.GrammarExportFormat is null ||
                    invocation.GrammarExportFormat != GrammarExportFormats.ForPath(cache.ProjectId.Path) ||
                    (scope.Collect.Contains(AssessmentKind.Correctness) && invocation.ApprovedExpectationsSha256 != expected) ||
                    invocation.CollectStatistics != scope.Collect.Contains(AssessmentKind.ObjectTiming) ||
                    invocation.PerWordTimeoutMs != (scope.PerWordLimit is { } limit ? (int)limit.TotalMilliseconds : null) ||
                    invocation.PerWordParserWorkLimit != scope.PerWordParserWorkLimit ||
                    !record.Selection.HashMatchesStoredWords() || record.Selection.Sha256 != selection.Sha256 ||
                    !record.Selection.Words.SequenceEqual(selection.Words, StringComparer.Ordinal)) continue;
                var stored = ScopeCodec.ReadTrial(record.ScopeJson, "reuse");
                var collected = stored.Collect.Count == 0
                    ? new[] { AssessmentKind.ParseTime, AssessmentKind.ObjectTiming, AssessmentKind.Correctness }
                    : stored.Collect;
                if (!stored.Words.SequenceEqual(scope.WordForms, StringComparer.Ordinal) ||
                    !collected.ToHashSet().SetEquals(scope.Collect)) continue;
                if (!Matches(invocation.SourcePath, invocation.SourceBytesSha256) ||
                    !Matches(invocation.WordsPath, invocation.WordsSha256) ||
                    !Matches(invocation.TsvPath, invocation.TsvSha256) ||
                    !Matches(invocation.StandardErrorPath, invocation.StandardErrorSha256) ||
                    !Matches(invocation.AnalysesPath, invocation.AnalysesSha256) ||
                    ((invocation.ImportedSnapshotPath is null) != (invocation.ImportedSnapshotSha256 is null)) ||
                    (invocation.ImportedSnapshotPath is not null &&
                        !Matches(invocation.ImportedSnapshotPath, invocation.ImportedSnapshotSha256)) ||
                    (record.CachePath is not null && !Matches(record.CachePath, record.CacheDigest))) continue;
                var detail = assessments.GetSelected(record.AssessmentId);
                if (detail.Words is null || detail.Words.Count != scope.ParsedWords.Count ||
                    detail.Words.Any(word => word.IsIncomplete) || !detail.Words.Select(word => word.Word)
                    .ToHashSet(StringComparer.Ordinal).SetEquals(scope.ParsedWords)) continue;
                if (scope.Collect.Contains(AssessmentKind.ObjectTiming))
                {
                    var timing = assessments.ListByInvocationAndKind(invocation.InvocationId, AssessmentKinds.ObjectTiming)
                        .FirstOrDefault(item => item.Selection.Sha256 == selection.Sha256 &&
                            item.Selection.Words.SequenceEqual(selection.Words, StringComparer.Ordinal));
                    if (timing is null || !Matches(timing.CachePath, timing.CacheDigest)) continue;
                    PanGlossBatchStatisticsReader.ReadWords(timing.CachePath!, scope.ParsedWords);
                }
                if (scope.Collect.Contains(AssessmentKind.Correctness))
                {
                    var sibling = assessments.ListByInvocationAndKind(invocation.InvocationId, AssessmentKinds.Correctness)
                        .FirstOrDefault(item => item.Selection.Sha256 == selection.Sha256 &&
                            item.Selection.Words.SequenceEqual(selection.Words, StringComparer.Ordinal));
                    if (sibling is null) continue;
                    var correctness = assessments.GetSelected(sibling.AssessmentId).Words;
                    if (correctness is null || correctness.Count != scope.ParsedWords.Count ||
                        correctness.Any(word => word.Correctness is null || word.IsIncomplete) ||
                        !correctness.Select(word => word.Word).ToHashSet(StringComparer.Ordinal).SetEquals(scope.ParsedWords))
                        continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                return record.AssessmentId;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidDataException or JsonException or ArgumentException or KeyNotFoundException or ReportRefusalException or
            AssessmentExpiredException)
        {
            return null;
        }
        return null;
    }

    private static bool Matches(string? path, string? digest) =>
        !string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(digest) &&
        BatchInvocationEvidence.DigestFile(path) == digest;
}
