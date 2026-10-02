using System;
using System.Collections.Generic;
using System.Linq;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Commands.Assess;

/// <summary>
/// Builds an Assessment's word rows the one way a run and a reopened project both show them: whether each search
/// finished, the sentence that says so, the run's completion summary, and each word's Fix these first priority.
/// <see cref="AssessCommand"/> builds a run's rows here, and <see cref="CurrentEvidenceSnapshot.Assessment"/> builds
/// the stored run's rows here too, so reopening a project never words a word differently from the run that
/// measured it.
/// </summary>
internal static class AssessmentWordRows
{
    private const string StepLimited = "INCOMPLETE — parsing did not finish (step limit)";
    private const string TimeLimited = "INCOMPLETE — parsing did not finish (time limit)";

    /// <summary>Whether a word's search stopped at a limit, including when it returned readings first.</summary>
    /// <param name="outcome">The word's stored outcome, such as <c>capped</c> or <c>analysed</c>.</param>
    /// <param name="morphology">The parser's evidence for the word, when it returned any.</param>
    internal static bool IsIncomplete(string outcome, ParseWordEvidence? morphology) =>
        outcome is "capped" or "timed-out" || morphology is { Capped: true } or { TimedOut: true };

    /// <summary>The sentence a person reads for how one word's search ended.</summary>
    /// <param name="outcome">The word's stored outcome, such as <c>capped</c> or <c>analysed</c>.</param>
    /// <param name="morphology">The parser's evidence for the word, when it returned any.</param>
    internal static string CompletionStatus(string outcome, ParseWordEvidence? morphology) => outcome switch
    {
        _ when morphology is { Capped: true, TimedOut: true } =>
            "INCOMPLETE — parsing did not finish (step and time limits)",
        "capped" => StepLimited,
        "timed-out" => TimeLimited,
        _ when morphology is { Capped: true } => StepLimited,
        _ when morphology is { TimedOut: true } => TimeLimited,
        "skipped" => "Not attempted",
        _ => "Search completed",
    };

    /// <summary>One word's row from what the parser said of it, before the project's analyses are read.</summary>
    internal static AssessmentWordResult Row(string word, string outcome, int? elapsedMs, string? signature,
        ParseWordEvidence? morphology, WordCorrectness? correctness) =>
        new(word, outcome, IsIncomplete(outcome, morphology), CompletionStatus(outcome, morphology), elapsedMs,
            signature)
        {
            Morphology = morphology,
            Correctness = correctness,
        };

    /// <summary>
    /// The row's Fix these first priority, from its standing, its readings' grades and the approved readings the
    /// parser missed; set those on the row before asking.
    /// </summary>
    internal static FixFirstPriority? FixFirst(AssessmentWordResult row) =>
        CompareSemantics.FixFirst(new CompareWordFacts(row.ProjectStanding, row.Outcome, row.IsIncomplete,
            row.Morphology, row.ReadingGrades, row.MissedApproved?.Count ?? 0), row.MissedApproved);

    /// <summary>How many searches completed, stopped at a limit, or were not attempted, in one sentence.</summary>
    internal static string CompletionSummary(IReadOnlyCollection<AssessmentWordResult> words) =>
        CompletionSummary(
            words.Count(word => !word.IsIncomplete && word.Outcome != "skipped"),
            words.Count(word => word.IsIncomplete), words.Count(word => word.Outcome == "skipped"));

    /// <summary>The same sentence from counts already made, as the project history lists each Assessment.</summary>
    internal static string CompletionSummary(int completedCount, int incompleteCount, int skippedCount)
    {
        var searchNoun = completedCount == 1 ? "search" : "searches";
        return $"{completedCount} {searchNoun} completed; {incompleteCount} incomplete; {skippedCount} skipped.";
    }

    /// <summary>One recorded word's row, with the missed approved readings named as the run named them.</summary>
    internal static AssessmentWordResult FromRecorded(AssessedWord word)
    {
        var row = Row(word.Word, word.Outcome, word.ElapsedMs, word.RawSignature, word.Morphology, word.Correctness);
        row = row with
        {
            IsIncomplete = word.IsIncomplete || row.IsIncomplete,
            ReadingGrades = word.ReadingGrades,
            ProjectStanding = word.ProjectStanding,
            OccurrenceCount = word.OccurrenceCount,
            MissedApproved = word.MissedApproved,
            Attempts = word.Attempts,
            Passes = word.Passes,
            Origin = word.Origin,
        };
        return row with { FixFirst = FixFirst(row) };
    }

    /// <summary>
    /// The snapshot's matching Assessment as an <c>assess</c> run returns it, or <see langword="null"/> when the
    /// snapshot has no Baseline or no matching Assessment.
    /// </summary>
    internal static AssessCommandResponse? FromStored(CurrentEvidenceSnapshot snapshot)
    {
        if (snapshot.Baseline is not { } baseline || snapshot.MatchingAssessment is not { } assessment) return null;
        var words = snapshot.EffectiveWords.Select(word =>
        {
            var row = FromRecorded(word);
            if (row.MissedApproved is { } missed)
                row = row with { MissedApproved = missed.Select(reading => snapshot.Navigation is { } navigation
                    ? navigation.Verify(reading) : reading with
                    {
                        Morphs = reading.Morphs.Select(morph => morph with { FieldWorksLink = null }).ToArray(),
                    }).ToArray() };
            row = snapshot.StoredAnalysesByWord.TryGetValue(word.Word, out var stored)
                ? row with { StoredAnalyses = stored, ExpectedAnalysis = ExpectedAnalysis(stored) } : row;
            row = snapshot.WordAnalysesLinksByWord.TryGetValue(word.Word, out var link)
                ? row with { TryWordLink = link } : row;
            return snapshot.ResolvedReadingsByWord.TryGetValue(word.Word, out var readings)
                ? row with { Readings = readings } : row;
        }).ToArray();
        var summary = CompletionSummary(words);
        var invocationId = assessment.Invocation?.InvocationId ?? string.Empty;
        var measurements = new List<ProducedAssessmentReference>
        {
            new(assessment.AssessmentId, AssessmentKinds.ParseTime, invocationId),
        };
        if (snapshot.MatchingObjectTimingAssessmentId is { } objectTimingId)
            measurements.Add(new(objectTimingId, AssessmentKinds.ObjectTiming, invocationId));
        if (snapshot.MatchingCorrectnessAssessmentId is { } correctnessId)
            measurements.Add(new(correctnessId, AssessmentKinds.Correctness, invocationId));
        return new AssessCommandResponse(
            new BaselineCaptureResponse(baseline.Token, baseline.FwDataPath, baseline.SourceLastWriteUtc,
                FieldWorksHeldProject: false, ReusedExistingBytes: true),
            new SelectionProjection(assessment.Selection.Words, []),
            measurements.Select(measurement => measurement.AssessmentId).ToArray(), summary)
        {
            InvocationId = invocationId,
            Words = words,
            CompletionSummary = summary,
            Measurements = measurements,
            TimingOverrideAssessmentIds = snapshot.RerunAssessments.Select(item => item.AssessmentId).ToArray(),
        };
    }

    /// <summary>The first approved analysis, else the sole analysis without an approved opinion.</summary>
    internal static ParserReading? ExpectedAnalysis(IReadOnlyList<ParserReading> analyses) =>
        analyses.FirstOrDefault(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Approved)
        ?? (analyses.Count == 1 ? analyses[0] : null);
}
