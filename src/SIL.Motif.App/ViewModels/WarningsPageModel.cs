using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Queries;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The Warnings page's model: the grammar's findings, counted beside the page's sidebar label, and the check a
/// person starts when nothing has been checked for this Baseline yet. It owns the grammar check and publishes a
/// <see cref="GrammarSummary"/> of it to the context for the pages that summarise it.
/// </summary>
public sealed class WarningsPageModel : PageModel
{
    public WarningsPageModel(WorkspaceContext context) : base(context)
    {
        Grammar = new GrammarViewModel(context.Commands);
        CheckGrammarCommand = new AsyncRelayCommand(CheckGrammarAsync, () => Context.HasProject && !Grammar.IsLoading);
        Grammar.PropertyChanged += OnGrammarChanged;
        Grammar.Warnings.PropertyChanged += OnGrammarChanged;
        context.PropertyChanged += OnContextPropertyChanged;
        PublishSummary();
    }

    /// <summary>The page's own grammar check.</summary>
    public GrammarViewModel Grammar { get; }

    /// <summary>Whether the open project's grammar has no check to show, so the page offers one.</summary>
    public bool IsGrammarNotChecked => Context.HasProject && !Grammar.HasChecked && !Grammar.IsLoading;

    /// <summary>Checks the open project's grammar: started only by a person, since it can take a minute.</summary>
    public IAsyncRelayCommand CheckGrammarCommand { get; }

    protected override void OnProjectCleared() => Grammar.Clear();

    // Opening shows the check stored for this Baseline, and never starts one of its own.
    protected override async Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
    {
        await Grammar.SetProjectAsync(null, cancellationToken).ConfigureAwait(true);
        var stored = await Context.Commands
            .ReadStoredGrammarCheckAsync(new GrammarCheckRequest(projectPath), cancellationToken).ConfigureAwait(true);
        if (!string.Equals(projectPath, Context.ProjectPath, StringComparison.Ordinal)) return;
        // A stored hit is read, not rerun: pinned by `TheStoredReadStampsTheParserExactlyAsTheCheckDoes`.
        if (stored.Succeeded && stored.Value?.Check is not null)
            await Grammar.SetProjectAsync(projectPath, cancellationToken).ConfigureAwait(true);
    }

    // A person asked for the new Baseline, so its grammar is checked rather than only read.
    protected override Task OnBaselineCapturedAsync(CancellationToken cancellationToken) =>
        Context.ProjectPath is { } path ? Grammar.SetProjectAsync(path, cancellationToken) : Task.CompletedTask;

    private Task CheckGrammarAsync() =>
        Context.ProjectPath is { } path ? Grammar.SetProjectAsync(path) : Task.CompletedTask;

    private void OnGrammarChanged(object? sender, PropertyChangedEventArgs e)
    {
        Badge = Grammar.ShowFindings ? Grammar.Warnings.TotalCount.ToString(CultureInfo.CurrentCulture) : string.Empty;
        PublishSummary();
        RaiseCheckState();
    }

    private void PublishSummary() => Context.GrammarSummary =
        new GrammarSummary(Grammar.SummaryText, Grammar.ShowFindings, Grammar.Warnings.BreakdownText);

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
