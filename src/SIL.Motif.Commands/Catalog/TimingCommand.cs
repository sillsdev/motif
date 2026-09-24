using System.Text.Json;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Catalog;

/// <summary>Reads stored Assessment timings for all or a selected set of words.</summary>
public static class TimingCommand
{
    /// <summary>Returns per-word timing and the stored PanGloss object timings for one Assessment.</summary>
    public static CommandOutcome<TimingResponse> Timing(TimingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Top <= 0 || request.By is not ("kind" or "rule") ||
            string.IsNullOrWhiteSpace(request.WordSet))
            return CommandOutcome<TimingResponse>.Refused(new Refusal(
                "timing.invalid-request", FailureReason.InvalidArgument,
                "Timing requires --by kind|rule and a positive --top value."));

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var assessments = new AssessmentRepository(database);
            AssessmentRecord? assessment = null;
            if (request.AssessmentId is not null)
            {
                try { assessment = assessments.Get(request.AssessmentId); }
                catch (KeyNotFoundException) { }
                if (assessment is not null && !assessment.Kind.IsStoredKind(AssessmentKind.ParseTime))
                    return RefusedTiming("timing.wrong-kind", "The named Assessment did not collect parse-time results.");
            }
            else
            {
                var workspaceKey = ProjectWorkspaceKey.Compute(project);
                var baseline = new BaselineRepository(database).GetCurrent(workspaceKey);
                var saved = new NamedSelectionRepository(database).GetDefault();
                if (baseline is not null && saved is not null)
                {
                    using var cache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
                    var composed = SelectionComposer.Compose(cache,
                        new SelectionRequest(false, saved.TextIds, saved.AddedWords, false, null), assessments,
                        JsonSerializer.Serialize(baseline.Token, SIL.Motif.Contract.MotifJson.CreateOptions()));
                    if (composed.Succeeded)
                        assessment = OverviewCommand.FindMatchingAssessment(
                            assessments, baseline.Token, composed.Value!.Selection, saved.Name);
                }
            }

            if (assessment is null || !assessment.Kind.IsStoredKind(AssessmentKind.ParseTime))
                return RefusedTiming("timing.no-assessment", "No matching stored ParseTime Assessment is available.");

            var words = assessment.Words ?? Array.Empty<AssessedWord>();
            var selected = ResolveWords(request, words, database, project, assessments);
            if (!selected.Succeeded) return CommandOutcome<TimingResponse>.Refused(selected.Refusal!);
            var selectedWords = selected.Value!;
            var selectedNames = selectedWords.Select(word => word.Word).ToHashSet(StringComparer.Ordinal);
            var objectRows = assessment.ObjectTimings.Where(row => selectedNames.Contains(row.Word)).ToArray();
            var aggregates = TimingAggregation.Aggregate(objectRows, request.By, request.Rule, request.Top);
            var summary = TimingAggregation.SummarizeWords(selectedWords, request.Top);
            return CommandOutcome<TimingResponse>.Success(new TimingResponse(
                assessment.AssessmentId, request.WordSet, request.By, selectedWords.Count,
                summary.MedianMs, summary.Percentile95Ms, summary.SlowestWords,
                aggregates.Aggregates, aggregates.CostliestWords));
        });
    }

    private static CommandOutcome<IReadOnlyList<AssessedWord>> ResolveWords(
        TimingRequest request,
        IReadOnlyList<AssessedWord> allWords,
        MotifDatabase database,
        ProjectLocator project,
        AssessmentRepository assessments)
    {
        if (request.ExplicitWords is { Count: > 0 } explicitWords)
        {
            var explicitSet = NormalizeWords(explicitWords);
            return Success(allWords.Where(word => explicitSet.Contains(word.Word)).ToArray());
        }

        var name = request.WordSet.Trim();
        if (StringComparer.Ordinal.Equals(name, "all")) return Success(allWords);
        if (name is "step-limit" or "steps")
            return Success(allWords.Where(IsStepLimited).ToArray());
        if (name is "slowest")
            return Success(allWords.Where(word => word.ElapsedMs is not null)
                .OrderByDescending(word => word.ElapsedMs).ThenBy(word => word.Word, StringComparer.Ordinal)
                .Take(request.Top).ToArray());
        if (name.StartsWith("cell:", StringComparison.Ordinal))
        {
            var cell = name[5..].Split(':', 2);
            if (cell.Length != 2 || !TryColumn(cell[1], out var column))
                return RefusedWords("timing.invalid-word-set", "A matrix cell must use cell:<standing>:<column>.");
            var standing = NormalizeStanding(cell[0]);
            if (standing is null) return RefusedWords("timing.invalid-word-set", "The matrix cell has an unknown standing.");
            return Success(allWords.Where(word =>
            {
                var placement = Place(word);
                return placement.Column == column && StringComparer.Ordinal.Equals(placement.Standing, standing);
            }).ToArray());
        }

        var repository = new NamedSelectionRepository(database);
        var saved = repository.Get(name);
        if (saved is null)
            return RefusedWords("timing.word-set-not-found", $"No saved Selection named '{name}' exists.");
        var current = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
        if (current is null)
            return RefusedWords("timing.no-baseline", "A Baseline is required to resolve a saved Selection.");
        using var cache = new FwDataProjectLoader().LoadScratchCache(current.FwDataPath);
        var composed = SelectionComposer.Compose(cache,
            new SelectionRequest(false, saved.TextIds, saved.AddedWords, false, null), assessments,
            JsonSerializer.Serialize(current.Token, SIL.Motif.Contract.MotifJson.CreateOptions()));
        if (!composed.Succeeded) return CommandOutcome<IReadOnlyList<AssessedWord>>.Refused(composed.Refusal!);
        var selectedNames = composed.Value!.Selection.Words.ToHashSet(StringComparer.Ordinal);
        return Success(allWords.Where(word => selectedNames.Contains(word.Word)).ToArray());
    }

    private static ComparePlacement Place(AssessedWord word) => CompareSemantics.Place(new CompareWordFacts(
        word.ProjectStanding, word.Outcome,
        word.IsIncomplete || word.Morphology is { Capped: true } or { TimedOut: true },
        word.ReadingGrades, word.MissedApprovedCount ?? 0));

    private static bool IsStepLimited(AssessedWord word) =>
        word.Outcome == "capped" || word.Morphology?.Capped == true;

    private static HashSet<string> NormalizeWords(IEnumerable<string> words) => words
        .Where(word => !string.IsNullOrWhiteSpace(word))
        .Select(word => word.Trim().Normalize(System.Text.NormalizationForm.FormD))
        .ToHashSet(StringComparer.Ordinal);

    private static string? NormalizeStanding(string value) => value switch
    {
        "approved" or SIL.Motif.Contract.Responses.ProjectStanding.Approved => SIL.Motif.Contract.Responses.ProjectStanding.Approved,
        "candidate" or SIL.Motif.Contract.Responses.ProjectStanding.Candidate => SIL.Motif.Contract.Responses.ProjectStanding.Candidate,
        "rejected" or SIL.Motif.Contract.Responses.ProjectStanding.Rejected => SIL.Motif.Contract.Responses.ProjectStanding.Rejected,
        "incorrect-spelling" or SIL.Motif.Contract.Responses.ProjectStanding.IncorrectSpelling => SIL.Motif.Contract.Responses.ProjectStanding.IncorrectSpelling,
        "not-present" or SIL.Motif.Contract.Responses.ProjectStanding.NotPresent => SIL.Motif.Contract.Responses.ProjectStanding.NotPresent,
        _ => null,
    };

    private static bool TryColumn(string value, out CompareColumnKind column)
    {
        column = value switch
        {
            "match" => CompareColumnKind.Match,
            "no-match" => CompareColumnKind.NoMatch,
            "no-parse" => CompareColumnKind.NoParse,
            "unknown" or "timeout" => CompareColumnKind.Timeout,
            "skipped" => CompareColumnKind.Skipped,
            _ => (CompareColumnKind)(-1),
        };
        return Enum.IsDefined(column);
    }

    private static CommandOutcome<IReadOnlyList<AssessedWord>> Success(IReadOnlyList<AssessedWord> words) =>
        CommandOutcome<IReadOnlyList<AssessedWord>>.Success(words);

    private static CommandOutcome<IReadOnlyList<AssessedWord>> RefusedWords(string code, string message) =>
        CommandOutcome<IReadOnlyList<AssessedWord>>.Refused(new Refusal(code, FailureReason.NotFound, message));

    private static CommandOutcome<TimingResponse> RefusedTiming(string code, string message) =>
        CommandOutcome<TimingResponse>.Refused(new Refusal(code, FailureReason.NotFound, message));
}
