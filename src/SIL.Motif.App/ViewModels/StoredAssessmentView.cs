using System.Globalization;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.App.ViewModels;

internal static class StoredAssessmentView
{
    internal static WorkspaceEvidence? From(CurrentEvidenceSnapshot snapshot)
    {
        if (snapshot.Baseline is not { } baseline || snapshot.MatchingAssessment is not { } assessment)
            return null;
        var records = snapshot.EffectiveWords;
        var words = records.Select(ToWord).ToArray();
        var completed = words.Count(word => !word.IsIncomplete && word.Outcome != "skipped");
        var incomplete = words.Count(word => word.IsIncomplete);
        var skipped = words.Count(word => word.Outcome == "skipped");
        var summary = $"{completed} searches completed; {incomplete} incomplete; {skipped} skipped.";
        var invocationId = assessment.Invocation?.InvocationId ?? string.Empty;
        var response = new AssessCommandResponse(
            new BaselineCaptureResponse(baseline.Token, baseline.FwDataPath, baseline.SourceLastWriteUtc,
                false, true),
            new SelectionProjection(assessment.Selection.Words, []), [assessment.AssessmentId], summary)
        {
            InvocationId = invocationId,
            Words = words,
            CompletionSummary = summary,
            Measurements = snapshot.MatchingCorrectnessAssessmentId is { } correctnessId
                ? [new ProducedAssessmentReference(assessment.AssessmentId, "ParseTime", invocationId),
                    new ProducedAssessmentReference(correctnessId, "Correctness", invocationId)]
                : [new ProducedAssessmentReference(assessment.AssessmentId, "ParseTime", invocationId)],
            TimingOverrideAssessmentIds = snapshot.RerunAssessments.Select(item => item.AssessmentId).ToArray(),
        };
        var saved = DateTimeOffset.Parse(assessment.SavedUtc, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        return new WorkspaceEvidence(response, saved, WasRerun: false);
    }

    private static AssessmentWordResult ToWord(AssessedWord word)
    {
        var incomplete = word.IsIncomplete || word.Outcome is "timed-out" or "capped" ||
            word.Morphology is { Capped: true } or { TimedOut: true };
        var missed = word.Correctness?.Unmatched.Select(index => word.Correctness.Expectations[index])
            .Select(expected => new ParserReading(expected.Morphs.Select(morph =>
                new ParserReadingMorph(morph.Forms.FirstOrDefault() ?? "?", "", "", null, false, null))
                .ToArray())).ToArray();
        var status = word.Morphology switch
        {
            { Capped: true, TimedOut: true } => "INCOMPLETE — parsing did not finish (step and time limits)",
            { Capped: true } => "INCOMPLETE — parsing did not finish (step limit)",
            { TimedOut: true } => "INCOMPLETE — parsing did not finish (time limit)",
            _ when word.Outcome == "timed-out" => "INCOMPLETE — parsing did not finish (time limit)",
            _ when word.Outcome == "capped" => "INCOMPLETE — parsing did not finish (step limit)",
            _ when word.Outcome == "skipped" => "Not attempted",
            _ => "Search completed",
        };
        return new AssessmentWordResult(word.Word, word.Outcome, incomplete, status,
            word.ElapsedMs, word.RawSignature)
        {
            Morphology = word.Morphology,
            Correctness = word.Correctness,
            ReadingGrades = word.ReadingGrades,
            ProjectStanding = word.ProjectStanding,
            MissedApproved = missed,
            OccurrenceCount = word.OccurrenceCount,
            FixFirst = CompareSemantics.FixFirst(new CompareWordFacts(word.ProjectStanding,
                word.Outcome, incomplete, word.Morphology, word.ReadingGrades,
                word.MissedApprovedCount ?? 0), missed),
        };
    }
}
