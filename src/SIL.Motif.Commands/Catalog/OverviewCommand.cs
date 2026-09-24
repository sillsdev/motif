using System.Globalization;
using Microsoft.Data.Sqlite;
using SIL.LCModel;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Catalog;

/// <summary>Reads a typed project Overview from the current Baseline and stored Assessment.</summary>
public static class OverviewCommand
{
    /// <summary>Returns the latest Assessment matching the exact current Baseline and resolved default Selection.</summary>
    public static CommandOutcome<OverviewResponse> Overview(OverviewRequest request) =>
        ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var opened = ReadOpenedUtc(database);
            DateTimeOffset? lastSave = File.Exists(project.FullFwDataPath)
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(project.FullFwDataPath), TimeSpan.Zero) : null;
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var baseline = new BaselineRepository(database).GetCurrent(workspaceKey);
            if (baseline is not null) lastSave = baseline.SourceLastWriteUtc;
            var savedSelection = new NamedSelectionRepository(database).GetDefault();
            Selection? selection = null;
            TextOccurrenceSnapshot? occurrenceSnapshot = null;
            AssessmentRecord? assessment = null;
            var wordformCount = 0;
            var lexemeCount = 0;
            var ruleCount = 0;
            if (baseline is not null)
            {
                using var cache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
                wordformCount = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().Count;
                var entries = cache.ServiceLocator.GetInstance<ILexEntryRepository>();
                lexemeCount = entries.Count;
                var affixRules = entries.AllInstances()
                    .SelectMany(entry => entry.AlternateFormsOS.OfType<IMoAffixAllomorph>()
                        .Concat(entry.LexemeFormOA is IMoAffixAllomorph allomorph ? [allomorph] : []))
                    .Select(form => form.Guid).Distinct().Count();
                ruleCount = affixRules + cache.LangProject.MorphologicalDataOA.CompoundRulesOS.Count +
                    cache.LangProject.PhonologicalDataOA.PhonRulesOS.Count;
                if (savedSelection is not null)
                {
                    var requestSelection = new SelectionRequest(false, savedSelection.TextIds,
                        savedSelection.AddedWords, false, null);
                    var composed = SelectionComposer.Compose(cache, requestSelection, new AssessmentRepository(database),
                        System.Text.Json.JsonSerializer.Serialize(baseline.Token, SIL.Motif.Contract.MotifJson.CreateOptions()));
                    if (!composed.Succeeded)
                        return CommandOutcome<OverviewResponse>.Refused(composed.Refusal!);
                    selection = composed.Value!.Selection with { Name = savedSelection.Name };
                    occurrenceSnapshot = TextOccurrenceReader.Read(cache, savedSelection.TextIds);
                    assessment = FindMatchingAssessment(new AssessmentRepository(database), baseline.Token,
                        selection, savedSelection.Name);
                }
            }

            var occurrenceCounts = selection is null || occurrenceSnapshot is null
                ? 0
                : selection.Words.Sum(word => occurrenceSnapshot.OccurrencesByWord.GetValueOrDefault(word));
            var words = selection?.Words ?? savedSelection?.AddedWords ?? Array.Empty<string>();
            var assessedWords = assessment?.Words ?? Array.Empty<AssessedWord>();
            var metrics = OverviewMetrics.Build(words, occurrenceSnapshot, assessedWords);
            var elapsedMs = assessment?.Words?.Where(word => word.ElapsedMs is not null).Sum(word => word.ElapsedMs!.Value);
            return CommandOutcome<OverviewResponse>.Success(new OverviewResponse(
                Path.GetFileNameWithoutExtension(project.FullFwDataPath), opened, lastSave,
                words.Count, savedSelection?.TextIds.Count ?? 0, savedSelection?.AddedWords.Count ?? 0,
                occurrenceCounts, wordformCount, ruleCount, lexemeCount,
                assessment?.AssessmentId, assessment is null ? null : ParseUtc(assessment.SavedUtc),
                elapsedMs is null ? null : elapsedMs.Value / 1000d,
                assessment?.GrammarSourceSha256, selection?.Sha256,
                metrics.TextCoverage, metrics.Accuracy,
                assessment is null ? TimingAggregation.SummarizeWords(Array.Empty<AssessedWord>())
                    : TimingAggregation.SummarizeWords(assessedWords),
                Warnings: null));
        });

    internal static AssessmentRecord? FindMatchingAssessment(
        AssessmentRepository repository, object baselineToken, Selection selection, string selectionName)
    {
        var baselineJson = System.Text.Json.JsonSerializer.Serialize(
            baselineToken, SIL.Motif.Contract.MotifJson.CreateOptions());
        return repository.ListBaselineAssessments(AssessmentKind.ParseTime.ToStoredKind())
            .Where(record => StringComparer.Ordinal.Equals(record.BaselineToken, baselineJson) &&
                StringComparer.Ordinal.Equals(record.Selection.Sha256, selection.Sha256) &&
                record.Selection.Words.SequenceEqual(selection.Words, StringComparer.Ordinal) &&
                (StringComparer.Ordinal.Equals(record.Selection.Name, selectionName) ||
                 StringComparer.Ordinal.Equals(record.Selection.Name, selection.Name)))
            .OrderBy(record => record.SavedUtc, StringComparer.Ordinal)
            .LastOrDefault();
    }

    private static DateTimeOffset ReadOpenedUtc(MotifDatabase database)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CreatedUtc FROM MotifMetadata WHERE Id = 1;";
        return ParseUtc((string)command.ExecuteScalar()!);
    }

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
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
        var rejectedRebuilt = 0;
        var rejected = 0;
        var candidatesConfirmed = 0;
        var candidates = 0;

        foreach (var form in selectionWords)
        {
            if (!byWord.TryGetValue(form, out var word))
            {
                if (assessedWords.Count > 0) unknown++;
                continue;
            }
            var placement = CompareSemantics.Place(new CompareWordFacts(
                word.ProjectStanding, word.Outcome,
                word.IsIncomplete || word.Morphology is { Capped: true } or { TimedOut: true },
                word.ReadingGrades, word.MissedApprovedCount ?? 0));
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
                if (placement.Column == CompareColumnKind.Match) approvedKept++;
            }
            if (meaning.Family == CompareFamilyKind.Violation) violations++;
            if (meaning.Family == CompareFamilyKind.Unknown) accuracyUnknown++;
            if (StringComparer.Ordinal.Equals(placement.Standing, SIL.Motif.Contract.Responses.ProjectStanding.Rejected))
            {
                rejected++;
                if (placement.Column == CompareColumnKind.Match) rejectedRebuilt++;
            }
            if (StringComparer.Ordinal.Equals(placement.Standing, SIL.Motif.Contract.Responses.ProjectStanding.Candidate))
            {
                candidates++;
                if (placement.Column == CompareColumnKind.Match) candidatesConfirmed++;
            }
        }

        var totalOccurrences = occurrences is null ? 0 : selectionWords.Sum(form => occurrences.OccurrencesByWord.GetValueOrDefault(form));
        return (
            new OverviewTextCoverage(parsed, noParse, unknown, skipped, totalOccurrences, parsedOccurrences),
            new OverviewAccuracy(approvedKept, approved, violations, accuracyUnknown,
                rejectedRebuilt, rejected, candidatesConfirmed, candidates));
    }
}
