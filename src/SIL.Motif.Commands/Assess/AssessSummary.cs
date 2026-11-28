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
    /// <summary>
    /// The scope line of an Assessment that scored only a caller's word list, such as <c>scored 500 listed words</c>;
    /// null when the run also read Texts, all wordforms or a retry, whose scope is the Selection's own.
    /// </summary>
    public static string? Scope(AssessCommandResponse response) =>
        response.SelectionDescriptor is { PastedWords.Count: > 0, AllWordforms: false, RetryFailed: false, RetryWorkOver: null } descriptor &&
        descriptor.TextIds.Count == 0
            ? $"scored {response.Selection.Words.Count} listed words" : null;

    /// <summary>Projects a complete Assessment response without publishing unpaired parser costs.</summary>
    public static JsonObject Full(AssessCommandResponse response)
    {
        var result = ProjectionJson.ToNode(new
        {
            response.Baseline, response.Selection, response.AssessmentIds, response.SummaryMarkdown,
            response.InvocationId, response.SelectionDescriptor, response.Counts, response.InputWords, response.LowercaseWords,
            response.Measurements,
            response.TimingOverrideAssessmentIds, response.CompletionSummary, response.CorrectnessStatus,
            response.GrammarWarnings, response.ObjectTimingNote,
        })!.AsObject();
        result["words"] = DetailedWords(response.Words);
        result["costComparison"] = new JsonObject { ["state"] = "unavailable", ["reason"] = "No paired Assessment was selected." };
        if (Scope(response) is { } scope) result["scope"] = scope;
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
            trial.Sample, trial.SampleWords, trial.WordListDigest,
        })!.AsObject();
        result["wordResults"] = DetailedWords(trial.WordResults);
        return result;
    }

    /// <summary>The page of a just-finished run.</summary>
    public static JsonObject Page(AssessCommandResponse response, int offset, int limit, bool detailed)
    {
        var measured = response.Measurements.FirstOrDefault(m => m.Kind == "ParseTime")?.AssessmentId
            ?? response.AssessmentIds.FirstOrDefault();
        var page = Build(measured, response.Words, response.InvocationId, offset, limit, detailed);
        if (Scope(response) is { } scope) page["scope"] = scope;
        return page;
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
            ["inputWords"] = words.Count(row => row.LowercaseOf is null),
            ["lowercaseWords"] = words.Count(row => row.LowercaseOf is not null),
            ["nextCursor"] = SIL.Motif.Contract.Responses.ResultPaging.NextCursor(offset, page.Length, words.Count),
            ["costComparison"] = new JsonObject { ["state"] = "unavailable", ["reason"] = "No paired Assessment was selected." },
        };
        if (!string.IsNullOrEmpty(invocationId)) result["invocation"] = invocationId;
        var shares = WorkShares(words);
        result["words"] = new JsonArray(page.Select((row, index) =>
            detailed ? Detailed(row, shares.After[offset + index]) : Concise(row, shares.After[offset + index])).ToArray());
        return result;
    }

    /// <summary>The detailed rows, each with its percentage of the run's parser work.</summary>
    private static JsonArray DetailedWords(IReadOnlyList<AssessmentWordResult> words)
    {
        var shares = WorkShares(words);
        return new JsonArray(words.Select((row, index) =>
        {
            var node = Detailed(row, shares.After[index]);
            if (shares.Before[index] is { } before) node["baselineWorkSharePercent"] = before;
            if (shares.After[index] is { } after && shares.Before[index] is { } was)
                node["workSharePointsDelta"] = Math.Round(after - was, ShareDigits);
            return node;
        }).ToArray());
    }

    private const int ShareDigits = 2;

    /// <summary>Each word's percent of the run's total work, per side.</summary>
    private static (double?[] After, double?[] Before) WorkShares(IReadOnlyList<AssessmentWordResult> words) =>
        (Shares(words, word => word.ParserWork), Shares(words, word => word.BaselineWorkUnits));

    private static double?[] Shares(IReadOnlyList<AssessmentWordResult> words, Func<AssessmentWordResult, long?> work)
    {
        var total = words.Sum(word => (decimal)(work(word) ?? 0));
        return total <= 0 || words.Any(word => work(word) is null) ? new double?[words.Count]
            : words.Select(word => work(word) is { } own ? (double?)Math.Round((double)(own * 100m / total), ShareDigits) : null).ToArray();
    }

    private static JsonNode Concise(AssessmentWordResult row, double? workShare)
    {
        var node = new JsonObject { ["word"] = row.Word, ["outcome"] = row.Outcome };
        if (row.LowercaseOf is { } lowercaseOf) node["lowercaseOf"] = lowercaseOf;
        if (row.StoppedBy is { } stoppedBy) node["stoppedBy"] = stoppedBy;
        if (workShare is { } share) node["workSharePercent"] = share;
        if (row.Outcome == "analysed")
        {
            node["reading"] = AssessmentCounts.IsAccepted(row) ? "accepted" : "guess-only";
            node["readings"] = row.Morphology?.Analyses.Count ?? 0;
        }
        return node;
    }

    private static JsonObject Detailed(AssessmentWordResult row, double? workShare = null) => ProjectionJson.ToNode(new
    {
        row.Word, row.LowercaseOf, row.WordWritingSystem, row.Outcome, row.IsIncomplete, row.CompletionStatus,
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
        WorkSharePercent = workShare,
    })!.AsObject();
}
