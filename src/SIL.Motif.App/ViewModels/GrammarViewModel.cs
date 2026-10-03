using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection.Usage;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Checks a project's grammar as a whole when requested or after a successful Baseline refresh. Reads stored
/// findings alongside allomorph refusals retained by Parse all words, without starting another parse.
/// </summary>
public sealed partial class GrammarViewModel : ObservableObject
{
    private readonly ICommandClient _commandClient;
    private readonly TimeProvider _timeProvider;
    private string? _projectPath;
    private int _generation;

    public GrammarViewModel(ICommandClient commandClient, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        _commandClient = commandClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
        CheckCommand = new AsyncRelayCommand(CheckFromUserAsync, () => _projectPath is not null);
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
    [NotifyPropertyChangedFor(nameof(ShownRefusal))]
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

    /// <summary>The current refusal in the window's words, with the command's own account under Details.</summary>
    public WindowRefusal? ShownRefusal => Refusal is null ? null : WindowRefusal.From(Refusal);

    /// <summary>What the Grammar page shows for this check.</summary>
    public string SummaryText => IsLoading ? "Checking grammar..."
        : ShownRefusal is { } refusal ? refusal.Sentence
        : !HasChecked ? "Not checked yet"
        : !HasBaseline ? "Capture a Baseline first"
        : Warnings.HasAny ? (Warnings.TotalCount == 1 ? "1 finding" : $"{Warnings.TotalCount} findings")
        : "No grammar findings were reported.";

    /// <summary>Sets the project to check and immediately checks it, discarding whatever was shown before.</summary>
    public async Task SetProjectAsync(string? fwDataPath, CancellationToken cancellationToken = default)
    {
        var preserveResolved = HasChecked && string.Equals(_projectPath, fwDataPath, StringComparison.Ordinal);
        if (preserveResolved)
        {
            _projectPath = fwDataPath;
            _generation++;
            IsLoading = false;
            Refusal = null;
            HasBaseline = false;
            HasChecked = false;
            CheckCommand.NotifyCanExecuteChanged();
        }
        else
        {
            ResetProject(fwDataPath);
        }
        if (fwDataPath is not null)
            await CheckAsync(cancellationToken, preserveResolved).ConfigureAwait(true);
    }

    /// <summary>Forgets the previous project's grammar while the next project opens.</summary>
    public void Clear() => ResetProject(null);

    /// <summary>Shows a stored check and enables Reload grammar without running the parser.</summary>
    public void LoadStored(string projectPath, GrammarCheckResponse? check)
    {
        ResetProject(projectPath);
        if (check is null) return;
        Warnings.Load(check.Findings);
        HasBaseline = check.HasBaseline;
        HasChecked = true;
    }

    private void ResetProject(string? projectPath)
    {
        _projectPath = projectPath;
        _generation++;
        IsLoading = false;
        Refusal = null;
        HasBaseline = false;
        HasChecked = false;
        Warnings.Load(null);
        CheckCommand.NotifyCanExecuteChanged();
    }

    private async Task CheckAsync(CancellationToken cancellationToken = default, bool preserveResolved = false)
    {
        if (_projectPath is not { } path) return;

        var generation = ++_generation;
        IsLoading = true;
        Refusal = null;
        ElapsedSeconds = 0;

        var checking = _commandClient.CheckGrammarAsync(new GrammarCheckRequest(path), cancellationToken);
        var started = _timeProvider.GetTimestamp();
        while (!checking.IsCompleted)
        {
            await Task.WhenAny(checking, Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, CancellationToken.None))
                .ConfigureAwait(true);
            if (generation != _generation) return;
            ElapsedSeconds = (int)_timeProvider.GetElapsedTime(started).TotalSeconds;
        }

        var outcome = await checking.ConfigureAwait(true);
        if (generation != _generation) return;

        // The table is filled before the state flips, so no view ever sees "checked" with an empty table.
        if (outcome.Succeeded)
        {
            Warnings.Load(outcome.Value!.Findings, preserveResolved);
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

    private async Task CheckFromUserAsync()
    {
        if (_projectPath is not { } path) return;
        using var usageAction = _commandClient.BeginUsageAction("grammar check",
            UsageArgumentShape.Text("fwDataPath"));
        await CheckAsync().ConfigureAwait(true);
    }
}
