using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;

namespace SIL.Motif.App.ViewModels;

/// <summary>Controls the once-only tech demo notice shown by the main window.</summary>
public sealed partial class TechDemoNoticeViewModel : ObservableObject
{
    /// <summary>The first-run notice shown before a person applies changes.</summary>
    public const string NoticeText =
        "Motif is a tech demo. It reads your project and writes nothing until you press Apply. Keep a FieldWorks backup, and tell us what goes wrong.";

    private readonly ITechDemoNoticePreferences _preferences;
    private readonly IUriLauncher _launcher;

    /// <summary>Creates the notice from the saved acknowledgment and the window's link launcher.</summary>
    /// <param name="preferences">Persists whether the person has acknowledged the notice.</param>
    /// <param name="launcher">Opens the Motif issue page.</param>
    public TechDemoNoticeViewModel(ITechDemoNoticePreferences preferences, IUriLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(launcher);
        _preferences = preferences;
        _launcher = launcher;
        IsVisible = !preferences.HasSeenTechDemoNotice;
    }

    /// <summary>Whether the notice is still waiting for acknowledgment.</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>Hides the notice and remembers the acknowledgment.</summary>
    [RelayCommand]
    private void GotIt()
    {
        _preferences.MarkTechDemoNoticeSeen();
        IsVisible = false;
    }

    /// <summary>Opens the issue page where a person can report a problem.</summary>
    [RelayCommand]
    private async Task ReportProblemAsync() =>
        await _launcher.LaunchAsync(new Uri(AppLinks.Issues)).ConfigureAwait(true);
}
