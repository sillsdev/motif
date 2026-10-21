using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;

namespace SIL.Motif.App.ViewModels;

/// <summary>Shows a refused Motif store and the confirmation for deleting only that store.</summary>
public sealed partial class StoreRefusalBlockViewModel : ObservableObject
{
    /// <summary>The warning shown before discarding the refused store.</summary>
    public static string StoreDeletionWarning => MotifAppStrings.StoreDeletionWarning;

    /// <summary>Creates a notice using the workspace’s recovery commands.</summary>
    /// <param name="deleteStoreCommand">Starts confirmation for the selected refusal.</param>
    /// <param name="confirmStoreDeletionCommand">Deletes the store after confirmation.</param>
    /// <param name="cancelStoreDeletionCommand">Closes confirmation without deleting.</param>
    public StoreRefusalBlockViewModel(
        IRelayCommand<WindowRefusal> deleteStoreCommand,
        IAsyncRelayCommand confirmStoreDeletionCommand,
        IRelayCommand cancelStoreDeletionCommand)
    {
        DeleteStoreCommand = deleteStoreCommand;
        ConfirmStoreDeletionCommand = confirmStoreDeletionCommand;
        CancelStoreDeletionCommand = cancelStoreDeletionCommand;
    }

    /// <summary>Starts confirmation for the refused store.</summary>
    public IRelayCommand<WindowRefusal> DeleteStoreCommand { get; }

    /// <summary>Confirms deletion of the refused store.</summary>
    public IAsyncRelayCommand ConfirmStoreDeletionCommand { get; }

    /// <summary>Cancels deletion and keeps the store.</summary>
    public IRelayCommand CancelStoreDeletionCommand { get; }

    /// <summary>The explanation of what deletion discards and preserves.</summary>
    public string Warning => StoreDeletionWarning;

    /// <summary>The refusal currently shown, or null when the store is usable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRefusal))]
    private WindowRefusal? _refusal;

    /// <summary>Whether deletion confirmation is visible.</summary>
    [ObservableProperty]
    private bool _isConfirmingStoreDeletion;

    /// <summary>Whether there is a refusal to show.</summary>
    public bool HasRefusal => Refusal is not null;

    internal void Show(WindowRefusal? refusal, bool isConfirmingStoreDeletion)
    {
        Refusal = refusal;
        IsConfirmingStoreDeletion = isConfirmingStoreDeletion;
    }
}
