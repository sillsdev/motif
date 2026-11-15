using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Owns a file Handoff and the evidence it covers for the current project.</summary>
public sealed class HandoffSessionViewModel : ObservableObject, IDisposable
{
    private readonly WorkspaceContext _context;

    public HandoffSessionViewModel(WorkspaceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        Handoff = new HandoffViewModel(context.Commands, context.Selection, context.FolderPicker, context.DragSource,
            context.Clock, context.Clipboard);
        Handoff.PropertyChanged += OnHandoffPropertyChanged;
        context.Assess.PropertyChanged += OnAssessChanged;
    }

    /// <summary>The session's AI Handoff run.</summary>
    public HandoffViewModel Handoff { get; }

    /// <summary>The action's label for the first write or a rewrite.</summary>
    public string HandoffActionText => Handoff.HasCompletedFiles ? MotifAppStrings.HandoffWriteAgain : MotifAppStrings.HandoffWrite;

    internal void OnProjectCleared()
    {
        Handoff.ProjectPath = null;
        Handoff.Reset();
    }

    // The session outlives one project, so wait for the run instead of disposing it.
    internal async Task OnStopWorkAsync()
    {
        if (!Handoff.IsActive) return;
        Handoff.CancelCommand.Execute(null);
        if (Handoff.RunCommand.ExecutionTask is { } running) await running.ConfigureAwait(true);
    }

    internal Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
    {
        Handoff.ProjectPath = projectPath;
        return Task.CompletedTask;
    }

    internal Task OnEvidencePublishedAsync(EvidenceLens lens, CancellationToken cancellationToken)
    {
        UpdateAvailability();
        if (Handoff.SelectedTrace is not null) return Task.CompletedTask;
        if (lens.Today.Assessment is not { } shown)
        {
            Handoff.InvocationId = null;
            return Task.CompletedTask;
        }
        Handoff.InvocationId = shown.Assessment.InvocationId;
        Handoff.LatestAssessmentAt = shown.CompletedAt;
        Handoff.CoverageText = CoverageOf(shown.CompletedAt, _context.Clock, _context.Assess.Words.CountSummary,
            _context.Selection.ChosenTextIds.Count, _context.Selection.PastedWordEntries.Count);
        return Task.CompletedTask;
    }

    private void UpdateAvailability() => Handoff.UnavailableReason = string.Format(System.Globalization.CultureInfo.CurrentCulture,
        MotifAppStrings.HandoffNoParseResultsFormat, _context.Assess.ShownRefusal?.Sentence ??
            (_context.Evidence.Stored?.LastParserRefusal is { } refusal ? WindowRefusal.From(refusal).Sentence :
                MotifAppStrings.HandoffParseAllWordsFirst));

    internal void OnRequested(PageRequest request)
    {
        if (request is not HandOffRequest handOff) return;
        if (handOff.SelectedTrace is { } trace) Handoff.UseSelectedTrace(trace);
        else Handoff.UseWords(handOff.Words, handOff.WarningScope);
    }

    private static string CoverageOf(DateTimeOffset? at, TimeProvider clock, string words, int texts, int pasted)
    {
        var sources = new List<string>();
        if (texts > 0) sources.Add(PluralText.Format(MotifAppStrings.HandoffCoverageTextsPattern, texts));
        if (pasted > 0) sources.Add(PluralText.Format(MotifAppStrings.HandoffCoveragePastedWordsPattern, pasted));
        var from = sources.Count == 2
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.HandoffCoverageJoinFormat, sources[0], sources[1])
            : sources.Count == 1 ? sources[0] : null;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var shown = at is { } parsed ? WindowTimeText.FormatWithDate(parsed, clock) : null;
        return shown is { } when
            ? from is null
                ? string.Format(culture, MotifAppStrings.HandoffCoverageOnFormat, when, words)
                : string.Format(culture, MotifAppStrings.HandoffCoverageOnFromFormat, when, words, from)
            : from is null
                ? string.Format(culture, MotifAppStrings.HandoffCoverageLatestFormat, words)
                : string.Format(culture, MotifAppStrings.HandoffCoverageLatestFromFormat, words, from);
    }

    private void OnHandoffPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        OnPropertyChanged(nameof(HandoffActionText));

    private void OnAssessChanged(object? sender, PropertyChangedEventArgs e) => UpdateAvailability();

    public void Dispose()
    {
        _context.Assess.PropertyChanged -= OnAssessChanged;
        Handoff.PropertyChanged -= OnHandoffPropertyChanged;
    }
}
