using System.Globalization;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Catalog;

/// <summary>Reads a typed project Overview from the current Baseline and stored Assessment.</summary>
public static class OverviewCommand
{
    /// <summary>Returns the latest Assessment matching the exact current Baseline and resolved default Selection.</summary>
    public static CommandOutcome<OverviewResponse> Overview(OverviewRequest request) =>
        ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var current = CurrentEvidenceQuery.ReadCurrentEvidence(database, project);
            if (!current.Succeeded)
                return CommandOutcome<OverviewResponse>.Refused(current.Refusal!);
            var evidence = current.Value!;
            var summary = evidence.ProjectSummary;
            var selection = evidence.Selection;
            TextOccurrenceSnapshot? occurrenceSnapshot = selection is null ? null :
                new TextOccurrenceSnapshot(selection.OccurrencesByWord,
                    new Dictionary<string, int>(StringComparer.Ordinal), selection.TotalOccurrences, 0);
            var words = selection?.Selection.Words ?? evidence.DefaultSelection?.AddedWords ?? Array.Empty<string>();
            var assessment = evidence.MatchingAssessment;
            var assessedWords = evidence.EffectiveWords;
            var metrics = OverviewMetrics.Build(words, occurrenceSnapshot, assessedWords);
            int? elapsedMs = assessment is null ? null : assessedWords.Where(word => word.ElapsedMs is not null)
                .Sum(word => word.ElapsedMs!.Value);
            var storedCheck = evidence.Baseline is null ? null : new GrammarCheckRepository(database).GetLatest(
                System.Text.Json.JsonSerializer.Serialize(evidence.Baseline.Token, MotifJson.CreateOptions()));
            var warningCounts = WarningsCommand.FromCheck(storedCheck);
            var largestKind = warningCounts.ByKind.FirstOrDefault();
            return CommandOutcome<OverviewResponse>.Success(new OverviewResponse(
                evidence.ProjectName, evidence.MotifStoreCreatedUtc, evidence.LastFieldWorksSaveUtc,
                words.Count, selection?.TextCount ?? evidence.DefaultSelection?.TextIds.Count ?? 0,
                selection?.AddedWordCount ?? evidence.DefaultSelection?.AddedWords.Count ?? 0,
                selection?.TotalOccurrences ?? 0, summary?.WordformCount ?? 0, summary?.RuleCount ?? 0,
                summary?.LexemeCount ?? 0, assessment?.AssessmentId,
                assessment is null ? null : DateTimeOffset.Parse(
                    assessment.SavedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                elapsedMs is null ? null : elapsedMs.Value / 1000d,
                assessment?.GrammarSourceSha256, selection?.Selection.Sha256,
                metrics.TextCoverage, metrics.Accuracy,
                assessment is null ? TimingAggregation.SummarizeWords(Array.Empty<AssessedWord>())
                    : TimingAggregation.SummarizeWords(assessedWords),
                Warnings: storedCheck is null ? null : new OverviewWarningsSummary(
                    warningCounts.TotalCount, warningCounts.WarningCount, largestKind?.GroupName,
                    largestKind?.Count)
                {
                    WarningCount = warningCounts.WarningCount,
                    InformationCount = warningCounts.InformationCount,
                    ByKind = warningCounts.ByKind,
                })
            {
                SelectionResolved = selection is not null,
                WordCoveragePercent = selection is null
                    ? null : OverviewMetrics.Percent(metrics.TextCoverage.ParsedWords, words.Count),
                ProjectFileName = Path.GetFileName(project.FullFwDataPath),
                BaselineCapturedUtc = evidence.Baseline is null
                    ? null : DateTimeOffset.Parse(evidence.Baseline.Token.CapturedUtc,
                        CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                BaselineSourceLastWriteUtc = evidence.Baseline?.SourceLastWriteUtc,
                IsStale = evidence.Freshness == EvidenceFreshness.Stale,
                BaselineToken = evidence.Baseline?.Token,
            });
        });
}

internal static class OverviewMetrics
{
    public static (OverviewTextCoverage TextCoverage, OverviewAccuracy Accuracy) Build(
        IReadOnlyList<string> selectionWords,
        TextOccurrenceSnapshot? occurrences,
        IReadOnlyList<AssessedWord> assessedWords)
    {
        var byWord = assessedWords.ToDictionary(word => word.Word, StringComparer.Ordinal);
        var parsed = 0;
        var noParse = 0;
        var unknown = 0;
        var skipped = 0;
        var parsedOccurrences = 0;
        var approvedKept = 0;
        var approved = 0;
        var violations = 0;
        var accuracyUnknown = 0;
        var rejectedAnalysesRebuilt = 0;
        var rejectedWordsInMatchCell = 0;
        var rejected = 0;
        var candidatesConfirmed = 0;
        var candidates = 0;
        var approvedNoMatch = 0;
        var approvedNoParse = 0;
        var approvedUnknown = 0;
        var approvedSkipped = 0;

        foreach (var form in selectionWords)
        {
            if (!byWord.TryGetValue(form, out var word))
            {
                if (assessedWords.Count > 0) unknown++;
                continue;
            }
            var placement = CompareSemantics.Place(new CompareWordFacts(
                word.ProjectStanding, word.Outcome,
                word.IsIncomplete, word.Morphology,
                word.ReadingGrades, word.MissedApprovedCount ?? 0));
            rejectedAnalysesRebuilt += word.ReadingGrades?.Count(grade =>
                StringComparer.Ordinal.Equals(grade, "disapproved")) ?? 0;
            switch (placement.Column)
            {
                case CompareColumnKind.Match:
                case CompareColumnKind.NoMatch:
                    parsed++;
                    parsedOccurrences += occurrences?.OccurrencesByWord.GetValueOrDefault(form) ?? 0;
                    break;
                case CompareColumnKind.NoParse:
                    noParse++;
                    break;
                case CompareColumnKind.Timeout:
                    unknown++;
                    break;
                case CompareColumnKind.Skipped:
                    skipped++;
                    break;
            }
            var meaning = CompareSemantics.MeaningOf(placement.Standing, placement.Column);
            if (StringComparer.Ordinal.Equals(placement.Standing, SIL.Motif.Contract.Responses.ProjectStanding.Approved))
            {
                approved++;
                switch (placement.Column)
                {
                    case CompareColumnKind.Match:
                        approvedKept++;
                        break;
                    case CompareColumnKind.NoMatch:
                        approvedNoMatch++;
                        break;
                    case CompareColumnKind.NoParse:
                        approvedNoParse++;
                        break;
                    case CompareColumnKind.Timeout:
                        approvedUnknown++;
                        break;
                    case CompareColumnKind.Skipped:
                        approvedSkipped++;
                        break;
                }
            }
            if (meaning.Family == CompareFamilyKind.Violation) violations++;
            if (meaning.Family == CompareFamilyKind.Unknown) accuracyUnknown++;
            if (StringComparer.Ordinal.Equals(placement.Standing, SIL.Motif.Contract.Responses.ProjectStanding.Rejected))
            {
                rejected++;
                if (placement.Column == CompareColumnKind.Match) rejectedWordsInMatchCell++;
            }
            if (StringComparer.Ordinal.Equals(placement.Standing, SIL.Motif.Contract.Responses.ProjectStanding.Candidate))
            {
                candidates++;
                if (placement.Column == CompareColumnKind.Match) candidatesConfirmed++;
            }
        }

        var totalOccurrences = occurrences is null ? 0 : selectionWords.Sum(form => occurrences.OccurrencesByWord.GetValueOrDefault(form));
        return (
            new OverviewTextCoverage(parsed, noParse, unknown, skipped, totalOccurrences, parsedOccurrences)
            {
                OccurrenceCoveragePercent = Percent(parsedOccurrences, totalOccurrences),
            },
            new OverviewAccuracy(approvedKept, approved, violations, accuracyUnknown,
                rejectedAnalysesRebuilt, rejected, candidatesConfirmed, candidates)
            {
                RejectedWordsInMatchCell = rejectedWordsInMatchCell,
                ApprovedWordsNoMatch = approvedNoMatch,
                ApprovedWordsNoParse = approvedNoParse,
                ApprovedWordsUnknown = approvedUnknown,
                ApprovedWordsSkipped = approvedSkipped,
            });
    }

    internal static double? Percent(int numerator, int denominator) => denominator == 0
        ? null : numerator * 100d / denominator;
}
