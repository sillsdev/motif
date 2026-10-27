using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using SIL.Motif.App.Views;

namespace SIL.Motif.App.Controls.WordPresentation;

/// <summary>
/// The interactive chrome of the word strips in one reader, built once and moved onto the one word that has the pointer,
/// keyboard focus or an open menu. Every other word shows a resting face in the same place, so a word costs its text
/// and analyses and not its buttons.
/// </summary>
internal sealed class WordStripOverlay
{
    /// <summary>The overlay a reader gives the strips below it.</summary>
    public static readonly AttachedProperty<WordStripOverlay?> InstanceProperty =
        AvaloniaProperty.RegisterAttached<WordStripOverlay, Control, WordStripOverlay?>("Instance", inherits: true);

    private CheckBox? _selectionBox;

    public WordStripOverlay()
    {
        foreach (var button in Buttons.Children.OfType<Button>())
            if (button.Flyout is { } flyout) flyout.Closed += (_, _) => Owner?.ReleaseOverlayIfIdle();
    }

    /// <summary>The opinion and add buttons.</summary>
    public WordDispositionButtons Buttons { get; } = new();

    /// <summary>The word the overlay is on now, if any.</summary>
    public WordStripToken? Owner { get; private set; }

    /// <summary>The selection checkbox, built on first use.</summary>
    public CheckBox SelectionBox => _selectionBox ??= WordStripToken.BuildPart<CheckBox>("SelectionBox", Owner?.Data);

    public static WordStripOverlay? GetInstance(Control control) => control.GetValue(InstanceProperty);

    public static void SetInstance(Control control, WordStripOverlay? value) => control.SetValue(InstanceProperty, value);

    /// <summary>Whether a menu opened from the overlay is still showing.</summary>
    public bool HasOpenMenu => Buttons.Children.OfType<Button>().Any(button => button.Flyout?.IsOpen == true);

    /// <summary>Moves the overlay onto a word.</summary>
    public void Claim(WordStripToken token)
    {
        if (ReferenceEquals(Owner, token)) return;
        var previous = Owner;
        Owner = token;
        previous?.SyncOverlay();
        token.SyncOverlay();
    }

    /// <summary>Takes the overlay off a word.</summary>
    public void Release(WordStripToken token)
    {
        if (!ReferenceEquals(Owner, token)) return;
        Owner = null;
        token.SyncOverlay();
    }
}
