using System.Globalization;
using SIL.Motif.Commands.Assess;
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
            var current = CurrentEvidenceQuery.ReadCurrentEvidence(database, project,
                includeResolvedReadings: false, includeWordContext: false);
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
            var timing = TimingAggregation.SummarizeWords(assessedWords, evidence.EffectiveObjectTimings);
            var storedCheck = evidence.Baseline is null ? null : new GrammarCheckRepository(database).GetLatest(
                System.Text.Json.JsonSerializer.Serialize(evidence.Baseline.Token, MotifJson.CreateOptions()));
            var needsAssociations = assessedWords.Count(word => word.ProjectStanding == ProjectStanding.Approved &&
                word.Outcome == "no-analysis" && !word.IsIncomplete) >= 2 || storedCheck is { Findings.Count: > 0 };
            var needsEntryComparisons = assessedWords.Any(word => CompareSemantics.PlacementOf(
                CompareSemantics.Compare(AssessmentWordRows.FromRecorded(word))) is
                { Standing: ProjectStanding.Approved, Column: CompareColumnKind.NoMatch });
            var context = needsAssociations || needsEntryComparisons
                ? CurrentEvidenceQuery.ReadWordContext(evidence, project.FullFwDataPath,
                    includeResolvedReadings: needsEntryComparisons)
                : CommandOutcome<CurrentEvidenceSnapshot>.Success(evidence);
            var associationEvidence = context.Succeeded ? context.Value! : evidence;
            var assessmentRows = associationEvidence.Assessment;
            if (storedCheck is not null)
            {
                storedCheck = StoredParseWarnings.Merge(storedCheck, associationEvidence);
                storedCheck = associationEvidence.WordContextAvailable && assessmentRows is not null
                    ? WarningWordsQuery.WithYourWords(storedCheck, assessmentRows.Words, evidence.EffectiveObjectTimings)
                    : storedCheck with
                    {
                        Findings = storedCheck.Findings.Select(finding => finding with { YourWords = null }).ToArray(),
                    };
            }
            var lookFirst = assessmentRows is not null
                ? OverviewLookFirstBuilder.Build(assessmentRows.Words, evidence.EffectiveWords, storedCheck,
                    associationEvidence.WordContextAvailable)
                : OverviewLookFirst.Empty;
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
                assessment is null ? null : timing.Attribution.WordTimeMs / 1000d,
                assessment?.GrammarSourceSha256, selection?.Selection.Sha256,
                metrics.TextCoverage, metrics.Accuracy,
                timing,
                Warnings: storedCheck is null ? null : new OverviewWarningsSummary(
                    warningCounts.TotalCount, warningCounts.WarningCount, largestKind?.GroupName,
                    largestKind?.Count)
                {
                    ErrorCount = warningCounts.ErrorCount,
                    WarningCount = warningCounts.WarningCount,
                    InformationCount = warningCounts.InformationCount,
                    ByKind = warningCounts.ByKind,
                    YourWords = warningCounts.YourWords,
                })
            {
                WritingSystems = summary?.WritingSystems ?? [],
                WordOrigins = assessedWords.Where(word => word.Origin is not null).ToDictionary(
                    word => word.Word, word => word.Origin!, StringComparer.Ordinal),
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
                LookFirst = lookFirst,
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
        var same = 0;
        var different = 0;
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
            var comparison = CompareSemantics.Compare(new CompareWordFacts(word.ProjectStanding, word.Outcome,
                word.IsIncomplete, word.Morphology, word.ReadingGrades, word.MissedApprovedCount ?? 0)
            { AnalysisComparison = word.AnalysisComparison });
            var placement = CompareSemantics.PlacementOf(comparison);
            rejectedAnalysesRebuilt += comparison.Availability == AnalysisComparisonAvailability.Available
                ? comparison.Readings.Count(reading => reading.Matches.Any(match => match.Opinion == ReadingGrade.Disapproved))
                : word.ReadingGrades?.Count(grade => grade == ReadingGrade.Disapproved) ?? 0;
            switch (placement.Column)
            {
                case CompareColumnKind.Match:
                    parsed++;
                    same++;
                    parsedOccurrences += occurrences?.OccurrencesByWord.GetValueOrDefault(form) ?? 0;
                    break;
                case CompareColumnKind.NoMatch:
                    parsed++;
                    different++;
                    parsedOccurrences += occurrences?.OccurrencesByWord.GetValueOrDefault(form) ?? 0;
                    break;
                case CompareColumnKind.NoParse:
                    if (comparison.MeaningCode == "refused") skipped++;
                    else noParse++;
                    break;
                case CompareColumnKind.Timeout:
                    unknown++;
                    break;
            }
            var meaning = CompareSemantics.MeaningOfCode(comparison.MeaningCode);
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
                        if (comparison.MeaningCode == "refused") approvedSkipped++;
                        else approvedNoParse++;
                        break;
                    case CompareColumnKind.Timeout:
                        approvedUnknown++;
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
                SameWords = same,
                DifferentWords = different,
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

internal static class OverviewLookFirstBuilder
{
    public static OverviewLookFirst Build(IReadOnlyList<AssessmentWordResult> words,
        IReadOnlyList<AssessedWord> measuredWords, GrammarCheckResponse? grammarCheck,
        bool associationsAvailable = true)
    {
        var lost = words.Where(word => Place(word) is
            { Standing: ProjectStanding.Approved, Column: CompareColumnKind.NoParse }).ToArray();
        var stopped = words.Where(word => word.Outcome == "capped" || word.Morphology?.Capped == true).ToArray();
        var stoppedNames = stopped.Select(word => word.Word).ToHashSet(StringComparer.Ordinal);
        var stoppedTimes = measuredWords.Where(word => stoppedNames.Contains(word.Word))
            .Select(TimingAggregation.WordTimeMs).Where(time => time is not null).Select(time => time!.Value).ToArray();
        var namedIds = (grammarCheck?.Findings ?? []).SelectMany(finding => finding.Subject)
            .Where(part => part.Reach is not null).SelectMany(part => part.Reach!.AllomorphIds
                .Concat(part.Reach.GrammaticalInfoIds)).Select(IdKey).ToHashSet(StringComparer.Ordinal);
        var sharedGroups = associationsAvailable
            ? ObjectUsesQuery.SharedBy(words, lost.Select(word => word.Word).ToArray()) : [];
        var distinctiveSharedGroups = sharedGroups.Where(item => item.Words.Count < lost.Length).ToArray();
        var shared = (distinctiveSharedGroups.Length > 0 ? distinctiveSharedGroups : sharedGroups)
            .Take(2)
            .Select(item => new OverviewSharedMorpheme(item.Morpheme.Form, item.Words.Count,
                (item.Morpheme.AllomorphId is { } allomorph && namedIds.Contains(IdKey(allomorph))) ||
                (item.Morpheme.GrammaticalInfoId is { } info && namedIds.Contains(IdKey(info)))))
            .ToArray();
        var candidateDifferent = words.Count(word => Place(word) is
            { Standing: ProjectStanding.Candidate, Column: CompareColumnKind.NoMatch });
        var sameTextDifferentEntry = words.Where(CompareSemantics.HasSameTextDifferentEntry)
            .Select(word => word.Word).ToArray();
        var identityComparisonAvailable = !words.Any(word => Place(word) is
            { Standing: ProjectStanding.Approved, Column: CompareColumnKind.NoMatch }) || associationsAvailable;
        return new OverviewLookFirst(lost.Select(word => word.Word).ToArray(), shared,
            stopped.Select(word => word.Word).ToArray(), stoppedTimes.Length == stopped.Length && stopped.Length > 0
                ? stoppedTimes.Sum() : null,
            candidateDifferent)
        {
            SharedLostMorphemesAvailable = associationsAvailable,
            ApprovedSameTextDifferentEntryWords = sameTextDifferentEntry,
            ApprovedSameTextDifferentEntryAvailable = identityComparisonAvailable,
        };
    }

    private static ComparePlacement Place(AssessmentWordResult word) =>
        CompareSemantics.PlacementOf(CompareSemantics.Compare(word));

    private static string IdKey(string id) => Guid.TryParse(id, out var guid) ? guid.ToString("D") : id;
}
