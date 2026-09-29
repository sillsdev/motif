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

    public static readonly StyledProperty<bool> RevealLinksProperty =
        AvaloniaProperty.Register<MorphemeRow, bool>(nameof(RevealLinks));

    static MorphemeRow()
    {
        MorphsProperty.Changed.AddClassHandler<MorphemeRow>((row, _) => row.Rebuild());
        ShowCategoryProperty.Changed.AddClassHandler<MorphemeRow>((row, _) => row.Rebuild());
        SeparatorsProperty.Changed.AddClassHandler<MorphemeRow>((row, _) => row.Rebuild());
        RevealLinksProperty.Changed.AddClassHandler<MorphemeRow>((row, _) => row.Rebuild());
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

    /// <summary>Whether FieldWorks links wait for hover or keyboard focus to appear.</summary>
    public bool RevealLinks
    {
        get => GetValue(RevealLinksProperty);
        set => SetValue(RevealLinksProperty, value);
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
        var column = new StackPanel();
        column.Children.Add(morph.HasLink
            ? Link(morph, morph.Form, FontWeight.SemiBold, "morphForm", RevealLinks)
            : new CopyableTextBlock { Text = morph.Form, FontWeight = FontWeight.SemiBold, Classes = { "morphForm" } });
        column.Children.Add(morph.HasLink
            ? Link(morph, morph.GlossOrPlaceholder, FontWeight.Normal, "morphGloss", RevealLinks)
            : new CopyableTextBlock { Text = morph.GlossOrPlaceholder, Classes = { "morphGloss" } });
        if (ShowCategory && morph.Category is { Length: > 0 })
            column.Children.Add(new CopyableTextBlock { Text = morph.Category, Classes = { "morphCategory", "muted" } });

        var block = new Border { Child = column, Classes = { "morph" } };
        if (last) block.Classes.Add("last");
        else if (Separators) block.Classes.Add("morphEdge");
        return block;
    }

    private static HyperlinkButton Link(ParserReadingMorphViewModel morph, string text, FontWeight weight,
        string role, bool reveal)
    {
        var button = new HyperlinkButton
        {
            Content = text,
            NavigateUri = morph.Link,
            Padding = new Thickness(0),
            FontWeight = weight,
            Classes = { role },
        };
        if (reveal)
        {
            button.Classes.Add("revealControl");
            button.Classes.Add("revealLink");
        }
        AutomationProperties.SetName(button, morph.LinkName);
        return button;
    }
}
