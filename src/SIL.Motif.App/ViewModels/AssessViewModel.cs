using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Services;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Adapts the Assessment request and response to the shared cancellable command-run module. The module
/// owns lifecycle, cancellation, progress, and refusal classification; this adapter owns Selection input.
/// Grammar findings are not read here — <see cref="GrammarViewModel"/> reads them independently of any run.
/// </summary>
public sealed partial class AssessViewModel : CommandRunViewModel<AssessCommandResponse>
{
    private readonly ICommandClient _commandClient;
    private readonly SelectionViewModel _selection;

    public AssessViewModel(ICommandClient commandClient, SelectionViewModel selection)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        ArgumentNullException.ThrowIfNull(selection);
        _commandClient = commandClient;
        _selection = selection;
        _selection.PropertyChanged += OnSelectionPropertyChanged;
        Trace = new TraceWordViewModel(commandClient);
        PropertyChanged += OnResultChanged;
        Words.PropertyChanged += OnWordsPropertyChanged;
    }

    /// <summary>The project this Assessment measures, or <c>null</c> before a project has been chosen.</summary>
    [ObservableProperty]
    private string? _projectPath;

    partial void OnProjectPathChanged(string? value)
    {
        RunCommand.NotifyCanExecuteChanged();
        Trace.SetProjectPath(value);
    }

    /// <summary>
    /// The Texts stage's word data, for looking up how often a Results word occurs in the chosen Texts.
    /// <see langword="null"/> shows no occurrence count rather than guessing one.
    /// </summary>
    public TextWordsViewModel? TextWords { get; set; }

    /// <summary>The last successful Assessment's words, as a sortable, searchable table.</summary>
    public AssessWordsViewModel Words { get; } = new();

    /// <summary>Traces one word on demand against the current Baseline's grammar, for Try a Word.</summary>
    public TraceWordViewModel Trace { get; }

    protected override bool CanStartCore() => ProjectPath is not null && _selection.CanAssess;

    /// <summary>When the last Assessment finished, so an older Handoff can say it is out of date.</summary>
    [ObservableProperty]
    private DateTimeOffset? _completedAt;

    private void OnResultChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Result)) return;
        if (Result is not null) CompletedAt = DateTimeOffset.Now;
        Words.Load(Result?.Words, TextWords is { } textWords ? word => LookUpOccurrences(textWords, word) : null);
    }

    private static int? LookUpOccurrences(TextWordsViewModel textWords, string word) =>
        textWords.Rows.FirstOrDefault(row => row.Form == word)?.OccurrenceCount;

    // Choosing a Results word primes Try a Word with it, without starting a trace the person did not ask for.
    private void OnWordsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AssessWordsViewModel.SelectedRow)) return;
        Trace.Reset();
        if (Words.SelectedRow is not { } row) return;
        Trace.SetWord(row.Word);
        // A word with no readings and nothing missed has no analyses to show, so Try a Word takes the width.
        Trace.IsFocused = !row.HasReadings && row.MissedApproved.Count == 0;
    }

    protected override Task<CommandOutcome<AssessCommandResponse>> ExecuteCoreAsync(
        CancellationToken cancellationToken)
    {
        var request = new AssessRequest(ProjectPath!, _selection.BuildRequest());
        return _commandClient.AssessAsync(request, this, cancellationToken);
    }

    private void OnSelectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectionViewModel.CanAssess)) RunCommand.NotifyCanExecuteChanged();
    }
}
