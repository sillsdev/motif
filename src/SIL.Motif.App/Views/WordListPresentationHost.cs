using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using ModuleWordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;

namespace SIL.Motif.App.Views;

internal sealed class WordListPresentationHost<T>(
    ItemsControl list,
    Func<T, WordPresentation> presentation,
    Func<T, IReadOnlyList<WordCardSection>> sections) : IWordPresentationHost where T : class
{
    public Task<WordCardReadResult> ReadCardAsync(
        WordPresentationKey key,
        long evidenceRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Find(key) is not { } item || presentation(item).EvidenceRevision != evidenceRevision)
            return Task.FromResult(WordCardReadResult.Failed("This word changed before its details could be shown.", false));
        return Task.FromResult(WordCardReadResult.Read(
            new WordCardDocument(key, evidenceRevision, sections(item))));
    }

    public async ValueTask<WordActionResult> HandleAsync(WordRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Find(request.Key) is not { } item || presentation(item).EvidenceRevision != request.EvidenceRevision)
            return new WordActionResult(false, "This word has changed. Move to it again to use that action.");

        var facts = presentation(item).Facts;
        switch (request.Action)
        {
            case WordAction.OpenInText:
                return Execute(facts.OpenInTextCommand);
            case WordAction.TryWord:
                return Execute(facts.TryWordCommand);
            case WordAction.SetOpinion when request.Opinion is { } opinion:
                return await SetOpinionAsync(facts, opinion).ConfigureAwait(true);
            case WordAction.NavigatePrevious:
                return await FocusRelativeAsync(item, -1, 1, cancellationToken).ConfigureAwait(true);
            case WordAction.NavigateNext:
                return await FocusRelativeAsync(item, 1, 1, cancellationToken).ConfigureAwait(true);
            case WordAction.NavigatePreviousPage:
                return await FocusRelativeAsync(item, -1, PageSize(), cancellationToken).ConfigureAwait(true);
            case WordAction.NavigateNextPage:
                return await FocusRelativeAsync(item, 1, PageSize(), cancellationToken).ConfigureAwait(true);
            case WordAction.NavigateFirst:
                return await FocusIndexAsync(0, cancellationToken).ConfigureAwait(true);
            case WordAction.NavigateLast:
                return await FocusIndexAsync(Items().Length - 1, cancellationToken).ConfigureAwait(true);
            case WordAction.OpenCard:
            case WordAction.CloseCard:
            case WordAction.OpenMorphology:
            case WordAction.ToggleRead:
                return new WordActionResult(true);
            default:
                return new WordActionResult(false);
        }
    }

    private async Task<WordActionResult> FocusRelativeAsync(
        T current,
        int direction,
        int distance,
        CancellationToken cancellationToken)
    {
        var items = Items();
        var index = Array.FindIndex(items, item => ReferenceEquals(item, current));
        return await FocusIndexAsync(index < 0 ? -1 : index + direction * distance, cancellationToken)
            .ConfigureAwait(true);
    }

    private async Task<WordActionResult> FocusIndexAsync(int target, CancellationToken cancellationToken)
    {
        if (target < 0 || target >= list.ItemCount) return new WordActionResult(false);
        list.ScrollIntoView(target);
        var row = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            list.UpdateLayout();
            var container = list.ContainerFromIndex(target);
            return container is ModuleWordRow direct
                ? direct
                : container?.GetVisualDescendants().OfType<ModuleWordRow>().FirstOrDefault();
        });
        cancellationToken.ThrowIfCancellationRequested();
        row?.FocusWord();
        return new WordActionResult(row is not null);
    }

    private int PageSize()
    {
        var viewport = list.GetVisualDescendants().OfType<ScrollViewer>()
            .FirstOrDefault(viewer => viewer.Name == "PART_ScrollViewer")?.Viewport.Height ?? list.Bounds.Height;
        var row = list.GetVisualDescendants().OfType<ModuleWordRow>().FirstOrDefault();
        return Math.Max(1, (int)Math.Floor(viewport / Math.Max(1, row?.Bounds.Height ?? 1)));
    }

    private T? Find(WordPresentationKey key) => Items()
        .FirstOrDefault(item => presentation(item).Key == key);

    private T[] Items() => list.Items.OfType<T>().ToArray();

    private static WordActionResult Execute(System.Windows.Input.ICommand command)
    {
        if (!command.CanExecute(null)) return new WordActionResult(false);
        command.Execute(null);
        return new WordActionResult(true);
    }

    private static async Task<WordActionResult> SetOpinionAsync(WordRowViewModel facts, OpinionMarkKind opinion)
    {
        var behavior = opinion switch
        {
            OpinionMarkKind.Approved => KeyboardShortcutBehavior.Approve,
            OpinionMarkKind.Disapproved => KeyboardShortcutBehavior.Disapprove,
            OpinionMarkKind.Unknown => KeyboardShortcutBehavior.Unknown,
            _ => (KeyboardShortcutBehavior?)null,
        };
        if (behavior is not { } shortcut)
            return new WordActionResult(false);
        IReadOnlyList<KeyboardStoredAnalysis> analyses = facts.Row.FieldWorksAnalysisId is { Length: > 0 } id
            ? [new KeyboardStoredAnalysis(id, facts.Row.Opinion ?? string.Empty, facts.Row.FieldWorksMorphemes)]
            : [];
        var result = await KeyboardOpinionShortcuts.StageAsync(
            facts.Word, null, analyses, facts.OpinionRoutes, shortcut).ConfigureAwait(true);
        return new WordActionResult(result.Handled, result.StatusMessage);
    }
}

internal static class WordListCardSections
{
    public static IReadOnlyList<WordCardSection> Listed(ListedWordViewModel listed) =>
        [new WordCardAnalysis(listed.Card, listed.CardToken, listed.HasCard ? null : listed.NotParsedText)];

    public static IReadOnlyList<WordCardSection> Moved(MovedWordViewModel moved) =>
        [new WordCardText("Before", moved.BeforeText),
            new WordCardAnalysis(moved.Listed.Card, moved.Listed.CardToken,
                moved.Listed.HasCard ? null : moved.Listed.NotParsedText)];

    public static IReadOnlyList<WordCardSection> Warning(WordRowViewModel row)
    {
        var sections = new List<WordCardSection> { new WordCardHeading("In FieldWorks") };
        if (row.FieldWorksMorphemes.Count > 0)
            sections.Add(new WordCardMorphology("Stored analysis", row.FieldWorksMorphemes));
        else
            sections.Add(new WordCardText("Stored analysis", row.OpinionLabel));
        sections.Add(new WordCardHeading("PanGloss"));
        if (row.PanGlossMorphemes.Count > 0)
            sections.Add(new WordCardMorphology(row.OutcomeWord,
                row.PanGlossMorphemes.Select(morph => morph.Morph).ToArray(),
                row.PanGlossMorphemes.Select(morph =>
                    new MorphemePanelAnnotation(morph.IsDifferent, morph.EntryDifferenceTip)).ToArray()));
        else
            sections.Add(new WordCardText("Outcome", row.OutcomeWord));
        if (row.HasMeaningDetail) sections.Add(new WordCardText("Meaning", row.MeaningDetail));
        if (row.HasIdentityDetail) sections.Add(new WordCardText("Morpheme identity", row.IdentityDetail));
        return sections;
    }
}
