using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using ModuleWordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;

namespace SIL.Motif.App.Views;

internal sealed class CompareWordPresentationHost(
    CompareViewModel compare,
    ListBox list,
    WordListOwner owner) : IWordPresentationHost
{
    public Task<WordCardReadResult> ReadCardAsync(
        WordPresentationKey key,
        long evidenceRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Find(key) is not { } word || word.EvidenceRevision != evidenceRevision)
            return Task.FromResult(WordCardReadResult.Failed("The word changed before its details could be shown.", false));
        return Task.FromResult(WordCardReadResult.Read(new WordCardDocument(key, evidenceRevision,
            [new WordCardAnalysis(word, word.CardToken)])));
    }

    public async ValueTask<WordActionResult> HandleAsync(WordRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var word = Find(request.Key);
        if (word is null || word.EvidenceRevision != request.EvidenceRevision)
            return new WordActionResult(false, "This word has changed. Move to it again to use that action.");
        switch (request.Action)
        {
            case WordAction.OpenInText:
                return Execute(word.WordRow.OpenInTextCommand);
            case WordAction.TryWord:
                return Execute(word.WordRow.TryWordCommand);
            case WordAction.SetOpinion when request.Opinion is { } opinion:
                var behavior = opinion switch
                {
                    OpinionMarkKind.Approved => KeyboardShortcutBehavior.Approve,
                    OpinionMarkKind.Disapproved => KeyboardShortcutBehavior.Disapprove,
                    OpinionMarkKind.Unknown => KeyboardShortcutBehavior.Unknown,
                    _ => (KeyboardShortcutBehavior?)null,
                };
                if (behavior is not { } shortcut) return new WordActionResult(false);
                var result = await word.StageOpinionShortcutAsync(shortcut);
                return new WordActionResult(result.Handled, result.StatusMessage);
            case WordAction.OpenCard when owner == WordListOwner.FixFirst:
                compare.FocusWord(word);
                return new WordActionResult(true);
            case WordAction.OpenCard:
            case WordAction.CloseCard:
            case WordAction.ToggleRead:
            case WordAction.OpenMorphology:
                return new WordActionResult(true);
            case WordAction.NavigatePrevious:
                return await FocusRelativeAsync(word, -1, 1, cancellationToken);
            case WordAction.NavigateNext:
                return await FocusRelativeAsync(word, 1, 1, cancellationToken);
            case WordAction.NavigatePreviousPage:
                return await FocusRelativeAsync(word, -1, PageSize(), cancellationToken);
            case WordAction.NavigateNextPage:
                return await FocusRelativeAsync(word, 1, PageSize(), cancellationToken);
            case WordAction.NavigateFirst:
                return await FocusIndexAsync(0, cancellationToken);
            case WordAction.NavigateLast:
                return await FocusIndexAsync((owner == WordListOwner.FixFirst ? compare.FixFirstRows.Count : compare.Words.Count) - 1, cancellationToken);
            default:
                return new WordActionResult(false);
        }
    }

    private async Task<WordActionResult> FocusRelativeAsync(
        CompareWordViewModel current,
        int direction,
        int distance,
        CancellationToken cancellationToken)
    {
        var index = IndexOf(current);
        return await FocusIndexAsync(index < 0 ? -1 : index + direction * distance, cancellationToken);
    }

    private async Task<WordActionResult> FocusIndexAsync(int target, CancellationToken cancellationToken)
    {
        var count = owner == WordListOwner.FixFirst ? compare.FixFirstRows.Count : compare.Words.Count;
        if (target < 0 || target >= count) return new WordActionResult(false);
        var row = await WordListNavigation.RealizeAsync(list, target, cancellationToken);
        if (row is null) return new WordActionResult(false);
        row.FocusWord();
        return new WordActionResult(true);
    }

    private int PageSize()
    {
        var viewport = list.GetVisualDescendants().OfType<ScrollViewer>()
            .FirstOrDefault(viewer => viewer.Name == "PART_ScrollViewer")?.Viewport.Height ?? 0;
        var row = list.GetVisualDescendants().OfType<ModuleWordRow>().FirstOrDefault();
        return Math.Max(1, (int)Math.Floor(viewport / Math.Max(1, row?.Bounds.Height ?? 1)));
    }

    private int IndexOf(CompareWordViewModel word) =>
        compare.IndexOfDisplayedWord(word, owner == WordListOwner.FixFirst);

    private CompareWordViewModel? Find(WordPresentationKey key) =>
        compare.FindMaterializedWord(key, owner);

    private static WordActionResult Execute(System.Windows.Input.ICommand command)
    {
        if (!command.CanExecute(null)) return new WordActionResult(false);
        command.Execute(null);
        return new WordActionResult(true);
    }
}
