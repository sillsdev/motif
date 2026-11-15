using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>The whole Context's words, grouped by written shape, and the places panel for the open word.</summary>
public sealed partial class WordsPageModel : PageModel
{
    private CancellationTokenSource? _panelCancellation;
    private long _panelGeneration;

    public WordsPageModel(WorkspaceContext context) : base(context)
    {
        Words = context.GetOrCreateTextWords();
        Words.OpenRowChanged += OnOpenRowChanged;
    }

    /// <summary>The Context's word rows, shared with the reader.</summary>
    public TextWordsViewModel Words { get; }

    /// <summary>The shared places panel for the open word, or null when none is open.</summary>
    [ObservableProperty]
    private WordOccurrencesViewModel? _occurrencePanel;

    /// <summary>Finishes when the panel for the latest open word has been built and its first page read.</summary>
    internal Task PanelPending { get; private set; } = Task.CompletedTask;

    protected override void OnProjectCleared() => ClosePanel();

    protected override Task OnStopWorkAsync()
    {
        ClosePanel();
        return Task.CompletedTask;
    }

    // Parser readings reach a row from the shown Assessment; the Texts page need not have been visited.
    protected override Task OnEvidencePublishedAsync(EvidenceLens lens, CancellationToken cancellationToken)
    {
        if (lens.Today.Assessment is not null) Words.ShowAssessmentSource(Context.Assess.Words.SourceOf);
        return Task.CompletedTask;
    }

    private void ClosePanel()
    {
        ++_panelGeneration;
        _panelCancellation?.Cancel();
        OccurrencePanel?.Cancel();
        OccurrencePanel = null;
    }

    private void OnOpenRowChanged()
    {
        var row = Words.OpenRow;
        if (row is null)
        {
            ClosePanel();
            return;
        }
        PanelPending = ShowPanelAsync(row, ++_panelGeneration);
    }

    private async Task ShowPanelAsync(TextWordRowViewModel row, long generation)
    {
        OccurrencePanel?.Cancel();
        OccurrencePanel = null;
        _panelCancellation?.Cancel();
        var cancellation = _panelCancellation = new CancellationTokenSource();
        try
        {
            // Stored analyses arrive with the row's detail, and the panel's readings are fixed when it opens.
            await Words.SettleVisibleDetailsAsync().ConfigureAwait(true);
            await Words.CardPending.ConfigureAwait(true);
            if (generation != _panelGeneration || !ReferenceEquals(Words.OpenRow, row) || row.Summary is not { } summary ||
                Words.ShapeOf(summary.Key) is not { } shape || Context.SelectionReads is not { Reader: { } reader } reads ||
                reads.Summary is not { } selection) return;
            if (shape.HasSeveralWordforms && row.HomographsUnread) await Words.ReadHomographsAsync(row).ConfigureAwait(true);
            if (generation != _panelGeneration || !ReferenceEquals(Words.OpenRow, row)) return;
            var changes = Context.Changes;
            var panel = ContextWordPanel.Build(row, shape, reader, selection.Texts, changes,
                (form, _) => AskAssistantAction.RunAsync(Context, new AskAssistantTarget("word", form, form, null)),
                row.Listed.CardToken, anchor => changes.Items.Any(change =>
                    change.Kind == ChangeKinds.Approve && change.Occurrence == anchor));
            if (panel is null) return;
            OccurrencePanel = panel;
            try { await panel.LoadPageAsync(0, cancellation.Token).ConfigureAwait(true); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException refusal) { panel.StatusSentence = refusal.Message; }
        }
        catch (OperationCanceledException) { }
    }
}
