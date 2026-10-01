using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// An interlinear row of morphemes: each one its form over its gloss over its category, with the form and
/// gloss linking into FieldWorks when the project names the entry. Texts, Results and Try a Word all show a
/// word's parts this way, so they show them through this one control. A morpheme the reading names by identity
/// opens the inspector when clicked, through <see cref="InspectLink.RequestedEvent"/>.
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

    public static readonly StyledProperty<bool> FormLinksProperty =
        AvaloniaProperty.Register<MorphemeRow, bool>(nameof(FormLinks));

    static MorphemeRow()
    {
        MorphsProperty.Changed.AddClassHandler<MorphemeRow>((row, _) => row.Rebuild());
        ShowCategoryProperty.Changed.AddClassHandler<MorphemeRow>((row, _) => row.Rebuild());
        SeparatorsProperty.Changed.AddClassHandler<MorphemeRow>((row, _) => row.Rebuild());
        RevealLinksProperty.Changed.AddClassHandler<MorphemeRow>((row, _) => row.Rebuild());
        FormLinksProperty.Changed.AddClassHandler<MorphemeRow>((row, _) => row.Rebuild());
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

    /// <summary>
    /// Whether each linked morpheme's form is itself the link into FieldWorks, marked with a small arrow, instead of a
    /// separate link under it; for a row where many morphemes would otherwise each repeat the tool's name.
    /// </summary>
    public bool FormLinks
    {
        get => GetValue(FormLinksProperty);
        set => SetValue(FormLinksProperty, value);
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
        var formIsLink = FormLinks && morph.HasLink;
        column.Children.Add(formIsLink ? FormLink(morph) : new CopyableTextBlock
            { Text = morph.Form, FontWeight = FontWeight.SemiBold, Classes = { "morphForm" } });
        column.Children.Add(new CopyableTextBlock
            { Text = morph.GlossOrPlaceholder, Classes = { "morphGloss" } });
        if (ShowCategory && morph.Category is { Length: > 0 })
            column.Children.Add(new CopyableTextBlock { Text = morph.Category, Classes = { "morphCategory", "muted" } });
        if (morph.HasLink && !formIsLink) column.Children.Add(Link(morph, RevealLinks));

        var block = new Border { Child = column, Classes = { "morph" }, Tag = morph };
        if (last) block.Classes.Add("last");
        else if (Separators) block.Classes.Add("morphEdge");
        if (morph.InspectSubject is not { } reference) return block;

        // The inspector opens on a click or a key, never on hover, and the page stays where it is.
        block.Focusable = true;
        block.Classes.Add("inspectable");
        AutomationProperties.SetName(block, $"Inspect {morph.Form}");
        ToolTip.SetTip(block, $"Show {morph.Form} in the inspector");
        block.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left || e.Source is Visual source &&
                source.FindAncestorOfType<HyperlinkButton>(includeSelf: true) is not null)
                return;
            block.Focus(NavigationMethod.Pointer);
            InspectLink.Request(block, reference);
            e.Handled = true;
        };
        block.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            InspectLink.Request(block, reference);
            e.Handled = true;
        };
        return block;
    }

    private static HyperlinkButton FormLink(ParserReadingMorphViewModel morph)
    {
        var words = new StackPanel { Orientation = Orientation.Horizontal, Classes = { "morphFormLinkWords" } };
        words.Children.Add(new TextBlock { Text = morph.Form, Classes = { "morphFormText" } });
        words.Children.Add(new TextBlock { Text = "↗", Classes = { "morphLinkMark" } });
        var button = new HyperlinkButton
        {
            Content = words,
            NavigateUri = morph.Link,
            Classes = { "morphForm", "morphFormLink", "morphLink" },
        };
        AutomationProperties.SetName(button, morph.LinkName);
        ToolTip.SetTip(button, morph.FormLinkTip);
        return button;
    }

    private static HyperlinkButton Link(ParserReadingMorphViewModel morph, bool reveal)
    {
        var button = new HyperlinkButton
        {
            Content = morph.LinkText,
            NavigateUri = morph.Link,
            Padding = new Thickness(0),
            Classes = { "morphLink" },
        };
        if (reveal)
        {
            button.Classes.Add("revealControl");
            button.Classes.Add("revealLink");
            button.Classes.Add("revealOnHover");
        }
        AutomationProperties.SetName(button, morph.LinkName);
        ToolTip.SetTip(button, morph.LinkName);
        return button;
    }
}
