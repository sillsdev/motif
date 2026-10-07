using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Services;
using SIL.Motif.App.Views;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Controls.WordPresentation;

/// <summary>
/// Presents each morpheme as separate selectable form, gloss and category text, with links and inspection actions.
/// The parent draws decorative separators between its directly hosted parts.
/// </summary>
public sealed class MorphemePanel : Decorator
{
    private Panel _parts = new WrapPanel { Orientation = Orientation.Horizontal };
    private bool _partsStale;

    public static readonly StyledProperty<IReadOnlyList<ParserReadingMorphViewModel>?> MorphsProperty =
        AvaloniaProperty.Register<MorphemePanel, IReadOnlyList<ParserReadingMorphViewModel>?>(nameof(Morphs));

    public static readonly StyledProperty<IReadOnlyList<MorphemePanelAnnotation>?> AnnotationsProperty =
        AvaloniaProperty.Register<MorphemePanel, IReadOnlyList<MorphemePanelAnnotation>?>(nameof(Annotations));

    public static readonly StyledProperty<bool> ShowCategoryProperty =
        AvaloniaProperty.Register<MorphemePanel, bool>(nameof(ShowCategory), defaultValue: true);

    public static readonly StyledProperty<bool> WrapPartsProperty =
        AvaloniaProperty.Register<MorphemePanel, bool>(nameof(WrapParts), defaultValue: true);

    public static readonly StyledProperty<bool> InlinePartsProperty =
        AvaloniaProperty.Register<MorphemePanel, bool>(nameof(InlineParts));

    public static readonly StyledProperty<bool> SeparatorsProperty =
        AvaloniaProperty.Register<MorphemePanel, bool>(nameof(Separators));

    public static readonly StyledProperty<bool> RevealLinksProperty =
        AvaloniaProperty.Register<MorphemePanel, bool>(nameof(RevealLinks));

    public static readonly StyledProperty<bool> ShowLinksProperty =
        AvaloniaProperty.Register<MorphemePanel, bool>(nameof(ShowLinks), defaultValue: true);

    public static readonly StyledProperty<bool> CompactLinksProperty =
        AvaloniaProperty.Register<MorphemePanel, bool>(nameof(CompactLinks));

    public static readonly StyledProperty<bool> FormLinksProperty =
        AvaloniaProperty.Register<MorphemePanel, bool>(nameof(FormLinks));

    public static readonly StyledProperty<int> MaxVisibleMorphsProperty =
        AvaloniaProperty.Register<MorphemePanel, int>(nameof(MaxVisibleMorphs), int.MaxValue);

    public static readonly StyledProperty<Mark?> LeadingMarkProperty =
        AvaloniaProperty.Register<MorphemePanel, Mark?>(nameof(LeadingMark));

    public static readonly StyledProperty<string?> LeadingTextProperty =
        AvaloniaProperty.Register<MorphemePanel, string?>(nameof(LeadingText));

    public static readonly StyledProperty<MorphemeOverflowRequest?> OverflowRequestProperty =
        AvaloniaProperty.Register<MorphemePanel, MorphemeOverflowRequest?>(nameof(OverflowRequest));

    /// <summary>Raised, bubbling, when the reader asks the host to show a complete morphology reading.</summary>
    public static readonly RoutedEvent<MorphemeOverflowRequestedEventArgs> OverflowRequestedEvent =
        RoutedEvent.Register<MorphemePanel, MorphemeOverflowRequestedEventArgs>(
            nameof(OverflowRequestedEvent), RoutingStrategies.Bubble);

    public static readonly StyledProperty<IBrush?> SeparatorBrushProperty =
        AvaloniaProperty.Register<MorphemePanel, IBrush?>(nameof(SeparatorBrush));

    public static readonly StyledProperty<double> SeparatorThicknessProperty =
        AvaloniaProperty.Register<MorphemePanel, double>(nameof(SeparatorThickness));

    public static readonly StyledProperty<double> SeparatorOffsetProperty =
        AvaloniaProperty.Register<MorphemePanel, double>(nameof(SeparatorOffset));

    public static readonly StyledProperty<IReadOnlyList<int>?> WarningMarkedMorphIndicesProperty =
        AvaloniaProperty.Register<MorphemePanel, IReadOnlyList<int>?>(nameof(WarningMarkedMorphIndices));

    public static readonly StyledProperty<IBrush?> WarningBrushProperty =
        AvaloniaProperty.Register<MorphemePanel, IBrush?>(nameof(WarningBrush));

    public static readonly StyledProperty<double> WarningThicknessProperty =
        AvaloniaProperty.Register<MorphemePanel, double>(nameof(WarningThickness));

    static MorphemePanel()
    {
        MorphsProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateParts());
        AnnotationsProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateParts());
        ShowCategoryProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateParts());
        WrapPartsProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateParts());
        InlinePartsProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateParts());
        SeparatorsProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateVisual());
        RevealLinksProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateParts());
        ShowLinksProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateParts());
        CompactLinksProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateParts());
        FormLinksProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateParts());
        MaxVisibleMorphsProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateParts());
        LeadingMarkProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateParts());
        LeadingTextProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateParts());
        OverflowRequestProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateParts());
        SeparatorBrushProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateVisual());
        SeparatorThicknessProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateVisual());
        SeparatorOffsetProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateVisual());
        WarningMarkedMorphIndicesProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateVisual());
        WarningBrushProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateVisual());
        WarningThicknessProperty.Changed.AddClassHandler<MorphemePanel>((panel, _) => panel.InvalidateVisual());
    }

    public MorphemePanel() => Child = _parts;

    /// <summary>How many times this panel has built its parts; a row or strip should build each panel once.</summary>
    internal int PartBuildCount { get; private set; }

    // An object initializer sets several properties in turn, so the parts are built once, at the next measure.
    private void InvalidateParts()
    {
        _partsStale = true;
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_partsStale) Rebuild();
        if (InlineParts)
            foreach (var text in _parts.Children.OfType<CopyableTextBlock>())
                text.MaxWidth = availableSize.Width;
        return base.MeasureOverride(availableSize);
    }

    /// <summary>Whether morphology parts reflow onto another line when the available width runs out.</summary>
    public bool WrapParts
    {
        get => GetValue(WrapPartsProperty);
        set => SetValue(WrapPartsProperty, value);
    }

    /// <summary>Shows each selectable form and gloss directly in the compact occurrence strip.</summary>
    public bool InlineParts
    {
        get => GetValue(InlinePartsProperty);
        set => SetValue(InlinePartsProperty, value);
    }

    /// <summary>The complete reading in order; the panel keeps this source when only a prefix is presented.</summary>
    public IReadOnlyList<ParserReadingMorphViewModel>? Morphs
    {
        get => GetValue(MorphsProperty);
        set => SetValue(MorphsProperty, value);
    }

    /// <summary>Optional identity differences and their explanation tips, in the same order as <see cref="Morphs"/>.</summary>
    public IReadOnlyList<MorphemePanelAnnotation>? Annotations
    {
        get => GetValue(AnnotationsProperty);
        set => SetValue(AnnotationsProperty, value);
    }

    /// <summary>Whether each morpheme shows its grammatical category under the gloss.</summary>
    public bool ShowCategory
    {
        get => GetValue(ShowCategoryProperty);
        set => SetValue(ShowCategoryProperty, value);
    }

    /// <summary>Whether a hairline separates adjacent morphemes on the same wrapped line.</summary>
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

    /// <summary>Whether each morpheme includes its separate FieldWorks link.</summary>
    public bool ShowLinks
    {
        get => GetValue(ShowLinksProperty);
        set => SetValue(ShowLinksProperty, value);
    }

    /// <summary>Whether a revealed FieldWorks link shows only its arrow while keeping its full accessible name.</summary>
    public bool CompactLinks
    {
        get => GetValue(CompactLinksProperty);
        set => SetValue(CompactLinksProperty, value);
    }

    /// <summary>Whether a linked form itself opens FieldWorks instead of showing a separate link below it.</summary>
    public bool FormLinks
    {
        get => GetValue(FormLinksProperty);
        set => SetValue(FormLinksProperty, value);
    }

    /// <summary>The maximum number of constituents shown before a supplied overflow action; defaults to all.</summary>
    public int MaxVisibleMorphs
    {
        get => GetValue(MaxVisibleMorphsProperty);
        set => SetValue(MaxVisibleMorphsProperty, value);
    }

    /// <summary>A named outcome or opinion mark shown before the morphology, when this panel is a row cell.</summary>
    public Mark? LeadingMark
    {
        get => GetValue(LeadingMarkProperty);
        set => SetValue(LeadingMarkProperty, value);
    }

    /// <summary>The accessible words paired with <see cref="LeadingMark"/>.</summary>
    public string? LeadingText
    {
        get => GetValue(LeadingTextProperty);
        set => SetValue(LeadingTextProperty, value);
    }

    /// <summary>The stable word and evidence context for a host request to show the complete reading.</summary>
    public MorphemeOverflowRequest? OverflowRequest
    {
        get => GetValue(OverflowRequestProperty);
        set => SetValue(OverflowRequestProperty, value);
    }

    /// <summary>The live theme brush used for decorative separators drawn by this panel.</summary>
    public IBrush? SeparatorBrush
    {
        get => GetValue(SeparatorBrushProperty);
        set => SetValue(SeparatorBrushProperty, value);
    }

    /// <summary>The themed thickness of decorative separator strokes.</summary>
    public double SeparatorThickness
    {
        get => GetValue(SeparatorThicknessProperty);
        set => SetValue(SeparatorThicknessProperty, value);
    }

    /// <summary>The themed inset between a selectable part and the separator drawn after it.</summary>
    public double SeparatorOffset
    {
        get => GetValue(SeparatorOffsetProperty);
        set => SetValue(SeparatorOffsetProperty, value);
    }

    /// <summary>The indices of visible forms named by exact grammar-warning evidence.</summary>
    public IReadOnlyList<int>? WarningMarkedMorphIndices
    {
        get => GetValue(WarningMarkedMorphIndicesProperty);
        set => SetValue(WarningMarkedMorphIndicesProperty, value);
    }

    /// <summary>The live theme brush for named-morpheme warning underlines.</summary>
    public IBrush? WarningBrush
    {
        get => GetValue(WarningBrushProperty);
        set => SetValue(WarningBrushProperty, value);
    }

    /// <summary>The themed thickness of named-morpheme warning underlines.</summary>
    public double WarningThickness
    {
        get => GetValue(WarningThicknessProperty);
        set => SetValue(WarningThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (InlineParts)
        {
            if (WarningBrush is null || WarningThickness <= 0 || WarningMarkedMorphIndices is not { Count: > 0 }) return;
            var inlineWarningPen = new Pen(WarningBrush, WarningThickness);
            foreach (var form in _parts.Children.OfType<CopyableTextBlock>())
            {
                if (form.Tag is not InlineMorphemePart { IsForm: true } part ||
                    !WarningMarkedMorphIndices.Contains(part.Index) ||
                    form.TranslatePoint(new Point(0, form.Bounds.Height), this) is not { } bottomLeft ||
                    form.TranslatePoint(new Point(form.Bounds.Width, form.Bounds.Height), this) is not { } bottomRight)
                    continue;
                context.DrawLine(inlineWarningPen, bottomLeft, bottomRight);
            }
            return;
        }
        var parts = _parts.Children.OfType<StackPanel>().Where(part => part.Tag is ParserReadingMorphViewModel).ToArray();
        if (Separators && SeparatorBrush is not null && SeparatorThickness > 0)
        {
            var pen = new Pen(SeparatorBrush, SeparatorThickness);
            for (var index = 1; index < parts.Length; index++)
            {
                var previous = parts[index - 1];
                var current = parts[index];
                if (Math.Abs(previous.Bounds.Top - current.Bounds.Top) > 0.5) continue;
                var x = previous.Bounds.Right + SeparatorOffset;
                context.DrawLine(pen, new Point(x, previous.Bounds.Top), new Point(x, previous.Bounds.Bottom));
            }
        }

        if (WarningBrush is null || WarningThickness <= 0 || WarningMarkedMorphIndices is not { Count: > 0 }) return;
        var warningPen = new Pen(WarningBrush, WarningThickness);
        for (var index = 0; index < parts.Length; index++)
        {
            if (!WarningMarkedMorphIndices.Contains(index) || parts[index].Children.FirstOrDefault() is not Control form ||
                form.TranslatePoint(new Point(0, form.Bounds.Height), this) is not { } bottomLeft ||
                form.TranslatePoint(new Point(form.Bounds.Width, form.Bounds.Height), this) is not { } bottomRight)
                continue;
            context.DrawLine(warningPen, bottomLeft, bottomRight);
        }
    }

    private void Rebuild()
    {
        _partsStale = false;
        PartBuildCount++;
        if (MaxVisibleMorphs < 1) throw new ArgumentOutOfRangeException(nameof(MaxVisibleMorphs));
        if (WrapParts && _parts is not WrapPanel || !WrapParts && _parts is not StackPanel)
        {
            _parts = WrapParts
                ? new WrapPanel { Orientation = Orientation.Horizontal }
                : new StackPanel { Orientation = Orientation.Horizontal };
            Child = _parts;
        }
        var morphs = Morphs ?? [];
        var limit = OverflowRequest is null ? morphs.Count : Math.Min(MaxVisibleMorphs, morphs.Count);
        var visible = morphs.Take(limit).ToArray();
        if (InlineParts)
        {
            _parts.Children.Clear();
            for (var index = 0; index < visible.Length; index++)
            {
                var morph = visible[index];
                _parts.Children.Add(InlinePart(morph, index, form: true));
                _parts.Children.Add(InlinePart(morph, index, form: false));
            }
            if (limit < morphs.Count) _parts.Children.Add(OverflowButton(morphs.Count - limit));
        }
        else
            ProgressivePanel.Populate(_parts, visible,
                (morph, index) => PartFor(morph, index, last: index == visible.Length - 1),
                (_, _) => limit < morphs.Count ? OverflowButton(morphs.Count - limit) : null);
        if (LeadingMark is not null || LeadingText is { Length: > 0 })
            _parts.Children.Insert(0, new NamedMark
            {
                Mark = LeadingMark,
                Text = LeadingText,
                Classes = { "wordPresentationLeadingMark" },
            });
    }

    private static CopyableTextBlock InlinePart(ParserReadingMorphViewModel morph, int index, bool form)
    {
        var text = new CopyableTextBlock
        {
            Text = form ? morph.Form : morph.GlossOrPlaceholder,
            TextWrapping = TextWrapping.Wrap,
            Tag = new InlineMorphemePart(index, form),
            Classes = { form ? "morphemePanelForm" : "morphemePanelGloss", "morphemePanelInline" },
        };
        SetLanguageText(text, form ? morph.FormWritingSystem : morph.GlossWritingSystem);
        return text;
    }

    private Control PartFor(ParserReadingMorphViewModel morph, int index, bool last)
    {
        var formIsLink = FormLinks && morph.HasLink;
        Control form = formIsLink ? FormLink(morph) : new CopyableTextBlock
            { Text = morph.Form, FontWeight = FontWeight.SemiBold, Classes = { "morphemePanelForm" } };
        if (form is TextBlock formText)
        {
            if (OverflowRequest is not null)
                formText.TextTrimming = TextTrimming.CharacterEllipsis;
            else
                formText.TextWrapping = TextWrapping.Wrap;
        }
        if (!formIsLink) SetLanguageText(form, morph.FormWritingSystem);
        var part = new StackPanel { Classes = { "morphemePanelPart" }, Tag = morph };
        var annotation = Annotations is { Count: > 0 } && index < Annotations.Count ? Annotations[index] : null;
        if (annotation?.IsDifferent == true) part.Classes.Add("different");
        if (!string.IsNullOrWhiteSpace(annotation?.Tooltip))
        {
            ToolTip.SetTip(form, annotation.Tooltip);
            ClearTipPlacement.SetIsEnabled(form, true);
        }
        part.Children.Add(form);
        var gloss = new CopyableTextBlock
            { Text = morph.GlossOrPlaceholder, Classes = { "morphemePanelGloss" } };
        if (OverflowRequest is not null)
            gloss.TextTrimming = TextTrimming.CharacterEllipsis;
        else
            gloss.TextWrapping = TextWrapping.Wrap;
        SetLanguageText(gloss, morph.GlossWritingSystem);
        part.Children.Add(gloss);
        if (ShowCategory && morph.Category is { Length: > 0 })
        {
            var category = new CopyableTextBlock
                { Text = morph.Category, Classes = { "morphemePanelCategory", "muted" } };
            SetLanguageText(category, morph.CategoryWritingSystem);
            part.Children.Add(category);
        }
        if (ShowLinks && morph.HasLink && !formIsLink) part.Children.Add(Link(morph, RevealLinks, CompactLinks));
        if (last) part.Classes.Add("morphemePanelPartLast");
        if (RevealLinks && morph.HasLink) part.Classes.Add("hoverReveal");
        if (morph.InspectSubject is not { } reference) return part;

        part.Focusable = true;
        part.Classes.Add("morphemePanelInspectable");
        AutomationProperties.SetName(part, $"Inspect {morph.Form}");
        ToolTip.SetTip(part, $"Show {morph.Form} in the inspector");
        part.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left || e.Source is Visual source &&
                source.FindAncestorOfType<HyperlinkButton>(includeSelf: true) is not null)
                return;
            part.Focus(NavigationMethod.Pointer);
            InspectLink.Request(part, reference, morph.Captured);
            e.Handled = true;
        };
        part.KeyDown += OnInspectKeyDown;
        return part;
    }

    private sealed record InlineMorphemePart(int Index, bool IsForm);

    [KeyboardShortcutHandler("Morpheme:InspectMorpheme")]
    private void OnInspectKeyDown(object? sender, KeyEventArgs e)
    {
        var entry = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Morpheme, e.Key, e.KeyModifiers);
        if (entry?.Behavior != KeyboardShortcutBehavior.InspectMorpheme ||
            sender is not StackPanel { Tag: ParserReadingMorphViewModel { InspectSubject: { } subject } morph } part ||
            !KeyboardShortcutRegistry.Allows(entry, KeyboardShortcutRegistry.IsTextInput(e.Source), hasFocusedItem: true)) return;
        InspectLink.Request(part, subject, morph.Captured);
        e.Handled = true;
    }

    private Button OverflowButton(int omitted)
    {
        var request = OverflowRequest ?? throw new InvalidOperationException("An overflow button requires its host request.");
        var source = $"{request.Producer} morphemes";
        var button = new Button
        {
            Content = $"Show {omitted} more",
            Classes = { "morphemePanelOverflow" },
        };
        AutomationProperties.SetName(button, $"Show all {Morphs?.Count ?? 0} {source}");
        ToolTip.SetTip(button, $"Show all {source}");
        button.Click += (_, _) => button.RaiseEvent(new MorphemeOverflowRequestedEventArgs(request, button));
        return button;
    }

    private static HyperlinkButton FormLink(ParserReadingMorphViewModel morph)
    {
        var words = new StackPanel
            { Orientation = Orientation.Horizontal, Classes = { "morphemePanelFormLinkWords" } };
        var form = new WritingSystemTextBlock { Text = morph.Form, Classes = { "morphemePanelFormText" } };
        SetLanguageText(form, morph.FormWritingSystem);
        words.Children.Add(form);
        words.Children.Add(new TextBlock { Text = "↗", Classes = { "morphemePanelLinkMark" } });
        var button = new HyperlinkButton
        {
            Content = words,
            NavigateUri = morph.Link,
            Classes = { "morphemePanelForm", "morphemePanelFormLink", "morphemePanelLink" },
        };
        AutomationProperties.SetName(button, morph.LinkName);
        ToolTip.SetTip(button, morph.FormLinkTip);
        return button;
    }

    private static void SetLanguageText(Control control, string? writingSystem)
    {
        WritingSystemText.SetId(control, writingSystem);
        WritingSystemText.SetStyleName(control, "Normal");
    }

    private static HyperlinkButton Link(ParserReadingMorphViewModel morph, bool reveal, bool compact)
    {
        var button = new HyperlinkButton
        {
            Content = compact ? "↗" : morph.LinkText,
            NavigateUri = morph.Link,
            Padding = new Thickness(0),
            Classes = { "morphemePanelLink" },
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

/// <summary>A comparison note for one morphology part in a word presentation.</summary>
public sealed record MorphemePanelAnnotation(bool IsDifferent, string? Tooltip);
