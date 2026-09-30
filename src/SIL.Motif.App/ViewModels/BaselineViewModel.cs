using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Shows a project's current Baseline — when FieldWorks last saved it, and whether FieldWorks holds the
/// project now — and captures a fresh one on demand. Reading never mutates anything: the initial state
/// comes from <see cref="ICommandClient.GetCurrentBaselineAsync"/>, a read-only query with no capture
/// side effect. Only <see cref="RefreshCommand"/> captures.
/// </summary>
public sealed partial class BaselineViewModel : ObservableObject, IProjectStateParticipant
{
    /// <summary>The words a person reads beside the Baseline's timestamp; pinned identically in the CLI.</summary>
    public const string FreshnessSentence = "as of FieldWorks' last save";

    private readonly ICommandClient _commandClient;
    private readonly TimeProvider _timeProvider;
    private string? _projectPath;
    private int _projectGeneration;

    public BaselineViewModel(ICommandClient commandClient, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        _commandClient = commandClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => _projectPath is not null);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBaseline))]
    [NotifyPropertyChangedFor(nameof(CapturedAtText))]
    [NotifyPropertyChangedFor(nameof(CapturedUtc))]
    private BaselineToken? _token;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CapturedTimeText))]
    [NotifyPropertyChangedFor(nameof(SavedText))]
    private DateTimeOffset? _sourceLastWriteUtc;

    /// <summary>The project file's last-write time as last read.</summary>
    [ObservableProperty]
    private DateTimeOffset? _projectLastWriteUtc;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeldStatusText))]
    private bool _fieldWorksHeldProject;

    /// <summary>Why the last read or write was refused, in the window's words.</summary>
    [ObservableProperty]
    private WindowRefusal? _shownRefusal;

    /// <summary>The Baseline's captured time in the current culture, or a placeholder before any capture.</summary>
    public string CapturedTimeText => SourceLastWriteUtc is { } savedUtc
        ? TimeZoneInfo.ConvertTime(savedUtc, _timeProvider.LocalTimeZone).ToString("f", CultureInfo.CurrentCulture)
        : "No Baseline captured yet";

    /// <summary>Which FieldWorks save the Baseline copies, said as such so it is not read as the capture time.</summary>
    public string SavedText => SourceLastWriteUtc is { } savedUtc
        ? $"From FieldWorks' save of {TimeZoneInfo.ConvertTime(savedUtc, _timeProvider.LocalTimeZone).ToString("f", CultureInfo.CurrentCulture)}"
        : string.Empty;

    /// <summary>When Motif captured the Baseline, which can be long after the save it copies.</summary>
    public string CapturedAtText => Token is { } token &&
        DateTimeOffset.TryParse(token.CapturedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var captured)
            ? $"Captured {TimeZoneInfo.ConvertTime(captured, _timeProvider.LocalTimeZone).ToString("ddd d MMM, h:mm tt", CultureInfo.CurrentCulture)}"
            : string.Empty;

    /// <summary>When Motif captured the Baseline, or <c>null</c> before any capture.</summary>
    public DateTimeOffset? CapturedUtc => Token is { } token &&
        DateTimeOffset.TryParse(token.CapturedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var captured)
            ? captured
            : null;

    /// <summary>Instance-bindable form of <see cref="FreshnessSentence"/>, for a view's binding path.</summary>
    public string FreshnessText => FreshnessSentence;

    /// <summary>A one-line sentence naming whether FieldWorks holds the project right now.</summary>
    public string HeldStatusText => FieldWorksHeldProject
        ? "FieldWorks holds this project open right now."
        : "FieldWorks does not currently hold this project.";

    public bool HasBaseline => Token is not null;

    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>Raised after a successful Refresh so the window can reload data from the new Baseline.</summary>
    public event EventHandler? Refreshed;

    /// <summary>Loads the current Baseline for a newly chosen project, discarding whatever was shown before.</summary>
    public async Task SetProjectAsync(string fwDataPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fwDataPath);
        ClearProject();
        _projectPath = fwDataPath;
        RefreshCommand.NotifyCanExecuteChanged();

        await CheckAsync(cancellationToken);
    }

    internal void ClearProject()
    {
        _projectGeneration++;
        _projectPath = null;
        Token = null;
        SourceLastWriteUtc = null;
        ProjectLastWriteUtc = null;
        FieldWorksHeldProject = false;
        ShownRefusal = null;
        RefreshCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Reads the recorded Baseline and the project file's last-write time again, so a save FieldWorks made since
    /// shows as such. Reads only: it never captures and never starts a run.
    /// </summary>
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_projectPath is not { } path) return;
        var generation = _projectGeneration;
        var outcome = await _commandClient.GetCurrentBaselineAsync(new CurrentBaselineRequest(path), cancellationToken);
        if (generation != _projectGeneration || !ReferenceEquals(path, _projectPath)) return;
        ApplySuccessOnly(outcome.Succeeded, outcome.Refusal,
            outcome.Value?.Token, outcome.Value?.SourceLastWriteUtc, outcome.Value?.FieldWorksHeldProject ?? false);
        if (outcome.Succeeded) ProjectLastWriteUtc = outcome.Value?.ProjectLastWriteUtc;
    }

    private async Task RefreshAsync()
    {
        if (_projectPath is not { } path) return;
        var generation = _projectGeneration;

        var outcome = await _commandClient.CaptureBaselineAsync(
            new BaselineCaptureRequest(path), CancellationToken.None);
        if (generation != _projectGeneration || !ReferenceEquals(path, _projectPath)) return;

        var applied = ApplySuccessOnly(outcome.Succeeded, outcome.Refusal,
            outcome.Value?.Token, outcome.Value?.SourceLastWriteUtc, outcome.Value?.FieldWorksHeldProject ?? false);
        if (!applied) return;
        ProjectLastWriteUtc = SourceLastWriteUtc;
        Refreshed?.Invoke(this, EventArgs.Empty);
    }

    // Shared by the read-only load and the capturing Refresh: state changes only on success either way.
    private bool ApplySuccessOnly(
        bool succeeded, Refusal? refusal, BaselineToken? token, DateTimeOffset? sourceLastWriteUtc,
        bool fieldWorksHeldProject)
    {
        if (!succeeded)
        {
            ShownRefusal = refusal is null ? null : WindowRefusal.From(refusal);
            return false;
        }

        Token = token;
        SourceLastWriteUtc = sourceLastWriteUtc;
        FieldWorksHeldProject = fieldWorksHeldProject;
        ShownRefusal = null;
        return true;
    }

    ProjectOpenStage IProjectStateParticipant.OpenStage => ProjectOpenStage.Baseline;

    void IProjectStateParticipant.ClearProject() => ClearProject();

    Task IProjectStateParticipant.OpenProjectAsync(string projectPath, CancellationToken cancellationToken) =>
        SetProjectAsync(projectPath, cancellationToken);
}
