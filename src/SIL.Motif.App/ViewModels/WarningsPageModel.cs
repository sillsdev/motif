using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The Warnings page's model: the grammar's findings, counted beside the page's sidebar label, and the check a
/// person starts when nothing has been checked for this Baseline yet. It owns the grammar check and publishes a
/// <see cref="GrammarSummary"/> of it to the context for the pages that summarise it.
/// </summary>
public sealed class WarningsPageModel : PageModel
{
    private int _readGeneration;

    public WarningsPageModel(WorkspaceContext context) : base(context)
    {
        Grammar = new GrammarViewModel(context.Commands, context.Clock);
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

    protected override void OnProjectCleared()
    {
        _readGeneration++;
        Grammar.Clear();
    }

    // Opening shows the check stored for this Baseline, and never starts one of its own.
    protected override async Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
    {
        Grammar.Clear();
        var generation = ++_readGeneration;
        var stored = await Context.Commands
            .ReadStoredGrammarCheckAsync(new GrammarCheckRequest(projectPath), cancellationToken).ConfigureAwait(true);
        if (generation != _readGeneration || !string.Equals(projectPath, Context.ProjectPath, StringComparison.Ordinal)) return;
        if (stored.Succeeded) Grammar.LoadStored(projectPath, stored.Value?.Check);
    }

    protected override async Task OnEvidencePublishedAsync(ProjectEvidence evidence, CancellationToken cancellationToken)
    {
        if (Grammar.IsLoading || Context.ProjectPath is not { } path) return;
        var generation = ++_readGeneration;
        var stored = await Context.Commands.ReadStoredGrammarCheckAsync(new GrammarCheckRequest(path), cancellationToken)
            .ConfigureAwait(true);
        if (generation == _readGeneration && path == Context.ProjectPath && stored.Succeeded)
            Grammar.LoadStored(path, stored.Value?.Check);
    }

    // A person asked for the new Baseline, so its grammar is checked rather than only read.
    protected override async Task OnBaselineCapturedAsync(CancellationToken cancellationToken)
    {
        if (Context.ProjectPath is not { } path) return;
        await Grammar.SetProjectAsync(path, cancellationToken).ConfigureAwait(true);
        await Context.PublishGrammarCheckedAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task CheckGrammarAsync()
    {
        if (Context.ProjectPath is not { } path) return;
        await Grammar.SetProjectAsync(path).ConfigureAwait(true);
        await Context.PublishGrammarCheckedAsync().ConfigureAwait(true);
    }

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
