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
                    invocation.ApprovedExpectationsSha256 != expected ||
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
                    (record.CachePath is not null && !Matches(record.CachePath, record.CacheDigest))) continue;
                var detail = assessments.GetSelected(record.AssessmentId);
                if (detail.Words is null || detail.Words.Count != scope.ParsedWords.Count ||
                    detail.Words.Any(word => word.IsIncomplete) || !detail.Words.Select(word => word.Word)
                    .ToHashSet(StringComparer.Ordinal).SetEquals(scope.ParsedWords)) continue;
                if (scope.Collect.Contains(AssessmentKind.Correctness))
                {
                    var siblings = assessments.ListRecentBaselineHeaders(AssessmentKinds.Correctness, 50).ToList();
                    var boundCorrectness = new BeforeAssessmentBindingRepository(database).Find(baseline.SemanticIdentity,
                        AssessmentKinds.Correctness, invocation.InvocationId);
                    if (boundCorrectness is not null && assessments.GetHeader(boundCorrectness) is { } promotedCorrectness)
                        siblings.Insert(0, promotedCorrectness);
                    var sibling = siblings.FirstOrDefault(item => item.Invocation?.InvocationId == invocation.InvocationId &&
                        item.Selection.Sha256 == selection.Sha256 &&
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
