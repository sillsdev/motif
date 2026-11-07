using System.Text.Json.Nodes;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Catalog;

/// <summary>Public Timing and Overview views exclude machine time and unpaired parser counters.</summary>
public static class TimingPresentation
{
    /// <summary>Projects selected words and their paired parser-work changes.</summary>
    public static JsonObject Timing(TimingResponse response) => ProjectionJson.ToNode(new
    {
        response.AssessmentId, response.WordSet, response.By, response.WordCount, response.ObjectTimingDetail,
        response.ObjectTimingAggregatesAvailable, response.IsStale, response.EvidenceRelation, response.Baseline,
        response.SourceLastWriteUtc, response.CurrentProjectIsStale, response.CostComparison,
        Objects = response.Aggregates.Select(row => new { row.Key, row.Name, row.Kind, row.IdentityQuality, row.Scope, row.WordsTouched }),
        Words = response.Words.Select(word => new { word.Word, word.Completion, word.Origin }),
    })!.AsObject();

    /// <summary>Projects Overview without exposing local durations or unpaired counters.</summary>
    public static JsonObject Overview(OverviewResponse response) => ProjectionJson.ToNode(new
    {
        response.ProjectName, response.MotifStoreCreatedUtc, response.LastFieldWorksSaveUtc,
        response.SelectionWordCount, response.SelectionTextCount, response.SelectionAddedWordCount,
        response.TextOccurrenceCount, response.WordformCount, response.RuleCount, response.LexemeCount,
        response.AssessmentId, response.AssessedUtc, response.GrammarFingerprint, response.SelectionFingerprint,
        response.TextCoverage, response.Accuracy,
        Timing = new { response.Timing.ParserWorkLimitedWordCount, response.Timing.ObjectTimingDetail,
            response.Timing.ObjectTimingAggregatesAvailable, response.Timing.CostComparison },
        response.Warnings, response.WritingSystems, response.LastParserRefusal,
        LookFirst = new { response.LookFirst.ApprovedLostWords, response.LookFirst.SharedLostMorphemes,
            response.LookFirst.ParserWorkLimitedWords, response.LookFirst.UnknownDifferentWordCount,
            response.LookFirst.ApprovedSameTextDifferentEntryWords, response.LookFirst.ApprovedSameTextDifferentEntryAvailable,
            response.LookFirst.SharedLostMorphemesAvailable, response.LookFirst.UnparsedWords,
            response.LookFirst.StemsWithoutInflectionClass },
        response.FrequencyBands, response.Ambiguity, response.WordOrigins, response.SelectionResolved,
        response.WordCoveragePercent, response.ProjectFileName, response.BaselineCapturedUtc,
        response.BaselineSourceLastWriteUtc, response.IsStale, response.BaselineToken,
    })!.AsObject();
}
