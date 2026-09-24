using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Checks a project's grammar as a whole, independently of any Text or word: once when a project is
/// chosen, and again after every successful Baseline refresh. Holds no dependency on
/// <see cref="AssessViewModel"/> — a grammar finding never comes from running an Assessment.
/// </summary>
public sealed partial class GrammarViewModel : ObservableObject
{
    private readonly ICommandClient _commandClient;
    private string? _projectPath;
    private int _generation;

    public GrammarViewModel(ICommandClient commandClient)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        _commandClient = commandClient;
        CheckCommand = new AsyncRelayCommand(CheckAsync, () => _projectPath is not null);
        Warnings.PropertyChanged += OnWarningsChanged;
    }

    // Which state shows depends on whether the table has rows, so it is re-read whenever that changes.
    private void OnWarningsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GrammarWarningsViewModel.HasAny)) return;
        OnPropertyChanged(nameof(ShowNoFindings));
        OnPropertyChanged(nameof(ShowFindings));
        OnPropertyChanged(nameof(SummaryText));
    }

    /// <summary>The last check's findings, as a sortable, searchable table.</summary>
    public GrammarWarningsViewModel Warnings { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowRefused))]
    [NotifyPropertyChangedFor(nameof(ShowNoBaseline))]
    [NotifyPropertyChangedFor(nameof(ShowNoFindings))]
    [NotifyPropertyChangedFor(nameof(ShowFindings))]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRefused))]
    [NotifyPropertyChangedFor(nameof(ShowNoBaseline))]
    [NotifyPropertyChangedFor(nameof(ShowNoFindings))]
    [NotifyPropertyChangedFor(nameof(ShowFindings))]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private Refusal? _refusal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoBaseline))]
    [NotifyPropertyChangedFor(nameof(ShowNoFindings))]
    [NotifyPropertyChangedFor(nameof(ShowFindings))]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private bool _hasBaseline;

    /// <summary>Whether a check has ever completed (successfully or not) for the current project.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRefused))]
    [NotifyPropertyChangedFor(nameof(ShowNoBaseline))]
    [NotifyPropertyChangedFor(nameof(ShowNoFindings))]
    [NotifyPropertyChangedFor(nameof(ShowFindings))]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private bool _hasChecked;

    public IAsyncRelayCommand CheckCommand { get; }

    /// <summary>How long the check in progress has run, for the wait screen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ElapsedText))]
    private int _elapsedSeconds;

    /// <summary>The wait screen's running time, so a long first check visibly still moves.</summary>
    public string ElapsedText => ElapsedSeconds < 2 ? string.Empty : $"{ElapsedSeconds} s";

    public bool ShowLoading => IsLoading;
    public bool ShowRefused => !IsLoading && HasChecked && Refusal is not null;
    public bool ShowNoBaseline => !IsLoading && HasChecked && Refusal is null && !HasBaseline;
    public bool ShowNoFindings => !IsLoading && HasChecked && Refusal is null && HasBaseline && !Warnings.HasAny;
    public bool ShowFindings => !IsLoading && HasChecked && Refusal is null && HasBaseline && Warnings.HasAny;

    /// <summary>What the stepper shows for this stage.</summary>
    public string SummaryText => IsLoading ? "Checking grammar..."
        : Refusal is { } refusal ? refusal.Message
        : !HasChecked ? "Not checked yet"
        : !HasBaseline ? "Capture a Baseline first"
        : Warnings.HasAny ? (Warnings.TotalCount == 1 ? "1 finding" : $"{Warnings.TotalCount} findings")
        : "No findings";

    /// <summary>Sets the project to check and immediately checks it, discarding whatever was shown before.</summary>
    public async Task SetProjectAsync(string? fwDataPath, CancellationToken cancellationToken = default)
    {
        _projectPath = fwDataPath;
        _generation++;
        IsLoading = false;
        Refusal = null;
        HasBaseline = false;
        HasChecked = false;
        Warnings.Load(null);
        CheckCommand.NotifyCanExecuteChanged();
        if (fwDataPath is not null) await CheckAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_projectPath is not { } path) return;

        var generation = ++_generation;
        IsLoading = true;
        Refusal = null;
        ElapsedSeconds = 0;

        var checking = _commandClient.CheckGrammarAsync(new GrammarCheckRequest(path), cancellationToken);
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (!checking.IsCompleted)
        {
            await Task.WhenAny(checking, Task.Delay(1000, CancellationToken.None)).ConfigureAwait(true);
            if (generation != _generation) return;
            ElapsedSeconds = (int)started.Elapsed.TotalSeconds;
        }

        var outcome = await checking.ConfigureAwait(true);
        if (generation != _generation) return;

        // The table is filled before the state flips, so no view ever sees "checked" with an empty table.
        if (outcome.Succeeded)
        {
            Warnings.Load(outcome.Value!.Findings);
            HasBaseline = outcome.Value.HasBaseline;
        }
        else
        {
            Warnings.Load(null);
            HasBaseline = false;
            Refusal = outcome.Refusal;
        }

        HasChecked = true;
        IsLoading = false;
    }
}
