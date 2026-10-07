using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;

namespace SIL.Motif.App.Controls;

/// <summary>Marks language text and lets the shared text-style module apply its saved settings.</summary>
public sealed class WritingSystemText
{
    private WritingSystemText() { }

    private static readonly ConditionalWeakTable<Control, Participant> Participants = new();

    public static readonly AttachedProperty<TextStyles?> ResolverProperty =
        AvaloniaProperty.RegisterAttached<WritingSystemText, Control, TextStyles?>("Resolver", inherits: true);

    public static readonly AttachedProperty<string?> IdProperty =
        AvaloniaProperty.RegisterAttached<WritingSystemText, Control, string?>("Id");

    public static readonly AttachedProperty<string?> StyleNameProperty =
        AvaloniaProperty.RegisterAttached<WritingSystemText, Control, string?>("StyleName");

    public static readonly AttachedProperty<string?> FlowIdProperty =
        AvaloniaProperty.RegisterAttached<WritingSystemText, Control, string?>("FlowId", inherits: true);

    public static readonly AttachedProperty<bool> FlowEnabledProperty =
        AvaloniaProperty.RegisterAttached<WritingSystemText, Control, bool>("FlowEnabled");

    public static readonly AttachedProperty<bool> WindowTextProperty =
        AvaloniaProperty.RegisterAttached<WritingSystemText, Control, bool>("WindowText");

    public static readonly AttachedProperty<bool> SuppressMissingFontNoticeProperty =
        AvaloniaProperty.RegisterAttached<WritingSystemText, Control, bool>("SuppressMissingFontNotice");

    static WritingSystemText()
    {
        ResolverProperty.Changed.AddClassHandler<Control>((control, _) => Refresh(control));
        IdProperty.Changed.AddClassHandler<Control>((control, _) => Refresh(control));
        StyleNameProperty.Changed.AddClassHandler<Control>((control, _) => Refresh(control));
        FlowIdProperty.Changed.AddClassHandler<Control>((control, _) => Refresh(control));
        FlowEnabledProperty.Changed.AddClassHandler<Control>((control, _) => Refresh(control));
        WindowTextProperty.Changed.AddClassHandler<Control>((control, _) => Refresh(control));
        SuppressMissingFontNoticeProperty.Changed.AddClassHandler<Control>((control, _) => Refresh(control));
    }

    /// <summary>Sets the text-style module inherited by annotated text below <paramref name="control"/>.</summary>
    public static void SetResolver(Control control, TextStyles resolver) =>
        control.SetValue(ResolverProperty, resolver);

    /// <summary>Sets the writing-system id that selects a language-text control's saved settings.</summary>
    public static void SetId(Control control, string? value) => control.SetValue(IdProperty, value);

    /// <summary>Sets the FieldWorks style name used for a language-text control.</summary>
    public static void SetStyleName(Control control, string? value) => control.SetValue(StyleNameProperty, value);

    public static void SetFlowId(Control control, string? value) => control.SetValue(FlowIdProperty, value);
    public static void SetFlowEnabled(Control control, bool value) => control.SetValue(FlowEnabledProperty, value);
    public static void SetWindowText(Control control, bool value) => control.SetValue(WindowTextProperty, value);
    public static void SetSuppressMissingFontNotice(Control control, bool value) =>
        control.SetValue(SuppressMissingFontNoticeProperty, value);

    public static TextStyles? GetResolver(Control control) => control.GetValue(ResolverProperty);
    public static string? GetId(Control control) => control.GetValue(IdProperty);
    public static string? GetStyleName(Control control) => control.GetValue(StyleNameProperty);
    public static string? GetFlowId(Control control) => control.GetValue(FlowIdProperty);
    public static bool GetFlowEnabled(Control control) => control.GetValue(FlowEnabledProperty);
    public static bool GetWindowText(Control control) => control.GetValue(WindowTextProperty);
    public static bool GetSuppressMissingFontNotice(Control control) =>
        control.GetValue(SuppressMissingFontNoticeProperty);

    internal static PresentationTicket? CapturePresentation(Control control) =>
        Participants.TryGetValue(control, out var participant) ? participant.CapturePresentation() : null;

    internal static void ReportTextPresented(Control control) =>
        ReportTextPresented(control, CapturePresentation(control));

    internal static void ReportTextPresented(Control control, PresentationTicket? presentation)
    {
        if (presentation is { } ticket && Participants.TryGetValue(control, out var participant))
            participant.OnTextPresented(ticket);
    }

    internal readonly record struct PresentationTicket(
        object ParticipantIdentity,
        long Generation,
        TextStyles Resolver,
        long Revision,
        string? Id,
        string StyleName,
        string Text);

    private static void Refresh(Control control)
    {
        if (!Participates(control))
        {
            if (Participants.TryGetValue(control, out var previous))
            {
                previous.Dispose();
                Participants.Remove(control);
            }
            return;
        }

        Participants.GetValue(control, static participantControl => new Participant(participantControl)).Refresh();
    }

    internal static bool IsInVisibleViewport(Control control, Rect textInkBounds)
    {
        if (textInkBounds.Width <= 0 || textInkBounds.Height <= 0) return false;
        var textVisual = control switch
        {
            TextBlock => control,
            TextPresenter presenter => presenter,
            _ => control.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault(),
        };
        if (textVisual is null) return false;
        var topLevel = control.GetVisualAncestors().OfType<TopLevel>().FirstOrDefault();
        if (topLevel is null || !control.IsEffectivelyVisible || textVisual.Bounds.Width <= 0 || textVisual.Bounds.Height <= 0)
            return false;
        var visible = BoundsIn(textVisual, textInkBounds, topLevel);
        if (!TryIntersect(visible, new Rect(topLevel.Bounds.Size), out visible)) return false;
        foreach (var visual in control.GetSelfAndVisualAncestors())
        {
            Rect? clip = visual.ClipToBounds ? new Rect(visual.Bounds.Size) : null;
            if (visual.Clip is { } geometry)
                clip = clip is { } current ? Intersect(current, geometry.Bounds) : geometry.Bounds;
            if (clip is { } clipBounds &&
                !TryIntersect(visible, BoundsIn(visual, clipBounds, topLevel), out visible)) return false;
            if (ReferenceEquals(visual, topLevel)) break;
        }
        return visible.Width > 0 && visible.Height > 0;
    }

    private static Rect BoundsIn(Visual visual, Rect bounds, Visual root)
    {
        var points = new[]
        {
            bounds.TopLeft, bounds.TopRight, bounds.BottomLeft, bounds.BottomRight,
        }.Select(point => visual.TranslatePoint(point, root)).ToArray();
        if (points.Any(point => point is null)) return default;
        var resolved = points.Select(point => point!.Value).ToArray();
        var left = resolved.Min(point => point.X);
        var top = resolved.Min(point => point.Y);
        return new Rect(left, top, resolved.Max(point => point.X) - left, resolved.Max(point => point.Y) - top);
    }

    private static Rect Intersect(Rect first, Rect second)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        return right <= left || bottom <= top ? default : new Rect(left, top, right - left, bottom - top);
    }

    private static bool TryIntersect(Rect first, Rect second, out Rect result)
    {
        result = Intersect(first, second);
        return result.Width > 0 && result.Height > 0;
    }

    private static bool Participates(Control control) =>
        GetStyleName(control) is not null || GetFlowEnabled(control) || GetWindowText(control);

    private sealed class Participant : IDisposable
    {
        private readonly Control _control;
        private readonly object _presentationIdentity = new();
        private readonly HashSet<Visual> _visibilityAncestors = [];
        private readonly HashSet<ScrollViewer> _scrollAncestors = [];
        private TextStyles? _subscribedStyles;
        private long _presentationGeneration;
        private long? _queuedPresentationGeneration;
        private bool _hasDrawnForCurrentContext;
        private bool _hasPresentedForCurrentContext;
        private bool _disposed;

        public Participant(Control control)
        {
            _control = control;
            _control.AttachedToVisualTree += OnAttachedToVisualTree;
            _control.DetachedFromVisualTree += OnDetachedFromVisualTree;
            _control.PropertyChanged += OnPropertyChanged;
            if (_control.IsAttachedToVisualTree()) SubscribeToVisibilityAncestors();
        }

        public void Refresh()
        {
            if (_disposed) return;
            _presentationGeneration++;
            _queuedPresentationGeneration = null;
            _hasDrawnForCurrentContext = false;
            _hasPresentedForCurrentContext = false;
            SynchronizeContextSubscription();
            var resolver = GetResolver(_control);
            if (resolver is null)
            {
                ClearStyle();
                return;
            }

            if (GetStyleName(_control) is { } styleName)
                resolver.Apply(_control, TextStyleRequest.Linguistic(GetId(_control), styleName));
            else
                ClearTypography();

            if (GetWindowText(_control)) resolver.ApplyWindowDirection(_control);
            else if (GetFlowEnabled(_control))
                resolver.ApplyFlowDirection(_control, TextStyleRequest.Linguistic(GetFlowId(_control)));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CancelPresentation();
            UnsubscribeFromContext();
            _control.AttachedToVisualTree -= OnAttachedToVisualTree;
            _control.DetachedFromVisualTree -= OnDetachedFromVisualTree;
            _control.PropertyChanged -= OnPropertyChanged;
            UnsubscribeFromVisibilityAncestors();
            ClearStyle();
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            SubscribeToVisibilityAncestors();
            Refresh();
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            CancelPresentation();
            UnsubscribeFromContext();
            _hasDrawnForCurrentContext = false;
            UnsubscribeFromVisibilityAncestors();
        }

        private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != Visual.IsVisibleProperty) return;
            if (_control.IsEffectivelyVisible) QueuePresentationCheck(_presentationGeneration);
            else CancelPresentationCheck();
        }

        private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == TextBlock.TextProperty || e.Property == TextBox.TextProperty)
            {
                _presentationGeneration++;
                _queuedPresentationGeneration = null;
                _hasDrawnForCurrentContext = false;
                _hasPresentedForCurrentContext = false;
            }
            else if (e.Property == Visual.IsVisibleProperty)
            {
                if (_control.IsEffectivelyVisible)
                {
                    QueuePresentationCheck(_presentationGeneration);
                }
                else CancelPresentationCheck();
            }
        }

        private void OnContextChanged(object? sender, EventArgs e) => Refresh();

        private void SynchronizeContextSubscription()
        {
            var next = _control.IsAttachedToVisualTree() ? GetResolver(_control) : null;
            if (ReferenceEquals(next, _subscribedStyles)) return;
            UnsubscribeFromContext();
            _subscribedStyles = next;
            if (_subscribedStyles is not null) _subscribedStyles.ContextChanged += OnContextChanged;
        }

        private void UnsubscribeFromContext()
        {
            if (_subscribedStyles is null) return;
            _subscribedStyles.ContextChanged -= OnContextChanged;
            _subscribedStyles = null;
        }

        private void SubscribeToVisibilityAncestors()
        {
            foreach (var ancestor in _control.GetVisualAncestors())
            {
                if (_visibilityAncestors.Add(ancestor)) ancestor.PropertyChanged += OnAncestorPropertyChanged;
                if (ancestor is ScrollViewer scroll && _scrollAncestors.Add(scroll))
                    scroll.ScrollChanged += OnScrollChanged;
            }
        }

        private void UnsubscribeFromVisibilityAncestors()
        {
            foreach (var ancestor in _visibilityAncestors) ancestor.PropertyChanged -= OnAncestorPropertyChanged;
            _visibilityAncestors.Clear();
            foreach (var scroll in _scrollAncestors) scroll.ScrollChanged -= OnScrollChanged;
            _scrollAncestors.Clear();
        }

        private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) =>
            QueuePresentationCheck(_presentationGeneration);

        internal PresentationTicket? CapturePresentation()
        {
            var text = TextOf(_control);
            if (_disposed || GetSuppressMissingFontNotice(_control) || !_control.IsAttachedToVisualTree() ||
                GetStyleName(_control) is not { } styleName || GetResolver(_control) is not { } resolver ||
                string.IsNullOrWhiteSpace(text)) return null;
            return new PresentationTicket(_presentationIdentity, _presentationGeneration, resolver, resolver.Revision,
                GetId(_control), styleName, text);
        }

        internal void OnTextPresented(PresentationTicket ticket)
        {
            if (!Matches(ticket) || !_control.IsAttachedToVisualTree()) return;
            _hasDrawnForCurrentContext = true;
            QueuePresentationCheck(ticket.Generation);
        }

        private bool Matches(PresentationTicket ticket) =>
            !_disposed && !GetSuppressMissingFontNotice(_control) &&
            ReferenceEquals(ticket.ParticipantIdentity, _presentationIdentity) &&
            ticket.Generation == _presentationGeneration &&
            ticket.Revision == ticket.Resolver.Revision && ReferenceEquals(ticket.Resolver, GetResolver(_control)) &&
            string.Equals(ticket.StyleName, GetStyleName(_control), StringComparison.Ordinal) &&
            string.Equals(ticket.Id, GetId(_control), StringComparison.Ordinal) &&
            string.Equals(ticket.Text, TextOf(_control), StringComparison.Ordinal);

        private void QueuePresentationCheck(long generation)
        {
            if (_disposed || !_hasDrawnForCurrentContext || _hasPresentedForCurrentContext ||
                _queuedPresentationGeneration == generation ||
                !_control.IsAttachedToVisualTree() || !_control.IsEffectivelyVisible ||
                GetStyleName(_control) is not { } styleName || GetResolver(_control) is not { } resolver ||
                string.IsNullOrWhiteSpace(TextOf(_control))) return;
            var id = GetId(_control);
            var text = TextOf(_control)!;
            var revision = resolver.Revision;
            _queuedPresentationGeneration = generation;
            Action checkPresentation = () =>
            {
                if (_queuedPresentationGeneration == generation) _queuedPresentationGeneration = null;
                if (_disposed || generation != _presentationGeneration ||
                    !_control.IsAttachedToVisualTree() || !_control.IsEffectivelyVisible ||
                    GetSuppressMissingFontNotice(_control) ||
                    revision != resolver.Revision ||
                    !ReferenceEquals(resolver, GetResolver(_control)) ||
                    !string.Equals(styleName, GetStyleName(_control), StringComparison.Ordinal) ||
                    !string.Equals(id, GetId(_control), StringComparison.Ordinal) ||
                    !string.Equals(text, TextOf(_control), StringComparison.Ordinal)) return;
                var metrics = resolver.GetLineMetrics(_control);
                if (metrics is null || !IsInVisibleViewport(_control, metrics.Value.InkBounds)) return;
                _hasPresentedForCurrentContext = true;
                resolver.RegisterMissingFontNotice(_control, TextStyleRequest.Linguistic(id, styleName), text);
            };
            if (resolver.PresentationCheckScheduler is { } schedule) schedule(checkPresentation);
            else Dispatcher.UIThread.Post(checkPresentation, DispatcherPriority.Background);
        }

        private void CancelPresentation()
        {
            _presentationGeneration++;
            _queuedPresentationGeneration = null;
            _hasPresentedForCurrentContext = false;
        }

        private void CancelPresentationCheck()
        {
            _presentationGeneration++;
            _queuedPresentationGeneration = null;
        }

        private void ClearStyle()
        {
            ClearTypography();
            _control.ClearValue(Visual.FlowDirectionProperty);
        }

        private void ClearTypography()
        {
            _control.ClearValue(TextElement.FontFamilyProperty);
            _control.ClearValue(TextElement.FontFeaturesProperty);
            _control.ClearValue(TextElement.FontSizeProperty);
        }

        private static string? TextOf(Control control) => control switch
        {
            TextBlock textBlock => textBlock.Text,
            TextBox textBox => textBox.Text,
            _ => null,
        };
    }
}
