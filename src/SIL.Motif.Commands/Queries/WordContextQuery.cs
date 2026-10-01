using SIL.Motif.Host.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>Reads stored context for an exact word without requiring a Selection or Assessment.</summary>
public static class WordContextQuery
{
    /// <summary>Reads a Baseline wordform and all of its individual analysis opinions.</summary>
    public static CommandOutcome<WordContextResponse> Query(WordContextRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Word))
            return Refused("word-context.empty-word", FailureReason.InvalidArgument, "Enter a word to read its analyses.");
        var word = request.Word.Trim().Normalize(System.Text.NormalizationForm.FormD);
        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
            if (request.ExpectedBaseline is { } expected && expected != baseline?.Token)
                return Refused("word-context.baseline-changed", FailureReason.Refused,
                    "The Baseline changed before this word was read. Refresh the evidence and try again.");
            if (baseline is null) return CommandOutcome<WordContextResponse>.Success(new(word, false));
            if (!File.Exists(baseline.FwDataPath))
                return Refused("word-context.baseline-unavailable", FailureReason.StoreInconsistent,
                    "The exact Baseline file is unavailable. Capture a new Baseline to read its analyses.");
            using var reader = BaselineReadCache.Open(baseline.FwDataPath);
            var cache = reader.Cache;
            var context = BaselineWordContext.Read(cache, Path.GetFileNameWithoutExtension(project.FullFwDataPath), [word]);
            var analyses = context.Analyses[word];
            var link = context.WordLinks.GetValueOrDefault(word);
            var liveSave = File.Exists(project.FullFwDataPath)
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(project.FullFwDataPath), TimeSpan.Zero) : (DateTimeOffset?)null;
            return CommandOutcome<WordContextResponse>.Success(new(word, true)
            {
                Baseline = baseline.Token,
                SourceLastWriteUtc = baseline.SourceLastWriteUtc,
                PublishedUtc = baseline.PublishedUtc,
                IsStale = liveSave > baseline.SourceLastWriteUtc,
                IsInFieldWorks = context.PresentWords.Contains(word),
                Analyses = analyses,
                ExpectedAnalysis = AssessmentWordRows.ExpectedAnalysis(analyses),
                WordAnalysesLink = link,
            });
        });
    }

    private static CommandOutcome<WordContextResponse> Refused(string code, FailureReason reason, string message) =>
        CommandOutcome<WordContextResponse>.Refused(new Refusal(code, reason, message));
}
