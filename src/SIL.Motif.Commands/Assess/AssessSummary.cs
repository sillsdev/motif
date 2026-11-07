using System.Text.Json.Nodes;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Commands.Assess;

/// <summary>
/// The small rendering of an Assessment: counts first, then one page of words. The agent tool and the CLI's
/// <c>--summary</c> both use it, so a large word list costs a page, not the whole list.
/// </summary>
public static class AssessSummary
{
    /// <summary>Projects a complete Assessment response without publishing unpaired parser costs.</summary>
    public static JsonObject Full(AssessCommandResponse response)
    {
        var result = ProjectionJson.ToNode(new
        {
            response.Baseline, response.Selection, response.AssessmentIds, response.SummaryMarkdown,
            response.InvocationId, response.SelectionDescriptor, response.Counts, response.Measurements,
            response.TimingOverrideAssessmentIds, response.CompletionSummary, response.CorrectnessStatus,
            response.GrammarWarnings,
        })!.AsObject();
        result["words"] = new JsonArray(response.Words.Select(Detailed).ToArray());
        result["costComparison"] = new JsonObject { ["state"] = "unavailable", ["reason"] = "No paired Assessment was selected." };
        return result;
    }

    /// <summary>Projects a Trial and its word evidence without unpaired parser costs.</summary>
    public static JsonObject Trial(TrialRecord trial)
    {
        var result = ProjectionJson.ToNode(new
        {
            trial.JobId, trial.ProposalId, trial.IntentDigest, trial.Baseline, trial.DryRun, trial.AssessmentIds,
            trial.Words, trial.AllWords, trial.Limits, trial.Status, trial.CompletedUtc, trial.Freshness,
            trial.Label, trial.GrammarCheckId, trial.Work, trial.GrammarCheckWarning, trial.DroppedGrammar,
            trial.Clean, trial.WordScope, trial.Standing, trial.DroppedGrammarNotChecked,
        })!.AsObject();
        result["wordResults"] = new JsonArray(trial.WordResults.Select(Detailed).ToArray());
        return result;
    }

    /// <summary>The page of a just-finished run.</summary>
    public static JsonObject Page(AssessCommandResponse response, int offset, int limit, bool detailed)
    {
        var measured = response.Measurements.FirstOrDefault(m => m.Kind == "ParseTime")?.AssessmentId
            ?? response.AssessmentIds.FirstOrDefault();
        return Build(measured, response.Words, response.InvocationId, offset, limit, detailed);
    }

    /// <summary>The page of a stored Assessment's words, read back by its id.</summary>
    public static JsonObject PageStored(string assessmentId, IReadOnlyList<AssessedWord> words, int offset,
        int limit, bool detailed) =>
        Build(assessmentId, words.Select(AssessmentWordRows.FromRecorded).ToArray(), null, offset, limit, detailed);

    /// <summary>Counts recorded words using the same outcome categories as an Assessment response.</summary>
    public static AssessmentCounts Counts(IReadOnlyList<AssessedWord> words) =>
        AssessmentCounts.Of(words.Select(AssessmentWordRows.FromRecorded));

    /// <summary>Projects a recorded word without publishing unpaired parser costs.</summary>
    public static JsonNode StoredWord(AssessedWord word)
    {
        var result = Detailed(AssessmentWordRows.FromRecorded(word)).AsObject();
        result["analyses"] = ProjectionJson.ToNode(word.Analyses);
        return result;
    }

    private static JsonObject Build(string? assessmentId, IReadOnlyList<AssessmentWordResult> words,
        string? invocationId, int offset, int limit, bool detailed)
    {
        offset = Math.Max(0, offset);
        limit = Math.Max(1, limit);
        var page = words.Skip(offset).Take(limit).ToArray();
        var end = offset + page.Length;
        var result = new JsonObject
        {
            ["counts"] = ProjectionJson.ToNode(AssessmentCounts.Of(words)),
            ["assessment"] = assessmentId,
            ["showing"] = page.Length == 0 ? $"showing 0 of {words.Count}" : $"showing {offset + 1}-{end} of {words.Count}",
            ["offset"] = offset,
            ["total"] = words.Count,
            ["nextOffset"] = end < words.Count ? end : null,
            ["costComparison"] = new JsonObject { ["state"] = "unavailable", ["reason"] = "No paired Assessment was selected." },
        };
        if (!string.IsNullOrEmpty(invocationId)) result["invocation"] = invocationId;
        result["words"] = new JsonArray(page.Select(row => detailed ? Detailed(row) : Concise(row)).ToArray());
        return result;
    }

    private static JsonNode Concise(AssessmentWordResult row)
    {
        var node = new JsonObject { ["word"] = row.Word, ["outcome"] = row.Outcome };
        if (row.StoppedBy is { } stoppedBy) node["stoppedBy"] = stoppedBy;
        if (row.Outcome == "analysed")
        {
            node["reading"] = AssessmentCounts.IsAccepted(row) ? "accepted" : "guess-only";
            node["readings"] = row.Morphology?.Analyses.Count ?? 0;
        }
        return node;
    }

    private static JsonNode Detailed(AssessmentWordResult row) => ProjectionJson.ToNode(new
    {
        row.Word, row.WordWritingSystem, row.Outcome, row.IsIncomplete, row.CompletionStatus,
        row.Comparison, row.AnalysisComparison, row.StoredAnalysesAvailable,
        Morphology = row.Morphology is { } evidence ? new
        {
            evidence.Schema, evidence.Index, evidence.Word, evidence.Capped, evidence.TimedOut,
            evidence.InvalidShape, evidence.Analyses, evidence.Unavailable, evidence.Refusal,
        } : null,
        row.Readings, row.StoredAnalyses, row.TryWordLink, row.HasNoWordform, row.Correctness,
        row.ReadingGrades, row.ProjectStanding, row.MissedApproved, row.ExpectedAnalysis,
        row.StoppedBy, row.Origin, row.OccurrenceCount, row.FixFirst, row.HasUnavailableEvidence,
        row.EvidenceStatus, row.CorrectnessStatus,
    })!;
}
