using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Shows a project's current Baseline — when FieldWorks last saved it, and whether FieldWorks holds the
/// project now — and captures a fresh one on demand. Reading never mutates anything: the initial state
/// comes from <see cref="ICommandClient.GetCurrentBaselineAsync"/>, a read-only query with no capture
/// side effect. Only <see cref="RefreshCommand"/> captures.
/// </summary>
public sealed partial class BaselineViewModel : ObservableObject
{
    /// <summary>The words a person reads beside the Baseline's timestamp; pinned identically in the CLI.</summary>
    public const string FreshnessSentence = "as of FieldWorks' last save";

    private readonly ICommandClient _commandClient;
    private string? _projectPath;

    public BaselineViewModel(ICommandClient commandClient)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        _commandClient = commandClient;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => _projectPath is not null);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBaseline))]
    private BaselineToken? _token;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CapturedTimeText))]
    private DateTimeOffset? _sourceLastWriteUtc;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeldStatusText))]
    private bool _fieldWorksHeldProject;

    [ObservableProperty]
    private string? _refusalMessage;

    /// <summary>The Baseline's captured time in the current culture, or a placeholder before any capture.</summary>
    public string CapturedTimeText => SourceLastWriteUtc is { } savedUtc
        ? savedUtc.ToLocalTime().ToString("f", CultureInfo.CurrentCulture)
        : "No Baseline captured yet";

    /// <summary>Instance-bindable form of <see cref="FreshnessSentence"/>, for a view's binding path.</summary>
    public string FreshnessText => FreshnessSentence;

    /// <summary>A one-line sentence naming whether FieldWorks holds the project right now.</summary>
    public string HeldStatusText => FieldWorksHeldProject
        ? "FieldWorks holds this project open right now."
        : "FieldWorks does not currently hold this project.";

    /// <summary>Whether an Assessment is on record for the Baseline currently loaded here.</summary>
    /// <remarks>
    /// This view model has no channel of its own to an Assessment store; whoever runs an Assessment
    /// against the current Baseline sets this so a later successful <see cref="RefreshCommand"/> knows
    /// whether to raise <see cref="OfferRerun"/>.
    /// </remarks>
    public bool HasAssessment { get; set; }

    public bool HasBaseline => Token is not null;

    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    /// Raised after every successful Refresh, before <see cref="OfferRerun"/>, so a composing view model can
    /// reload whatever it reads from the current Baseline — the Text list first of all, which a project
    /// chosen before its first capture has never had.
    /// </summary>
    public event EventHandler? Refreshed;

    /// <summary>Raised after a successful Refresh that replaced a Baseline an Assessment already covered.</summary>
    public event EventHandler? OfferRerun;

    /// <summary>Loads the current Baseline for a newly chosen project, discarding whatever was shown before.</summary>
    public async Task SetProjectAsync(string fwDataPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fwDataPath);
        _projectPath = fwDataPath;
        Token = null;
        SourceLastWriteUtc = null;
        FieldWorksHeldProject = false;
        RefusalMessage = null;
        HasAssessment = false;
        RefreshCommand.NotifyCanExecuteChanged();

        var outcome = await _commandClient.GetCurrentBaselineAsync(
            new CurrentBaselineRequest(fwDataPath), cancellationToken);
        ApplySuccessOnly(outcome.Succeeded, outcome.Refusal?.Message,
            outcome.Value?.Token, outcome.Value?.SourceLastWriteUtc, outcome.Value?.FieldWorksHeldProject ?? false);
    }

    private async Task RefreshAsync()
    {
        if (_projectPath is null) return;

        var hadAssessment = HasAssessment;
        var outcome = await _commandClient.CaptureBaselineAsync(
            new BaselineCaptureRequest(_projectPath), CancellationToken.None);

        var applied = ApplySuccessOnly(outcome.Succeeded, outcome.Refusal?.Message,
            outcome.Value?.Token, outcome.Value?.SourceLastWriteUtc, outcome.Value?.FieldWorksHeldProject ?? false);
        if (!applied) return;
        Refreshed?.Invoke(this, EventArgs.Empty);
        if (hadAssessment) OfferRerun?.Invoke(this, EventArgs.Empty);
    }

    // Shared by the read-only load and the capturing Refresh: state changes only on success either way.
    private bool ApplySuccessOnly(
        bool succeeded, string? refusalMessage, BaselineToken? token, DateTimeOffset? sourceLastWriteUtc,
        bool fieldWorksHeldProject)
    {
        if (!succeeded)
        {
            RefusalMessage = refusalMessage;
            return false;
        }

        Token = token;
        SourceLastWriteUtc = sourceLastWriteUtc;
        FieldWorksHeldProject = fieldWorksHeldProject;
        RefusalMessage = null;
        HasAssessment = false;
        return true;
    }
}
