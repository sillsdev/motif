using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The shared word analysis card opened from a row or Analyze texts.</summary>
public sealed partial class WordRowCard : UserControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<WordRowCard, bool>(nameof(IsOpen));
    public static readonly StyledProperty<ResultsTokenViewModel?> CardTokenProperty =
        AvaloniaProperty.Register<WordRowCard, ResultsTokenViewModel?>(nameof(CardToken));

    static WordRowCard()
    {
        IsOpenProperty.Changed.AddClassHandler<WordRowCard>((card, _) => card.LoadTimingForOpenRow());
        CardTokenProperty.Changed.AddClassHandler<WordRowCard>((card, _) => card.LoadTimingForOpenRow());
    }

    public WordRowCard()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>The analysis evidence whose card this control shows.</summary>
    public ResultsTokenViewModel? CardToken
    {
        get => GetValue(CardTokenProperty);
        set => SetValue(CardTokenProperty, value);
    }

    /// <summary>Whether the row containing this card is open.</summary>
    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    internal async void LoadTimingForOpenRow()
    {
        if (IsOpen && CardToken is { Actions: { } actions } token)
            await actions.LoadCardTimingAsync(token);
    }
}
