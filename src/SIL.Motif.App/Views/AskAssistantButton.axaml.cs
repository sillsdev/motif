using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// "Ask an Assistant to Fix This": a button with a small flyout for an optional note. It raises
/// <see cref="AskedEvent"/>, which bubbles to the window, so the card, row or cell never needs the shell.
/// </summary>
public sealed partial class AskAssistantButton : UserControl
{
    /// <summary>Raised, bubbling, when the person sends the Request.</summary>
    public static readonly RoutedEvent<AskAssistantEventArgs> AskedEvent =
        RoutedEvent.Register<AskAssistantButton, AskAssistantEventArgs>("Asked", RoutingStrategies.Bubble);

    public static readonly StyledProperty<string> TargetKindProperty =
        AvaloniaProperty.Register<AskAssistantButton, string>(nameof(TargetKind), "word");

    public static readonly StyledProperty<string?> TargetIdProperty =
        AvaloniaProperty.Register<AskAssistantButton, string?>(nameof(TargetId));

    public static readonly StyledProperty<string?> TargetLabelProperty =
        AvaloniaProperty.Register<AskAssistantButton, string?>(nameof(TargetLabel));

    public static readonly StyledProperty<bool> CardStyleProperty =
        AvaloniaProperty.Register<AskAssistantButton, bool>(nameof(CardStyle));

    public AskAssistantButton() => AvaloniaXamlLoader.Load(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CardStyleProperty)
            this.FindControl<Button>("AskButton")?.Classes.Set("wordPresentationCardAction", CardStyle);
    }

    /// <summary>Whether the button takes the word card's action style rather than the plain one.</summary>
    public bool CardStyle
    {
        get => GetValue(CardStyleProperty);
        set => SetValue(CardStyleProperty, value);
    }

    /// <summary><c>word</c>, <c>warning</c> or <c>matrix-cell</c>.</summary>
    public string TargetKind
    {
        get => GetValue(TargetKindProperty);
        set => SetValue(TargetKindProperty, value);
    }

    /// <summary>The word, Warning or cell the Request is about.</summary>
    public string? TargetId
    {
        get => GetValue(TargetIdProperty);
        set => SetValue(TargetIdProperty, value);
    }

    /// <summary>A short label for the target.</summary>
    public string? TargetLabel
    {
        get => GetValue(TargetLabelProperty);
        set => SetValue(TargetLabelProperty, value);
    }

    private void OnAskClick(object? sender, RoutedEventArgs e)
    {
        var note = this.FindControl<TextBox>("NoteBox");
        if (!string.IsNullOrWhiteSpace(TargetId))
            RaiseEvent(new AskAssistantEventArgs(new AskAssistantTarget(TargetKind, TargetId,
                string.IsNullOrWhiteSpace(TargetLabel) ? TargetId : TargetLabel, note?.Text), this));
        if (note is not null) note.Text = string.Empty;
        CloseFlyout();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => CloseFlyout();

    private void CloseFlyout() => this.FindControl<Button>("AskButton")?.Flyout?.Hide();
}

/// <summary>A Request the person sent from an <see cref="AskAssistantButton"/>.</summary>
public sealed class AskAssistantEventArgs : RoutedEventArgs
{
    public AskAssistantEventArgs(AskAssistantTarget target, object source) : base(AskAssistantButton.AskedEvent, source) =>
        Target = target;

    /// <summary>What was asked about, with the note.</summary>
    public AskAssistantTarget Target { get; }
}
