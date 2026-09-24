using System.ComponentModel;

namespace SIL.Motif.App.ViewModels;

/// <summary>Opens the AI Handoff page with some words as the ones it will write.</summary>
/// <param name="Words">The words to hand off.</param>
public sealed record HandOffRequest(IReadOnlyList<string> Words) : PageRequest(WorkspacePage.AiHandoff);

/// <summary>The AI Handoff page's model: the action that writes the files, and which Assessment they cover.</summary>
public sealed class AiHandoffPageModel : PageModel
{
    public AiHandoffPageModel(WorkspaceContext context) : base(context) =>
        Handoff.PropertyChanged += OnHandoffPropertyChanged;

    public HandoffViewModel Handoff => Context.Handoff;

    /// <summary>What the page's action reads: the first write, or a rewrite.</summary>
    public string HandoffActionText => Handoff.HasCompletedFiles ? "Write the AI Handoff again" : "Write the AI Handoff";

    protected override void OnProjectCleared() => Handoff.Reset();

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
    {
        Handoff.ProjectPath = projectPath;
        return Task.CompletedTask;
    }

    protected override void OnEvidencePublished(WorkspaceEvidence evidence)
    {
        Handoff.InvocationId = evidence.Assessment.InvocationId;
        Handoff.LatestAssessmentAt = evidence.CompletedAt;
        Handoff.CoverageText = CoverageOf(evidence.CompletedAt, Context.Assess.Words.CountSummary,
            Context.Selection.ChosenTextIds.Count, Context.Selection.PastedWordEntries.Count);
    }

    protected override void OnRequested(PageRequest request)
    {
        if (request is HandOffRequest handOff) Handoff.UseWords(handOff.Words);
    }

    // What an AI Handoff written now would cover, so the reader knows which run the chat model will see.
    private static string CoverageOf(DateTimeOffset? at, string words, int texts, int pasted)
    {
        var sources = new List<string>();
        if (texts > 0) sources.Add(texts == 1 ? "1 text" : $"{texts} texts");
        if (pasted > 0) sources.Add(pasted == 1 ? "1 pasted word" : $"{pasted} pasted words");
        var from = sources.Count > 0 ? " from " + string.Join(" and ", sources) : string.Empty;
        return at is { } when
            ? $"Covers the Assessment of {when.ToLocalTime():ddd d MMM, h:mm tt}: {words}{from}."
            : $"Covers the latest Assessment: {words}{from}.";
    }

    private void OnHandoffPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        OnPropertyChanged(nameof(HandoffActionText));
}
