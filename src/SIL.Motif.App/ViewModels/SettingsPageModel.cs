namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Page registration for Settings. Its view requires the window's existing Settings model as its data
/// context so display choices and project preferences share one owner.
/// </summary>
public sealed class SettingsPageModel : PageModel, IDisposable
{
    private SettingsViewModel? _settings;
    private int _handoffRequestVersion;

    public SettingsPageModel(WorkspaceContext context) : base(context)
    {
        Connections = new AssistantConnectionsViewModel(context);
        Handoff = context.HandoffSession;
    }

    /// <summary>The assistant connection controls hosted in Settings.</summary>
    public AssistantConnectionsViewModel Connections { get; }

    /// <summary>The file Handoff action hosted in Settings.</summary>
    public HandoffSessionViewModel Handoff { get; }

    /// <summary>Changes when a Handoff request should be brought into view.</summary>
    public int HandoffRequestVersion => _handoffRequestVersion;

    /// <summary>The existing window Settings model, assigned by the window to share its display preferences.</summary>
    internal SettingsViewModel? Settings
    {
        get => _settings;
        set
        {
            if (value is not null)
            {
                value.AssistantConnections = Connections;
                if (_handoffRequestVersion > 0) value.ShowHandoffByCopyingFiles = true;
            }
            SetProperty(ref _settings, value);
        }
    }

    protected override void OnProjectCleared() => Handoff.OnProjectCleared();

    protected override Task OnStopWorkAsync() => Handoff.OnStopWorkAsync();

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
        Handoff.OnProjectOpenedAsync(projectPath, cancellationToken);

    protected override Task OnEvidencePublishedAsync(EvidenceLens lens, CancellationToken cancellationToken) =>
        Handoff.OnEvidencePublishedAsync(lens, cancellationToken);

    protected override void OnRequested(PageRequest request)
    {
        if (request is HandOffRequest)
        {
            if (Settings is { } settings) settings.ShowHandoffByCopyingFiles = true;
            _handoffRequestVersion++;
            OnPropertyChanged(nameof(HandoffRequestVersion));
        }
        Handoff.OnRequested(request);
    }

    public void Dispose() => Connections.Dispose();
}
