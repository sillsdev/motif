using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// An interlinear row of morphemes: each one its form over its gloss over its category, with the form and
/// gloss linking into FieldWorks when the project names the entry. Texts, Results and Try a Word all show a
/// word's parts this way, so they show them through this one control.
/// </summary>
public sealed class MorphemeRow : WrapPanel
{
    public static readonly StyledProperty<IReadOnlyList<ParserReadingMorphViewModel>?> MorphsProperty =
        AvaloniaProperty.Register<MorphemeRow, IReadOnlyList<ParserReadingMorphViewModel>?>(nameof(Morphs));

    public static readonly StyledProperty<bool> ShowCategoryProperty =
        AvaloniaProperty.Register<MorphemeRow, bool>(nameof(ShowCategory), defaultValue: true);

    public static readonly StyledProperty<bool> SeparatorsProperty =
        AvaloniaProperty.Register<MorphemeRow, bool>(nameof(Separators));

    static MorphemeRow()
    {
        MorphsProperty.Changed.AddClassHandler<MorphemeRow>((row, _) => row.Rebuild());
        ShowCategoryProperty.Changed.AddClassHandler<MorphemeRow>((row, _) => row.Rebuild());
        SeparatorsProperty.Changed.AddClassHandler<MorphemeRow>((row, _) => row.Rebuild());
    }

    public MorphemeRow() => Orientation = Orientation.Horizontal;

    /// <summary>The morphemes in order; an empty list shows nothing.</summary>
    public IReadOnlyList<ParserReadingMorphViewModel>? Morphs
    {
        get => GetValue(MorphsProperty);
        set => SetValue(MorphsProperty, value);
    }

    /// <summary>Whether each morpheme shows its grammatical category under the gloss.</summary>
    public bool ShowCategory
    {
        get => GetValue(ShowCategoryProperty);
        set => SetValue(ShowCategoryProperty, value);
    }

    /// <summary>Whether a hairline separates one morpheme from the next, for a row inside a card.</summary>
    public bool Separators
    {
        get => GetValue(SeparatorsProperty);
        set => SetValue(SeparatorsProperty, value);
    }

    private void Rebuild()
    {
        Children.Clear();
        var morphs = Morphs ?? [];
        for (var index = 0; index < morphs.Count; index++)
            Children.Add(BlockFor(morphs[index], last: index == morphs.Count - 1));
    }

    private Control BlockFor(ParserReadingMorphViewModel morph, bool last)
    {
        var column = new StackPanel { Spacing = 0 };
        column.Children.Add(morph.HasLink
            ? Link(morph, morph.Form, FontWeight.SemiBold, 13)
            : new CopyableTextBlock { Text = morph.Form, FontWeight = FontWeight.SemiBold, FontSize = 13 });
        column.Children.Add(morph.HasLink
            ? Link(morph, morph.GlossOrPlaceholder, FontWeight.Normal, 12)
            : new CopyableTextBlock { Text = morph.GlossOrPlaceholder, FontSize = 12 });
        if (ShowCategory && morph.Category is { Length: > 0 })
            column.Children.Add(new CopyableTextBlock { Text = morph.Category, FontSize = 11, Classes = { "muted" } });

        var block = new Border
        {
            Child = column,
            Padding = new Thickness(0, 0, last ? 0 : 12, 0),
            Margin = new Thickness(0, 0, last ? 0 : 12, 4),
        };
        if (Separators && !last)
        {
            block.BorderThickness = new Thickness(0, 0, 1, 0);
            block.Classes.Add("morphEdge");
        }
        return block;
    }

    private static HyperlinkButton Link(ParserReadingMorphViewModel morph, string text, FontWeight weight, double size)
    {
        var button = new HyperlinkButton
        {
            Content = text,
            NavigateUri = morph.Link,
            Padding = new Thickness(0),
            FontWeight = weight,
            FontSize = size,
        };
        AutomationProperties.SetName(button, morph.LinkName);
        return button;
    }
}
