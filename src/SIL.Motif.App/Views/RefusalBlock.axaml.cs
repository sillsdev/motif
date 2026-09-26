using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// Shows a <see cref="WindowRefusal"/> the one way every page shows a refusal: its sentence, with the
/// command's own account folded under Details. Bind the refusal as its data context; with none it hides.
/// </summary>
/// <remarks>
/// A refusal that <see cref="WindowRefusal.OffersStoreDeletion"/> also shows Delete this file and reopen, but
/// only where the host binds <see cref="StoreDeletionCommand"/>: a surface that cannot reopen the project shows
/// no button rather than one that does nothing.
/// </remarks>
public sealed partial class RefusalBlock : UserControl
{
    /// <summary>What Delete this file and reopen runs, with the refusal as its parameter.</summary>
    public static readonly StyledProperty<ICommand?> StoreDeletionCommandProperty =
        AvaloniaProperty.Register<RefusalBlock, ICommand?>(nameof(StoreDeletionCommand));

    private readonly Button _deleteStoreButton;

    public RefusalBlock()
    {
        AvaloniaXamlLoader.Load(this);
        _deleteStoreButton = this.FindControl<Button>("DeleteStoreButton")!;
        IsVisible = false;
    }

    /// <summary>What Delete this file and reopen runs, with the refusal as its parameter.</summary>
    public ICommand? StoreDeletionCommand
    {
        get => GetValue(StoreDeletionCommandProperty);
        set => SetValue(StoreDeletionCommandProperty, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        IsVisible = DataContext is WindowRefusal;
        ShowStoreDeletion();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StoreDeletionCommandProperty) ShowStoreDeletion();
    }

    private void ShowStoreDeletion()
    {
        _deleteStoreButton.Command = StoreDeletionCommand;
        _deleteStoreButton.IsVisible =
            DataContext is WindowRefusal { OffersStoreDeletion: true } && StoreDeletionCommand is not null;
    }
}
