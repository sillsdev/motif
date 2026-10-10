using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Projection.Usage;
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

    private bool CanAcceptNewSet(AnalysisOperationScope scope) => CanNativeAcceptNewSet(scope);

    private async Task AcceptNewSetAsync(AnalysisOperationScope scope)
    {
        using var historyAction = _changes.BeginStagingAction();
        if (CanNativeAcceptNewSet(scope))
            await AddNativeParserReadingsAsync(scope, CanonicalId.Mint().Value).ConfigureAwait(true);
    }

    private bool CanAddParserReadings(AnalysisOperationScope scope) =>
        scope != AnalysisOperationScope.AssessmentSelection && NativeWordsFor(scope).Any(word => word.Actions.CanAddParserReading);

    private async Task AddParserReadingsAsync(AnalysisOperationScope scope)
    {
        using var historyAction = _changes.BeginStagingAction();
        await AddNativeParserReadingsAsync(scope).ConfigureAwait(true);
    }

    private bool CanMarkSpellingsIncorrect(AnalysisOperationScope scope) =>
        scope != AnalysisOperationScope.AssessmentSelection &&
        NativeWordsFor(scope).Any(word => word.Actions.CandidateWordformIds.Count == 1);

    private async Task MarkSpellingsIncorrectAsync(AnalysisOperationScope scope)
    {
        using var historyAction = _changes.BeginStagingAction();
        await MarkNativeSpellingsAsync(scope).ConfigureAwait(true);
    }

    private bool CanRemoveAnalyses(AnalysisOperationScope scope) =>
        scope != AnalysisOperationScope.AssessmentSelection && NativeAnalysisIds(scope).Count > 0;

    private Task RemoveAnalysesAsync(AnalysisOperationScope scope) => RemoveNativeAnalysesAsync(scope);

    private bool CanUndoChanges(AnalysisOperationScope scope) =>
        scope != AnalysisOperationScope.AssessmentSelection && NativeChangesFor(scope).Length > 0;

    private async Task UndoChangesAsync(AnalysisOperationScope scope)
    {
        using var historyAction = _changes.BeginStagingAction();
        await UndoNativeChangesAsync(scope).ConfigureAwait(true);
    }

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
