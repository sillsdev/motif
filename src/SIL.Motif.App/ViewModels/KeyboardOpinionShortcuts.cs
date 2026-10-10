using SIL.Motif.Host.PanGloss;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Requests;

namespace SIL.Motif.App.ViewModels;

internal sealed record KeyboardStoredAnalysis(string Id, string Opinion, IReadOnlyList<ParserReadingMorph> Morphs)
{
    public string DisplayReading => string.Join(" ", Morphs.Select(morph =>
        $"{morph.Form} = {(string.IsNullOrWhiteSpace(morph.Gloss) ? "?" : morph.Gloss)}"));
}

internal sealed record PendingStoredOpinionChange(
    string Kind, string Word, Guid? WordformId, string StoredAnalysisId, string DisplayReading,
    ExpectedContext? ExpectedContext = null);

internal sealed record KeyboardOpinionShortcutResult(bool Handled, string? StatusMessage);

internal static class KeyboardOpinionShortcuts
{
    public static async Task<KeyboardOpinionShortcutResult> StageAsync(
        string word, Guid? wordformId, IReadOnlyList<KeyboardStoredAnalysis> analyses,
        WordRowRoutes? routes, KeyboardShortcutBehavior behavior, ExpectedContext? expectedContext = null)
    {
        var kind = behavior switch
        {
            KeyboardShortcutBehavior.Approve => ChangeKinds.Approve,
            KeyboardShortcutBehavior.Disapprove => ChangeKinds.Reject,
            KeyboardShortcutBehavior.Unknown => ChangeKinds.Candidate,
            _ => throw new ArgumentOutOfRangeException(nameof(behavior)),
        };
        if (analyses.Count != 1)
            return new(true, analyses.Count == 0
                ? $"{word} has no stored analysis to change."
                : $"{word} has {analyses.Count} analyses. Choose one in Analyze texts.");

        var analysis = analyses[0];
        if (string.IsNullOrWhiteSpace(analysis.Id))
            return new(true, $"Motif cannot identify the stored analysis for {word}.");

        var current = new FieldWorksAnalysisMarking(analysis.Id, analysis.Opinion, analysis.Morphs);
        var choice = AnalysisMarkingState.OpinionChoicesFor(current)
            .FirstOrDefault(candidate => candidate.ChangeKind == kind);
        if (choice is null)
            return new(true, $"{word} is already {OpinionFor(kind)}.");
        if (routes?.Changes is { ProjectPath: null })
            return new(true, "Open a project before collecting a change.");
        if (routes?.StageOpinion is not { } stage)
            return new(true, "Open a project before collecting a change.");

        var change = new PendingStoredOpinionChange(kind, word, wordformId, analysis.Id,
            analysis.DisplayReading, expectedContext);
        var staged = await stage(change).ConfigureAwait(true);
        return new(true, staged ? null : $"Motif could not stage the change for {word}.");
    }

    private static string OpinionFor(string kind) => kind switch
    {
        ChangeKinds.Approve => "Approved",
        ChangeKinds.Reject => "Disapproved",
        _ => "Unknown",
    };
}
