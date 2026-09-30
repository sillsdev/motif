using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;

namespace SIL.Motif.App.ViewModels;

/// <summary>Defines the words or Texts affected by one Analyze texts operation.</summary>
public enum AnalysisOperationScope
{
    /// <summary>Every word occurrence in the currently selected Text.</summary>
    SelectedText,

    /// <summary>Every word occurrence in all chosen Texts.</summary>
    ChosenTexts,

    /// <summary>Every checked word occurrence.</summary>
    CheckedWords,

    /// <summary>The Selection supplied to the current Assessment.</summary>
    AssessmentSelection,
}

public sealed partial class ResultsInTextViewModel
{
    private void InitializeScopeCommands()
    {
        AcceptNewSetCommand = new AsyncRelayCommand<AnalysisOperationScope>(AcceptNewSetAsync,
            CanAcceptNewSet);
        AddParserReadingsCommand = new AsyncRelayCommand<AnalysisOperationScope>(AddParserReadingsAsync,
            CanAddParserReadings);
        MarkSpellingsIncorrectCommand = new AsyncRelayCommand<AnalysisOperationScope>(
            MarkSpellingsIncorrectAsync, CanMarkSpellingsIncorrect);
        RemoveAnalysesCommand = new AsyncRelayCommand<AnalysisOperationScope>(RemoveAnalysesAsync,
            CanRemoveAnalyses);
        UndoChangesCommand = new AsyncRelayCommand<AnalysisOperationScope>(UndoChangesAsync,
            CanUndoChanges);
    }

    /// <summary>Accepts new readings in the requested Text, Selection, or checked-word scope.</summary>
    public IAsyncRelayCommand<AnalysisOperationScope> AcceptNewSetCommand { get; private set; } = null!;

    /// <summary>Adds parser readings as Unknown in the requested scope.</summary>
    public IAsyncRelayCommand<AnalysisOperationScope> AddParserReadingsCommand { get; private set; } = null!;

    /// <summary>Marks distinct spellings Incorrect in the requested scope.</summary>
    public IAsyncRelayCommand<AnalysisOperationScope> MarkSpellingsIncorrectCommand { get; private set; } = null!;

    /// <summary>Removes stored analyses in the requested scope.</summary>
    public IAsyncRelayCommand<AnalysisOperationScope> RemoveAnalysesCommand { get; private set; } = null!;

    /// <summary>Removes pending changes in the requested scope.</summary>
    public IAsyncRelayCommand<AnalysisOperationScope> UndoChangesCommand { get; private set; } = null!;

    private IEnumerable<ResultsTokenViewModel> TokensFor(AnalysisOperationScope scope) => scope switch
    {
        AnalysisOperationScope.SelectedText => SelectedText?.Lines.SelectMany(line => line.Tokens) ?? [],
        AnalysisOperationScope.ChosenTexts => _allWords,
        AnalysisOperationScope.CheckedWords => CheckedTokens,
        _ => [],
    };

    private bool CanAcceptNewSet(AnalysisOperationScope scope)
    {
        if (_changes.AssessmentId is null) return false;
        return scope switch
        {
            AnalysisOperationScope.SelectedText => HasCompleteAssessmentForSelectedText(),
            AnalysisOperationScope.AssessmentSelection => _assess.Result?.Words is { Count: > 0 } words &&
                words.All(word => AnalysisMarkingState.Create(word).PanGlossClass is not
                    (AnalysisMarkingClass.Capped or AnalysisMarkingClass.NotAssessed)),
            AnalysisOperationScope.CheckedWords => CanAcceptCheckedNewSet(),
            _ => false,
        };
    }

    private bool CanAcceptCheckedNewSet()
    {
        if (_changes.AssessmentId is null || _assess.Result?.Words is not { Count: > 0 }) return false;
        var tokens = CheckedTokens.DistinctBy(token => token.Form, StringComparer.Ordinal).ToArray();
        return tokens.Length > 0 && tokens.All(token => token.Marking.PanGlossClass is not
            (AnalysisMarkingClass.Capped or AnalysisMarkingClass.NotAssessed));
    }

    private bool HasCompleteAssessmentForSelectedText()
    {
        if (SelectedText is null) return false;
        var tokens = SelectedText.Lines.SelectMany(line => line.Tokens).Where(token => token.IsWord)
            .DistinctBy(token => token.Form, StringComparer.Ordinal).ToArray();
        return tokens.Length > 0 && tokens.All(token => token.Marking.PanGlossClass is not
            (AnalysisMarkingClass.Capped or AnalysisMarkingClass.NotAssessed));
    }

    private async Task AcceptNewSetAsync(AnalysisOperationScope scope)
    {
        if (_changes.AssessmentId is not { } assessmentId) return;
        switch (scope)
        {
            case AnalysisOperationScope.SelectedText when SelectedText is { } text:
                await _changes.AcceptNewSetAsync(assessmentId, textId: text.TextId).ConfigureAwait(true);
                break;
            case AnalysisOperationScope.AssessmentSelection:
                await _changes.AcceptNewSetAsync(assessmentId, selection: true).ConfigureAwait(true);
                break;
            case AnalysisOperationScope.CheckedWords:
                foreach (var token in DistinctWords(CheckedTokens))
                    if (token.WordformId is { } wordformId)
                        await _changes.AcceptNewSetAsync(assessmentId,
                            CanonicalId.FromGuid(wordformId).Value).ConfigureAwait(true);
                break;
        }
    }

    private bool CanAddParserReadings(AnalysisOperationScope scope) =>
        scope != AnalysisOperationScope.AssessmentSelection && TokensFor(scope).Any(HasParserOnlyReading);

    private Task AddParserReadingsAsync(AnalysisOperationScope scope) =>
        AddParserReadingsAsUnknownAsync(TokensFor(scope));

    private static bool HasParserOnlyReading(ResultsTokenViewModel token) =>
        token.Marking.PanGlossReadings.Any(reading => reading.IsParserOnly);

    private async Task AddParserReadingsAsUnknownAsync(IEnumerable<ResultsTokenViewModel> tokens)
    {
        foreach (var token in DistinctWords(tokens))
        {
            foreach (var (reading, index) in token.Marking.PanGlossReadings
                         .Select((reading, index) => (reading, index))
                         .Where(item => item.reading.IsParserOnly)
                         .DistinctBy(item => ProjectAnalysisKey.For(item.reading.Analysis)))
            {
                await _changes.AddFromMarkingAsync(new AnalysisMarkingAction(
                    AnalysisMarkingActionKind.Add, "Add as Unknown", null, reading.Analysis, index,
                    "Not in FieldWorks", "Unknown", ChangeKinds.AddCandidate), token).ConfigureAwait(true);
            }
        }
    }

    private bool CanMarkSpellingsIncorrect(AnalysisOperationScope scope) => scope switch
    {
        AnalysisOperationScope.SelectedText => SelectedText is not null,
        AnalysisOperationScope.ChosenTexts => _allWords.Count > 0,
        AnalysisOperationScope.CheckedWords => CheckedTokens.Length > 0,
        _ => false,
    };

    private Task MarkSpellingsIncorrectAsync(AnalysisOperationScope scope) =>
        MarkSpellingsIncorrectAsync(TokensFor(scope));

    private async Task MarkSpellingsIncorrectAsync(IEnumerable<ResultsTokenViewModel> tokens)
    {
        foreach (var token in DistinctWords(tokens))
            await _changes.AddFromTextAsync(ChangeKinds.IncorrectSpelling, token).ConfigureAwait(true);
    }

    private bool CanRemoveAnalyses(AnalysisOperationScope scope) => scope switch
    {
        AnalysisOperationScope.SelectedText => SelectedText is not null,
        AnalysisOperationScope.ChosenTexts or AnalysisOperationScope.CheckedWords => TokensFor(scope)
            .SelectMany(token => token.Marking.FieldWorksAnalyses)
            .Any(analysis => !string.IsNullOrWhiteSpace(analysis.StoredAnalysisId)),
        _ => false,
    };

    private Task RemoveAnalysesAsync(AnalysisOperationScope scope)
    {
        if (scope == AnalysisOperationScope.SelectedText && SelectedText is { } text)
            return _changes.RemoveAnalysesInTextAsync(text.TextId);
        var ids = TokensFor(scope).SelectMany(token => token.Marking.FieldWorksAnalyses)
            .Select(analysis => analysis.StoredAnalysisId).Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal).ToArray();
        return ids.Length == 0 ? Task.CompletedTask : _changes.RemoveAnalysesAsync(ids);
    }

    private bool CanUndoChanges(AnalysisOperationScope scope) => scope != AnalysisOperationScope.AssessmentSelection &&
        HasChangesFor(TokensFor(scope));

    private bool HasChangesFor(IEnumerable<ResultsTokenViewModel> tokens)
    {
        var forms = tokens.Where(token => token.IsWord).Select(token => token.Form)
            .ToHashSet(StringComparer.Ordinal);
        return _changes.Items.Any(change => forms.Contains(change.Word));
    }

    private Task UndoChangesAsync(AnalysisOperationScope scope) =>
        UndoChangesAsync(TokensFor(scope));

    private async Task UndoChangesAsync(IEnumerable<ResultsTokenViewModel> tokens)
    {
        var wordTokens = tokens.Where(token => token.IsWord).ToArray();
        var forms = wordTokens.Select(token => token.Form).ToHashSet(StringComparer.Ordinal);
        var occurrences = wordTokens.Select(token => token.Occurrence).OfType<OccurrenceAnchor>().ToHashSet();
        foreach (var change in _changes.Items.Where(change => forms.Contains(change.Word) &&
                     (change.Occurrence is null || occurrences.Contains(change.Occurrence)))
                     .DistinctBy(change => change.ChangeId).ToArray())
            await _changes.RemoveCommand.ExecuteAsync(change).ConfigureAwait(true);
    }

    private static IEnumerable<ResultsTokenViewModel> DistinctWords(IEnumerable<ResultsTokenViewModel> tokens) =>
        tokens.Where(token => token.IsWord).DistinctBy(token => token.WordformId is { } id
            ? $"id:{id:N}" : $"word:{token.Form}");

    private bool CanRecheckCheckedChanges() => HasCheckedUncertainChanges;

    private void NotifyScopeCommands()
    {
        AcceptNewSetCommand.NotifyCanExecuteChanged();
        AddParserReadingsCommand.NotifyCanExecuteChanged();
        MarkSpellingsIncorrectCommand.NotifyCanExecuteChanged();
        RemoveAnalysesCommand.NotifyCanExecuteChanged();
        UndoChangesCommand.NotifyCanExecuteChanged();
        RecheckCheckedChangesCommand.NotifyCanExecuteChanged();
    }
}
