using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Catalog;

/// <summary>Reads one Trial and compares its correctness over words shared with the earlier Assessment.</summary>
public static class ReviewNumbersCommand
{
    /// <summary>The Assessment identities and expected number of touched words for one Review Trial.</summary>
    public sealed record Request(string ProjectPath, string? BeforeAssessmentId, string TrialAssessmentId,
        int TouchedWordCount);

    /// <summary>The command's comparison and whether every touched word finished with correctness evidence.</summary>
    public sealed record Response(string Text, bool EvidenceComplete);

    /// <summary>Reads both recorded Assessments and returns one comparison.</summary>
    public static CommandOutcome<Response> Read(Request request) =>
        ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            var repository = new AssessmentRepository(database);
            AssessmentRecord trial;
            AssessmentRecord? before = null;
            try
            {
                trial = repository.Get(request.TrialAssessmentId);
                if (request.BeforeAssessmentId is { } id) before = repository.Get(id);
            }
            catch (KeyNotFoundException exception)
            {
                return CommandOutcome<Response>.Refused(new Refusal("review.assessment-not-found",
                    FailureReason.NotFound, exception.Message));
            }
            if (trial.Kind != RegressionChecker.RequiredKind ||
                before is not null && before.Kind != RegressionChecker.RequiredKind)
                return CommandOutcome<Response>.Refused(new Refusal("review.wrong-assessment-kind",
                    FailureReason.InvalidArgument, "Review numbers require Correctness Assessments."));
            try
            {
                return CommandOutcome<Response>.Success(Summarize(before, trial, request.TouchedWordCount));
            }
            catch (Exception exception) when (exception is ReportRefusalException or ComparisonRefusalException)
            {
                return CommandOutcome<Response>.Refused(new Refusal("review.numbers-unavailable",
                    FailureReason.Refused, exception.Message));
            }
        });

    /// <summary>Summarizes both Assessments over their shared words, using the regression comparison.</summary>
    public static Response Summarize(AssessmentRecord? before, AssessmentRecord trial, int touchedWordCount)
    {
        ArgumentNullException.ThrowIfNull(trial);
        var trialWords = trial.Words ?? [];
        var complete = trialWords.Count == touchedWordCount && trialWords.All(word =>
            word.Correctness is not null && word.Morphology is { Capped: false, TimedOut: false,
                InvalidShape: false });
        if (before is null)
        {
            var covered = trialWords.Count(word => word.Correctness?.Status == "covered");
            return new Response($"{covered} of {trialWords.Count} touched words kept their approved analyses. " +
                "There is no earlier Assessment to compare.", complete);
        }
        var finding = RegressionChecker.Check(before.ToCorrectness(), trial.ToCorrectness());
        if (finding is null)
            return new Response("These Assessments were made by different Assessors and cannot be compared.",
                complete);
        if (!finding.CanCompare)
            return new Response("The regression check could not compare these Assessments because they share no words.",
                complete);
        var shared = before.Selection.Words.Intersect(trial.Selection.Words, StringComparer.Ordinal).Count();
        var noun = shared == 1 ? "word" : "words";
        return new Response($"Among {shared} shared {noun}, approved kept: " +
            $"{finding.PreviousCoverage.Analysed} → {finding.CandidateCoverage.Analysed}.", complete);
    }
}
