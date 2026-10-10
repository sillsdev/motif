using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Views;

internal sealed class ResultsTextWordPresentationHost(
    ResultsInTextViewModel inText,
    ResultsInTextPanel panel) : IWordPresentationHost
{
    public Task<WordCardReadResult> ReadCardAsync(
        WordPresentationKey key,
        long evidenceRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Find(key) is not { } token || token.EvidenceRevision != evidenceRevision)
            return Task.FromResult(WordCardReadResult.Failed(
                "This occurrence changed before its details could be shown.", false));
        var document = new WordCardDocument(key, evidenceRevision,
            [new WordCardAnalysis(null, token)]);
        return Task.FromResult(WordCardReadResult.Read(document));
    }

    public async ValueTask<WordActionResult> HandleAsync(WordRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var token = Find(request.Key);
        if (token is null || token.EvidenceRevision != request.EvidenceRevision)
            return new WordActionResult(false, "This occurrence has changed. Move to it again to use that action.");

        switch (request.Action)
        {
            case WordAction.OpenCard:
                await inText.OpenTokenCardAsync(token).ConfigureAwait(true);
                return new WordActionResult(true);
            case WordAction.CloseCard:
                inText.CloseTokenCard();
                panel.ReturnFocusToWord(token);
                return new WordActionResult(true);
            case WordAction.OpenMorphology:
                await inText.OpenTokenCardAsync(token).ConfigureAwait(true);
                return new WordActionResult(true);
            case WordAction.NavigatePrevious:
                return new WordActionResult(await panel.FocusNativeNeighbourAsync(token, -1));
            case WordAction.NavigateNext:
                return new WordActionResult(await panel.FocusNativeNeighbourAsync(token, 1));
            case WordAction.NavigatePreviousLine:
                return new WordActionResult(await panel.FocusLineAsync(token, -1));
            case WordAction.NavigateNextLine:
                return new WordActionResult(await panel.FocusLineAsync(token, 1));
            case WordAction.NavigatePreviousPage:
                return new WordActionResult(await panel.FocusLineAsync(token, -panel.ScreenLineCount(token), clamp: true));
            case WordAction.NavigateNextPage:
                return new WordActionResult(await panel.FocusLineAsync(token, panel.ScreenLineCount(token), clamp: true));
            case WordAction.NavigateFirst:
                return new WordActionResult(await panel.FocusBoundaryAsync(0));
            case WordAction.NavigateLast:
                return new WordActionResult(await panel.FocusBoundaryAsync(inText.DisplayedLineCount - 1));
            case WordAction.NavigateCardPrevious:
                await inText.MoveTokenCardAsync(-1).ConfigureAwait(true);
                return new WordActionResult(true);
            case WordAction.NavigateCardNext:
                await inText.MoveTokenCardAsync(1).ConfigureAwait(true);
                return new WordActionResult(true);
            case WordAction.SetOpinion when request.Opinion is { } opinion:
                return await SetOpinionAsync(token, opinion).ConfigureAwait(true);
            default:
                return new WordActionResult(false);
        }
    }

    private ResultsTokenViewModel? Find(WordPresentationKey key) =>
        inText.SelectedToken is { } selected && selected.PresentationKey == key
            ? selected
            : (inText.LinePages?.RealizedLines ?? []).SelectMany(line => line.Tokens)
                .FirstOrDefault(token => token.PresentationKey == key);

    private static async Task<WordActionResult> SetOpinionAsync(
        ResultsTokenViewModel token,
        OpinionMarkKind opinion)
    {
        if (token.Marking.FieldWorksAnalyses.Count != 1)
            return new WordActionResult(false, token.Marking.FieldWorksAnalyses.Count == 0
                ? $"{token.Form} has no stored analysis to change."
                : $"{token.Form} has {token.Marking.FieldWorksAnalyses.Count} analyses. Choose one in Analyze texts.");

        var kind = opinion switch
        {
            OpinionMarkKind.Approved => ChangeKinds.Approve,
            OpinionMarkKind.Disapproved => ChangeKinds.Reject,
            _ => ChangeKinds.Candidate,
        };
        var analysis = token.Marking.FieldWorksAnalyses[0];
        if (string.IsNullOrWhiteSpace(analysis.StoredAnalysisId))
            return new WordActionResult(false, $"Motif cannot identify the stored analysis for {token.Form}.");

        var choice = AnalysisMarkingState.OpinionChoicesFor(analysis)
            .FirstOrDefault(item => item.ChangeKind == kind);
        if (choice is null)
        {
            var requestedOpinion = opinion switch
            {
                OpinionMarkKind.Approved => ReadingGrade.Approved,
                OpinionMarkKind.Disapproved => ReadingGrade.Disapproved,
                _ => ReadingGrade.Candidate,
            };
            var currentOpinion = analysis.Opinion;
            var label = requestedOpinion switch
            {
                ReadingGrade.Approved => "Approved",
                ReadingGrade.Disapproved => "Disapproved",
                _ => "Unknown",
            };
            return new WordActionResult(false, currentOpinion == requestedOpinion
                ? $"{token.Form} is already {label}."
                : $"Motif cannot stage {label} for {token.Form} from this view.");
        }
        if (token.StageMarkingChoiceForTokenCommand?.CanExecute(token.BindMarkingChoice(choice)) != true)
            return new WordActionResult(false, $"Motif cannot stage the change for {token.Form} right now.");

        await token.StageMarkingChoiceForTokenCommand.ExecuteAsync(token.BindMarkingChoice(choice)).ConfigureAwait(true);
        return new WordActionResult(true);
    }
}
