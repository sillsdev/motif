using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

internal sealed class ReviewWordPresentationHost(ReviewPageModel page) : IWordPresentationHost
{
    public async Task<WordCardReadResult> ReadCardAsync(
        WordPresentationKey key,
        long evidenceRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Find(key) is not { } change || change.Presentation.EvidenceRevision != evidenceRevision)
            return WordCardReadResult.Failed("This change has changed before its details could be shown.", false);

        await page.ShowContextCommand.ExecuteAsync(change).ConfigureAwait(true);
        var listed = change.Listed!;
        WordCardSentenceToken[] ChangedTokens(IEnumerable<UncertaintyTokenViewModel> tokens) =>
            tokens.Select(token => new WordCardSentenceToken(token.Form, token.FormWritingSystem, token.IsChanged)).ToArray();
        var pending = new WordCardPendingChange(
            change.Reading,
            change.HasAnalyses,
            change.HasRowMorphs,
            change.RowAnalysisIsParserBuilt,
            change.RowMorphs,
            change.IsUncertain,
            ChangedTokens(change.BeforeWords),
            [],
            change.HasUnavailableContext,
            page.Context, change.ContextSource);
        var sections = new WordCardSection[]
        {
            pending,
            new WordCardAnalysis(listed.Card, listed.CardToken, listed.HasCard ? null : listed.NotParsedText),
        };
        return WordCardReadResult.Read(new WordCardDocument(key, evidenceRevision, sections));
    }

    public async ValueTask<WordActionResult> HandleAsync(WordRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Find(request.Key) is not { } change || change.Presentation.EvidenceRevision != request.EvidenceRevision)
            return new WordActionResult(false, "This change has changed. Move to it again to use that action.");

        switch (request.Action)
        {
            case WordAction.OpenCard:
                await page.ShowContextCommand.ExecuteAsync(change).ConfigureAwait(true);
                return new WordActionResult(true);
            case WordAction.CloseCard:
            case WordAction.OpenMorphology:
                return new WordActionResult(true);
            case WordAction.OpenInText:
                return Execute(change.Listed!.Row.OpenInTextCommand);
            case WordAction.TryWord:
                return Execute(change.Listed!.Row.TryWordCommand);
            // The row's Keep and Undo are the list entry's, so Undo names dependents and clears the looked-at mark.
            case WordAction.UndoChange:
                if (EntryOf(change) is not { } undone) return new WordActionResult(false);
                await undone.UndoCommand.ExecuteAsync(null);
                return new WordActionResult(true);
            case WordAction.KeepChange:
                if (EntryOf(change) is not { } kept || !kept.KeepCommand.CanExecute(null))
                    return new WordActionResult(false);
                await kept.KeepCommand.ExecuteAsync(null);
                return new WordActionResult(true);
            case WordAction.ReconfirmChange:
                if (!page.ReconfirmChangeCommand.CanExecute(change)) return new WordActionResult(false);
                await page.ReconfirmChangeCommand.ExecuteAsync(change);
                return new WordActionResult(true);
            default:
                return new WordActionResult(false);
        }
    }

    private ChangeViewModel? Find(WordPresentationKey key) => key.Value.StartsWith("change:", StringComparison.Ordinal)
        ? page.ReviewEntries.Select(entry => entry.Change).FirstOrDefault(change =>
            change is not null && change.Presentation.Key == key)
        : null;

    private ReviewEntryViewModel? EntryOf(ChangeViewModel change) =>
        page.Entries.FirstOrDefault(entry => ReferenceEquals(entry.Change, change));

    private static WordActionResult Execute(System.Windows.Input.ICommand command)
    {
        if (!command.CanExecute(null)) return new WordActionResult(false);
        command.Execute(null);
        return new WordActionResult(true);
    }
}
