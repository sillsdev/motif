using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;

namespace SIL.Motif.App.ViewModels;

/// <summary>Controls the once-only tech demo notice shown by the main window.</summary>
public sealed partial class TechDemoNoticeViewModel : ObservableObject
{
    /// <summary>The first-run notice shown before a person applies changes.</summary>
    public const string NoticeText =
        "Motif is a tech demo. It writes nothing until you press Apply. Keep a FieldWorks backup.";

    private readonly ITechDemoNoticePreferences _preferences;

    /// <summary>Creates the notice from the saved acknowledgment.</summary>
    /// <param name="preferences">Persists whether the person has acknowledged the notice.</param>
    public TechDemoNoticeViewModel(ITechDemoNoticePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _preferences = preferences;
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
}
