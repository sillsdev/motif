namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Page registration for Settings. Its view requires the window's existing Settings model as its data
/// context so display choices and project preferences share one owner.
/// </summary>
public sealed class SettingsPageModel(WorkspaceContext context) : PageModel(context)
{
    private SettingsViewModel? _settings;

    /// <summary>The existing window Settings model, assigned by the window to share its display preferences.</summary>
    internal SettingsViewModel? Settings
    {
        get => _settings;
        set => SetProperty(ref _settings, value);
    }
}
