using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The Warnings page's model: the grammar's findings, counted beside the page's sidebar label, and the check a
/// person starts when nothing has been checked for this Baseline yet.
/// </summary>
public sealed class WarningsPageModel : PageModel
{
    public WarningsPageModel(WorkspaceContext context) : base(context)
    {
        CheckGrammarCommand = new AsyncRelayCommand(CheckGrammarAsync, () => Context.HasProject && !Grammar.IsLoading);
        Grammar.PropertyChanged += OnGrammarChanged;
        Grammar.Warnings.PropertyChanged += OnGrammarChanged;
        context.PropertyChanged += OnContextPropertyChanged;
    }

    public GrammarViewModel Grammar => Context.Grammar;

    /// <summary>Whether the open project's grammar has no check to show, so the page offers one.</summary>
    public bool IsGrammarNotChecked => Context.HasProject && !Grammar.HasChecked && !Grammar.IsLoading;

    /// <summary>Checks the open project's grammar: started only by a person, since it can take a minute.</summary>
    public IAsyncRelayCommand CheckGrammarCommand { get; }

    private Task CheckGrammarAsync() =>
        Context.ProjectPath is { } path ? Grammar.SetProjectAsync(path) : Task.CompletedTask;

    private void OnGrammarChanged(object? sender, PropertyChangedEventArgs e)
    {
        Badge = Grammar.ShowFindings ? Grammar.Warnings.TotalCount.ToString(CultureInfo.CurrentCulture) : string.Empty;
        RaiseCheckState();
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceContext.HasProject)) RaiseCheckState();
    }

    private void RaiseCheckState()
    {
        OnPropertyChanged(nameof(IsGrammarNotChecked));
        CheckGrammarCommand.NotifyCanExecuteChanged();
    }
}
