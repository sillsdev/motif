using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using ModuleWordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;

namespace SIL.Motif.App.Views;

internal sealed class TextWordPresentationHost(TextWordsViewModel words, TextWordsPanel panel) : IWordPresentationHost
{
    public Task<WordCardReadResult> ReadCardAsync(
        WordPresentationKey key,
        long evidenceRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Find(key) is not { } row || row.Presentation.EvidenceRevision != evidenceRevision)
            return Task.FromResult(WordCardReadResult.Failed("The word changed before its details could be shown.", false));

        var listed = row.Listed;
        var sections = new List<WordCardSection>
        {
            new WordCardAnalysis(listed.Card, listed.CardToken, listed.HasCard ? null : listed.NotParsedText),
        };
        if (row.HasSeveralAnalyses) sections.Add(new WordCardText(string.Empty, row.ProjectSummary));
        sections.Add(new WordCardHeading("WHERE IT APPEARS"));
        sections.Add(new WordCardOccurrences(row.Occurrences));
        return Task.FromResult(WordCardReadResult.Read(new WordCardDocument(key, evidenceRevision, sections)));
    }

    public async ValueTask<WordActionResult> HandleAsync(WordRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var row = Find(request.Key);
        if (row is null || row.Presentation.EvidenceRevision != request.EvidenceRevision)
            return new WordActionResult(false, "This word has changed. Move to it again to use that action.");
        switch (request.Action)
        {
            case WordAction.OpenInText:
                Execute(row.Listed.Row.OpenInTextCommand);
                return new WordActionResult(true);
            case WordAction.TryWord:
                Execute(row.Listed.Row.TryWordCommand);
                return new WordActionResult(true);
            case WordAction.SetOpinion when request.Opinion is { } opinion:
                var behavior = opinion switch
                {
                    OpinionMarkKind.Approved => KeyboardShortcutBehavior.Approve,
                    OpinionMarkKind.Disapproved => KeyboardShortcutBehavior.Disapprove,
                    OpinionMarkKind.Unknown => KeyboardShortcutBehavior.Unknown,
                    _ => (KeyboardShortcutBehavior?)null,
                };
                if (behavior is not { } shortcut) return new WordActionResult(false);
                var result = await row.StageOpinionShortcutAsync(shortcut);
                return new WordActionResult(result.Handled, result.StatusMessage);
            case WordAction.OpenCard:
            case WordAction.CloseCard:
            case WordAction.OpenMorphology:
                return new WordActionResult(true);
            case WordAction.NavigatePrevious:
                return await FocusRelativeAsync(row, -1, 1, cancellationToken);
            case WordAction.NavigateNext:
                return await FocusRelativeAsync(row, 1, 1, cancellationToken);
            case WordAction.NavigatePreviousPage:
                return await FocusRelativeAsync(row, -1, PageSize(), cancellationToken);
            case WordAction.NavigateNextPage:
                return await FocusRelativeAsync(row, 1, PageSize(), cancellationToken);
            case WordAction.NavigateFirst:
                return await FocusIndexAsync(row, 0, cancellationToken);
            case WordAction.NavigateLast:
                return await FocusIndexAsync(row, words.Rows.Count - 1, cancellationToken);
            default:
                return new WordActionResult(false);
        }
    }

    private async Task<WordActionResult> FocusRelativeAsync(
        TextWordRowViewModel current,
        int direction,
        int distance,
        CancellationToken cancellationToken)
    {
        var index = IndexOf(current);
        return await FocusIndexAsync(current, index < 0 ? -1 : index + direction * distance, cancellationToken);
    }

    private async Task<WordActionResult> FocusIndexAsync(
        TextWordRowViewModel current,
        int target,
        CancellationToken cancellationToken)
    {
        if (target < 0 || target >= words.Rows.Count) return new WordActionResult(false);
        var list = panel.FindControl<ListBox>("TextWordsPanelRowsItems");
        if (list is null) return new WordActionResult(false);
        cancellationToken.ThrowIfCancellationRequested();
        list.ScrollIntoView(target);
        var container = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            list.UpdateLayout();
            return list.ContainerFromIndex(target);
        });
        cancellationToken.ThrowIfCancellationRequested();
        var row = container is ModuleWordRow direct
            ? direct
            : container?.GetVisualDescendants().OfType<ModuleWordRow>().FirstOrDefault();
        if (row is null) return new WordActionResult(false);
        row.FocusWord();
        return new WordActionResult(true);
    }

    private int PageSize()
    {
        var list = panel.FindControl<ListBox>("TextWordsPanelRowsItems");
        var viewport = list?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.Viewport.Height ?? 0;
        var row = list?.GetVisualDescendants().OfType<WordRow>().FirstOrDefault();
        return Math.Max(1, (int)Math.Floor(viewport / Math.Max(1, row?.Bounds.Height ?? 1)));
    }

    private int IndexOf(TextWordRowViewModel row) =>
        Array.FindIndex(words.Rows.ToArray(), item => ReferenceEquals(item, row));

    private TextWordRowViewModel? Find(WordPresentationKey key) =>
        words.Rows.FirstOrDefault(row => row.Presentation.Key == key);

    private static void Execute(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null)) command.Execute(null);
    }
}
